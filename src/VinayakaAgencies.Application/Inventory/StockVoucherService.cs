using System.Globalization;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>
/// Posts manual inventory documents atomically: validate, value, post the accounting voucher
/// through the journal engine, append unified-ledger rows, move serials — or touch nothing
/// (phase-2-inventory.md §2, checkpoints C2.1–C2.3, C2.5).
/// </summary>
/// <remarks>
/// The negative-stock policy (<c>open-questions.md</c> #20) is warn: predicted negatives come back
/// as warnings from <see cref="ValidateAsync"/> and block <see cref="PostAsync"/> until the caller
/// confirms them. Serial availability is never a warning — a serial that is not physically in the
/// line's godown always blocks, because a confirmation cannot conjure a unit that was never received.
/// </remarks>
public sealed class StockVoucherService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    IVoucherPostingService voucherPostingService,
    IStockValuationService valuationService) : IStockVoucherService
{
    /// <summary>The system voucher type every stock movement is numbered and posted under.</summary>
    public const string StockJournalVoucherTypeName = "Stock Journal";

    /// <summary>The seeded §4.5 control account stock value must reconcile to (checkpoint C2.1).</summary>
    public const string InventoryControlLedgerName = "Stock-in-Hand";

    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly IVoucherPostingService _voucherPostingService = voucherPostingService
        ?? throw new ArgumentNullException(nameof(voucherPostingService));

    private readonly IStockValuationService _valuationService = valuationService
        ?? throw new ArgumentNullException(nameof(valuationService));

    /// <inheritdoc />
    public async Task<StockValidationResult> ValidateAsync(
        PostStockVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var (errors, warnings, _) = await ValidateCoreAsync(companyId, request, cancellationToken)
            .ConfigureAwait(false);

        return new StockValidationResult(errors, warnings);
    }

    /// <inheritdoc />
    public async Task<PostedStockVoucherSummary> PostAsync(
        PostStockVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var (errors, warnings, resolved) = await ValidateCoreAsync(companyId, request, cancellationToken)
            .ConfigureAwait(false);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        if (warnings.Count > 0 && !request.ConfirmNegativeStockWarnings)
        {
            throw new InvalidOperationException(
                "This voucher would take stock below zero somewhere. Confirm to post anyway:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, warnings));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var voucherType = await RequireStockJournalVoucherTypeAsync(companyId, cancellationToken)
            .ConfigureAwait(false);

        var voucher = new StockVoucher(companyId, request.Purpose, request.VoucherDate, request.Narration);
        voucher.SetCounterLedger(request.CounterLedgerId);

        // One ordered pass builds the aggregate and keeps every domain line paired with the plan it
        // came from; valuation, ledger rows and serials all work off these pairs.
        var pairs = new List<(PlannedLine Plan, StockVoucherLine Line)>(resolved.Lines.Count);

        foreach (var plan in resolved.Lines)
        {
            var line = voucher.AddLine(
                plan.Direction,
                plan.Item.Id,
                plan.GodownId,
                plan.Quantity,
                plan.Item.IsSerialTracked,
                plan.Item.IsBatchTracked,
                plan.ReceiptUnitCost,
                plan.BatchNumber);

            // Issue lines always carry an engine-derived cost; transfer receipts carry the value
            // their issue counterpart took out (C2.5). Receipts keep what the operator entered.
            if (plan.Direction == StockMovementDirection.Decrease || plan.ValuedUnitCostMinor.HasValue)
            {
                line.ApplyValuedCost(plan.ValuedUnitCostMinor!.Value);
            }

            pairs.Add((plan, line));
        }

        // A half-entered transfer would silently create or destroy stock; the shape check runs
        // before anything is written (checkpoint C2.5 depends on it).
        voucher.ValidateTransferBalance();

        var posted = await _voucherPostingService.PostAsync(
            new PostVoucherRequest(
                voucherType.Id,
                request.VoucherDate,
                BuildAccountingLines(resolved),
                Narration: ComposeNarration(request),
                ReferenceNumber: null,
                ReferenceDate: null,
                ManualNumber: null),
            cancellationToken).ConfigureAwait(false);

        voucher.AttachPostedVoucher(posted.VoucherId, posted.Number);
        await _unitOfWork.Repository<StockVoucher>()
            .AddAsync(voucher, cancellationToken)
            .ConfigureAwait(false);

        var nextSequence = await NextGlobalSequenceAsync(companyId, cancellationToken).ConfigureAwait(false);

        foreach (var (plan, line) in pairs)
        {
            var kind = ResolveKind(request.Purpose, line.Direction);

            await _unitOfWork.Repository<StockLedgerEntry>().AddAsync(
                new StockLedgerEntry(
                    companyId,
                    nextSequence++,
                    request.VoucherDate,
                    kind,
                    line.ItemId,
                    line.GodownId,
                    line.Quantity,
                    line.UnitCostMinor,
                    voucher.Id,
                    line.Id,
                    line.BatchNumber),
                cancellationToken).ConfigureAwait(false);

            await ApplySerialLifecycleAsync(voucher, line, plan, resolved, cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // The stock document's number IS the posted voucher's SJ number: one movement, one number.
        return Summarise(voucher, posted.Number, resolved, pairs);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostedStockVoucherSummary>> ListRecentAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var vouchers = await _unitOfWork.Repository<StockVoucher>()
            .ListAsync(new RecentVouchers(companyId, take), cancellationToken)
            .ConfigureAwait(false);

        if (vouchers.Count == 0)
        {
            return [];
        }

        var voucherIds = vouchers.Select(voucher => voucher.Id).ToList();

        var lines = await _unitOfWork.Repository<StockVoucherLine>()
            .ListAsync(new LinesOfVouchers(voucherIds), cancellationToken)
            .ConfigureAwait(false);

        var itemNames = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id, item => item.Name);

        var godownNames = (await _unitOfWork.Repository<Godown>()
                .ListAsync(new GodownsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(godown => godown.Id, godown => godown.Name);

        return vouchers
            .Select(voucher =>
            {
                var voucherLines = lines
                    .Where(line => line.StockVoucherId == voucher.Id)
                    .OrderBy(line => line.LineNumber)
                    .ToList();

                return new PostedStockVoucherSummary(
                    voucher.Id,
                    voucher.PostedNumber ?? string.Empty,
                    voucher.Purpose,
                    voucher.VoucherDate,
                    voucher.PostedVoucherId,
                    voucher.CounterLedgerId,
                    voucher.Narration,
                    voucherLines.Sum(LineValueMinor),
                    voucherLines
                        .Select(line => new PostedStockVoucherLineSummary(
                            line.LineNumber,
                            line.Direction,
                            line.ItemId,
                            itemNames.GetValueOrDefault(line.ItemId, "?"),
                            line.GodownId,
                            godownNames.GetValueOrDefault(line.GodownId, "?"),
                            line.Quantity,
                            MoneyScale.FromMinor(line.UnitCostMinor),
                            line.BatchNumber))
                        .ToList());
            })
            .ToList();
    }

    /// <summary>
    /// One movement after kit expansion: what actually reaches the ledger. Issues of kits never do —
    /// their components deplete instead (phase-2-inventory.md §2).
    /// </summary>
    /// <param name="ValuedUnitCostMinor">
    /// The issue-side cost assigned by the valuation engine; null until valued and on receipt lines,
    /// which carry the operator's <see cref="ReceiptUnitCost"/>.
    /// </param>
    private sealed record PlannedLine(
        int Ordinal,
        StockMovementDirection Direction,
        Item Item,
        Guid GodownId,
        decimal Quantity,
        decimal ReceiptUnitCost,
        string? BatchNumber,
        IReadOnlyList<string>? SerialNumbers)
    {
        public long? ValuedUnitCostMinor { get; init; }

        /// <summary>The minor-unit cost this line books: receipts carry what was paid.</summary>
        public long CostMinor =>
            Direction == StockMovementDirection.Increase
                ? MoneyScale.ToMinor(ReceiptUnitCost)
                : ValuedUnitCostMinor ?? 0L;
    }

    private sealed record ResolvedVoucher(
        StockVoucherPurpose Purpose,
        DateOnly VoucherDate,
        Ledger? CounterLedger,
        Ledger ControlLedger,
        IReadOnlyDictionary<Guid, string> GodownNamesById,
        IReadOnlyList<PlannedLine> Lines);

    /// <summary>
    /// Everything checkable without writing: existence, activity, purpose shapes, serial
    /// availability (C2.3), batch presence, the costing of every issue side against the ledger as it
    /// stands, transfer-receipt costs (C2.5) and the negative-stock projection (#20).
    /// </summary>
    private async Task<(List<string> Errors, List<string> Warnings, ResolvedVoucher Resolved)>
        ValidateCoreAsync(
            Guid companyId,
            PostStockVoucherRequest request,
            CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (request.Lines.Count == 0)
        {
            errors.Add("A stock voucher needs at least one line.");
        }

        var counterLedger = default(Ledger);

        if (request.Purpose == StockVoucherPurpose.Transfer)
        {
            if (request.CounterLedgerId.HasValue)
            {
                errors.Add("A transfer has no non-stock leg; leave the counter ledger empty.");
            }
        }
        else if (!request.CounterLedgerId.HasValue)
        {
            errors.Add($"A {request.Purpose} voucher must name the ledger its value comes from or goes to.");
        }
        else
        {
            counterLedger = await RequireLedgerAsync(companyId, request.CounterLedgerId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (!counterLedger.IsActive || !counterLedger.AllowsManualPosting)
            {
                errors.Add($"'{counterLedger.Name}' cannot take this posting.");
            }
        }

        var controlLedger = await RequireControlLedgerAsync(companyId, cancellationToken)
            .ConfigureAwait(false);

        var itemsById = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id);

        var godownsById = (await _unitOfWork.Repository<Godown>()
                .ListAsync(new GodownsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(godown => godown.Id);

        foreach (var line in request.Lines)
        {
            if (!itemsById.TryGetValue(line.ItemId, out var item))
            {
                errors.Add($"Item '{line.ItemId}' does not exist in this company's books.");
                continue;
            }

            if (!item.IsActive)
            {
                errors.Add($"Item '{item.Name}' is inactive and cannot move.");
            }

            if (!godownsById.TryGetValue(line.GodownId, out var godown))
            {
                errors.Add($"Godown '{line.GodownId}' does not exist in this company's books.");
                continue;
            }

            if (!godown.IsActive)
            {
                errors.Add($"Godown '{godown.Name}' is inactive and cannot move stock.");
            }

            if (item.IsKit && request.Purpose != StockVoucherPurpose.Issue)
            {
                errors.Add(
                    $"'{item.Name}' is a kit; receive its components instead — a kit has no stock "
                    + "of its own.");
            }
        }

        var plans = await ExpandKitsAsync(request, itemsById, errors, cancellationToken)
            .ConfigureAwait(false);

        await ValueIssuesAsync(plans, request.VoucherDate, warnings, cancellationToken)
            .ConfigureAwait(false);

        if (request.Purpose == StockVoucherPurpose.Transfer)
        {
            AssignTransferReceiptCosts(plans);
        }

        await CheckSerialsAndBatchesAsync(
                companyId,
                request,
                plans,
                godownsById,
                errors,
                cancellationToken)
            .ConfigureAwait(false);

        await ProjectNegativeStockAsync(companyId, plans, godownsById, warnings, cancellationToken)
            .ConfigureAwait(false);

        return (
            errors,
            warnings,
            new ResolvedVoucher(
                request.Purpose,
                request.VoucherDate,
                counterLedger,
                controlLedger,
                godownsById.ToDictionary(pair => pair.Key, pair => pair.Value.Name),
                plans));
    }

    /// <summary>
    /// Expands kit-issue lines into component lines, preserving order so later stages can rely on
    /// <see cref="PlannedLine.Ordinal"/>.
    /// </summary>
    private async Task<List<PlannedLine>> ExpandKitsAsync(
        PostStockVoucherRequest request,
        Dictionary<Guid, Item> itemsById,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var plans = new List<PlannedLine>(request.Lines.Count);
        var ordinal = 0;

        foreach (var line in request.Lines)
        {
            ordinal++;

            if (!itemsById.TryGetValue(line.ItemId, out var item))
            {
                continue;
            }

            if (!(item.IsKit && request.Purpose == StockVoucherPurpose.Issue))
            {
                plans.Add(new PlannedLine(
                    ordinal,
                    line.Direction,
                    item,
                    line.GodownId,
                    line.Quantity,
                    line.UnitCost,
                    string.IsNullOrWhiteSpace(line.BatchNumber) ? null : line.BatchNumber.Trim(),
                    line.SerialNumbers));

                continue;
            }

            var components = await _unitOfWork.Repository<ItemKitComponent>()
                .ListAsync(new ComponentsOfKit(item.Id), cancellationToken)
                .ConfigureAwait(false);

            if (components.Count == 0)
            {
                errors.Add(
                    $"'{item.Name}' is a kit with no components configured; set them on the item "
                    + "master first.");
                continue;
            }

            foreach (var component in components)
            {
                if (!itemsById.TryGetValue(component.ComponentItemId, out var componentItem))
                {
                    errors.Add(
                        $"Kit component '{component.ComponentItemId}' of '{item.Name}' does not exist.");
                    continue;
                }

                if (componentItem.IsSerialTracked)
                {
                    errors.Add(
                        $"'{componentItem.Name}' is a serial-tracked component of kit '{item.Name}'. "
                        + "Issue it directly with its serials; kit expansion cannot guess which units "
                        + "to take.");
                    continue;
                }

                plans.Add(new PlannedLine(
                    ordinal,
                    StockMovementDirection.Decrease,
                    componentItem,
                    line.GodownId,
                    line.Quantity * component.Quantity,
                    0m,
                    null,
                    null));
            }
        }

        return plans;
    }

    /// <summary>
    /// Costs every decrease line against the ledger as it stands before the voucher, carrying each
    /// item's earlier lines of this same voucher as prior consumption so a second line is costed on
    /// what genuinely remains.
    /// </summary>
    private async Task ValueIssuesAsync(
        List<PlannedLine> plans,
        DateOnly voucherDate,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var issuedByItem = new Dictionary<Guid, decimal>();

        foreach (var index in Enumerable.Range(0, plans.Count))
        {
            var plan = plans[index];

            if (plan.Direction != StockMovementDirection.Decrease)
            {
                continue;
            }

            var prior = issuedByItem.TryGetValue(plan.Item.Id, out var seen) ? seen : 0m;

            var valued = await _valuationService.ValueIssueAsync(
                    plan.Item.Id,
                    plan.Quantity,
                    voucherDate,
                    prior,
                    cancellationToken)
                .ConfigureAwait(false);

            warnings.AddRange(valued.Warnings);
            issuedByItem[plan.Item.Id] = prior + plan.Quantity;

            plans[index] = plan with { ValuedUnitCostMinor = valued.UnitCostMinor };
        }
    }

    /// <summary>
    /// Lands each transfer's receipt side at exactly the value its issue side took out
    /// (checkpoint C2.5): per item, the OUT lines' total value is distributed across the IN lines
    /// by quantity, and the last IN line solves for the unit cost that makes the sum exact.
    /// </summary>
    private static void AssignTransferReceiptCosts(List<PlannedLine> plans)
    {
        // Materialised up front: the body replaces plans in place, and a lazy enumeration over the
        // list it mutates would throw mid-transfer.
        var transferItemIds = plans
            .Where(plan => plan.Direction == StockMovementDirection.Increase)
            .Select(plan => plan.Item.Id)
            .Distinct()
            .ToList();

        foreach (var itemId in transferItemIds)
        {
            var outs = plans
                .Where(plan => plan.Direction == StockMovementDirection.Decrease && plan.Item.Id == itemId)
                .ToList();

            var ins = plans
                .Select((plan, index) => (plan, index))
                .Where(pair => pair.plan.Direction == StockMovementDirection.Increase
                    && pair.plan.Item.Id == itemId)
                .OrderBy(pair => pair.index)
                .ToList();

            if (ins.Count == 0 || outs.Count == 0)
            {
                continue;
            }

            var outTotal = outs.Sum(out_ =>
                StockLedgerEntry.ComputeValueMinor(out_.Quantity, out_.ValuedUnitCostMinor ?? 0L));

            var totalInQuantity = ins.Sum(pair => pair.plan.Quantity);

            if (totalInQuantity <= 0m)
            {
                continue;
            }

            long allocated = 0;

            for (var position = 0; position < ins.Count; position++)
            {
                var (plan, index) = ins[position];
                var isLast = position == ins.Count - 1;

                long target;

                if (!isLast)
                {
                    target = (long)Math.Round(
                        (decimal)outTotal * MoneyScale.ToMinor(plan.Quantity)
                        / MoneyScale.ToMinor(totalInQuantity),
                        0,
                        MidpointRounding.ToEven);
                }
                else
                {
                    target = Math.Max(0L, outTotal - allocated);
                }

                var unitCost = isLast
                    ? SolveUnitCostForTarget(plan.Quantity, target)
                    : StockLedgerEntry.UnitCostFromValue(target, plan.Quantity);

                allocated += StockLedgerEntry.ComputeValueMinor(plan.Quantity, unitCost);

                plans[index] = plan with { ValuedUnitCostMinor = unitCost };
            }
        }
    }

    /// <summary>
    /// Finds the unit cost whose quantity × cost rounds to exactly the target value, scanning
    /// around the estimate. Fractional quantities can make some targets unrepresentable; the scan
    /// then returns the closest cost, leaving at most half a minor unit of drift in a case that
    /// whole-unit trading never produces.
    /// </summary>
    private static long SolveUnitCostForTarget(decimal quantity, long targetValueMinor)
    {
        var estimate = StockLedgerEntry.UnitCostFromValue(targetValueMinor, quantity);

        for (var delta = 0L; delta <= 4L; delta++)
        {
            foreach (var candidate in new[] { estimate - delta, estimate + delta })
            {
                if (candidate >= 0
                    && StockLedgerEntry.ComputeValueMinor(quantity, candidate) == targetValueMinor)
                {
                    return candidate;
                }
            }
        }

        return Math.Max(0L, estimate);
    }

    /// <summary>
    /// Serial availability (C2.3) and batch presence, on the expanded lines. A serial must exist,
    /// be in stock, and sit in the issuing godown; transfers additionally need one destination
    /// godown per serial-tracked item so units can be followed.
    /// </summary>
    private async Task CheckSerialsAndBatchesAsync(
        Guid companyId,
        PostStockVoucherRequest request,
        IReadOnlyList<PlannedLine> plans,
        Dictionary<Guid, Godown> godownsById,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var seenIncreaseSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            var godownName = godownsById.TryGetValue(plan.GodownId, out var godown)
                ? godown.Name
                : plan.GodownId.ToString();

            // Every stock movement posts exactly one accounting voucher through the journal engine,
            // which refuses zero-amount lines — so a receipt must carry what the goods cost.
            // Transfer receipts are exempt: their cost is derived from the issue side (C2.5).
            if (request.Purpose != StockVoucherPurpose.Transfer
                && plan.Direction == StockMovementDirection.Increase
                && plan.ReceiptUnitCost <= 0m)
            {
                errors.Add(
                    $"'{plan.Item.Name}' into {godownName}: a receipt needs a unit cost above zero, "
                    + "or it would reach the books carrying nothing.");
            }

            if (plan.Direction == StockMovementDirection.Increase
                && plan.Item.IsBatchTracked
                && string.IsNullOrWhiteSpace(plan.BatchNumber))
            {
                errors.Add($"A receipt of '{plan.Item.Name}' into {godownName} must carry its batch number.");
            }

            if (!plan.Item.IsSerialTracked)
            {
                continue;
            }

            var serials = plan.SerialNumbers ?? [];
            var expected = (int)plan.Quantity;

            if (plan.Quantity != decimal.Truncate(plan.Quantity) || serials.Count != expected)
            {
                errors.Add(
                    $"'{plan.Item.Name}' is serial-tracked: list exactly {expected} serial number(s) "
                    + $"for {plan.Quantity.ToString("0.####", CultureInfo.InvariantCulture)} unit(s).");
                continue;
            }

            if (plan.Direction == StockMovementDirection.Increase)
            {
                foreach (var serial in serials)
                {
                    var key = NameNormaliser.Normalise(serial);

                    if (!seenIncreaseSerials.Add(key))
                    {
                        errors.Add(
                            $"Serial '{serial}' appears more than once among this voucher's receipts.");
                    }

                    var existing = await FindSerialAsync(companyId, plan.Item.Id, key, cancellationToken)
                        .ConfigureAwait(false);

                    if (existing is not null)
                    {
                        errors.Add(
                            $"Serial '{serial}' of '{plan.Item.Name}' already exists in the books — "
                            + "a serial can be received only once.");
                    }
                }

                continue;
            }

            Guid destinationGodownId = default;

            if (request.Purpose == StockVoucherPurpose.Transfer)
            {
                var destinations = plans
                    .Where(candidate => candidate.Direction == StockMovementDirection.Increase
                        && candidate.Item.Id == plan.Item.Id)
                    .Select(candidate => candidate.GodownId)
                    .Distinct()
                    .ToList();

                if (destinations.Count != 1)
                {
                    errors.Add(
                        $"'{plan.Item.Name}' is serial-tracked: a transfer needs exactly one "
                        + "destination godown for the item, so the units can be followed. Split the "
                        + "voucher instead.");
                    continue;
                }

                destinationGodownId = destinations[0];
            }

            foreach (var serial in serials)
            {
                var existing = await FindSerialAsync(
                        companyId,
                        plan.Item.Id,
                        NameNormaliser.Normalise(serial),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    errors.Add(
                        $"Serial '{serial}' of '{plan.Item.Name}' is not in the books. It cannot be "
                        + "issued or transferred because it was never received (checkpoint C2.3).");
                    continue;
                }

                if (existing.Status != SerialNumberStatus.InStock)
                {
                    errors.Add(
                        $"Serial '{serial}' of '{plan.Item.Name}' was already sold on "
                        + $"{existing.SoldOn:yyyy-MM-dd}. The same serial cannot leave stock twice "
                        + "(checkpoint C2.3).");
                    continue;
                }

                if (existing.CurrentGodownId != plan.GodownId)
                {
                    var whereItIs = godownsById.TryGetValue(existing.CurrentGodownId, out var actual)
                        ? actual.Name
                        : existing.CurrentGodownId.ToString();

                    errors.Add(
                        $"Serial '{serial}' of '{plan.Item.Name}' sits in {whereItIs}, not "
                        + $"{godownName}. Pick the godown it is actually in, or move it first.");
                    continue;
                }

                if (destinationGodownId == existing.CurrentGodownId)
                {
                    errors.Add(
                        $"Serial '{serial}' of '{plan.Item.Name}' is being transferred into the "
                        + "godown it already sits in.");
                }
            }
        }
    }

    /// <summary>
    /// Applies the movements in order over the current on-hand and warns wherever a godown would go
    /// negative — the approved policy (open-questions.md #20). Serial-tracked lines are gated by
    /// availability upstream and cannot produce these.
    /// </summary>
    private async Task ProjectNegativeStockAsync(
        Guid companyId,
        IReadOnlyList<PlannedLine> plans,
        Dictionary<Guid, Godown> godownsById,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var itemIds = plans.Select(plan => plan.Item.Id).Distinct().ToList();

        if (itemIds.Count == 0)
        {
            return;
        }

        var entries = await _unitOfWork.Repository<StockLedgerEntry>()
            .ListAsync(new EntriesOfItems(companyId, itemIds), cancellationToken)
            .ConfigureAwait(false);

        var onHand = new Dictionary<Guid, Dictionary<Guid, decimal>>();

        foreach (var entry in entries)
        {
            if (!onHand.TryGetValue(entry.ItemId, out var byGodown))
            {
                byGodown = [];
                onHand[entry.ItemId] = byGodown;
            }

            byGodown[entry.GodownId] = byGodown.GetValueOrDefault(entry.GodownId) + entry.SignedQuantity;
        }

        foreach (var plan in plans)
        {
            if (!onHand.TryGetValue(plan.Item.Id, out var byGodown))
            {
                byGodown = [];
                onHand[plan.Item.Id] = byGodown;
            }

            var resulting = byGodown.GetValueOrDefault(plan.GodownId)
                + (plan.Direction == StockMovementDirection.Increase ? plan.Quantity : -plan.Quantity);

            byGodown[plan.GodownId] = resulting;

            if (resulting >= 0m)
            {
                continue;
            }

            var godownName = godownsById.TryGetValue(plan.GodownId, out var godown)
                ? godown.Name
                : plan.GodownId.ToString();

            warnings.Add(
                $"'{plan.Item.Name}' in {godownName} would fall to "
                + $"{resulting.ToString("0.####", CultureInfo.InvariantCulture)}. "
                + "Post only if this shortfall is intended.");
        }
    }

    private static List<PostVoucherLine> BuildAccountingLines(ResolvedVoucher resolved)
    {
        var lines = new List<PostVoucherLine>();

        decimal increases = 0;
        decimal decreases = 0;

        foreach (var plan in resolved.Lines.OrderBy(plan => plan.Ordinal))
        {
            var value = MoneyScale.FromMinor(
                StockLedgerEntry.ComputeValueMinor(plan.Quantity, plan.CostMinor));

            switch (resolved.Purpose)
            {
                case StockVoucherPurpose.OpeningBalance or StockVoucherPurpose.Receipt:
                    lines.Add(new PostVoucherLine(
                        resolved.ControlLedger.Id,
                        Debit: value,
                        Credit: 0m,
                        Narration: $"{plan.Item.Name} × {plan.Quantity:0.####}"));
                    increases += value;
                    break;

                case StockVoucherPurpose.Issue:
                    lines.Add(new PostVoucherLine(
                        resolved.ControlLedger.Id,
                        Debit: 0m,
                        Credit: value,
                        Narration: $"{plan.Item.Name} × {plan.Quantity:0.####}"));
                    decreases += value;
                    break;

                case StockVoucherPurpose.Transfer when plan.Direction == StockMovementDirection.Decrease:
                    decreases += value;
                    break;
            }
        }

        switch (resolved.Purpose)
        {
            case StockVoucherPurpose.OpeningBalance or StockVoucherPurpose.Receipt:
                lines.Add(new PostVoucherLine(
                    resolved.CounterLedger!.Id,
                    Debit: 0m,
                    Credit: increases,
                    Narration: "Stock received"));
                break;

            case StockVoucherPurpose.Issue:
                lines.Add(new PostVoucherLine(
                    resolved.CounterLedger!.Id,
                    Debit: decreases,
                    Credit: 0m,
                    Narration: "Stock issued"));
                break;

            case StockVoucherPurpose.Transfer:
                // Location changes, value does not (checkpoint C2.5): the pair nets to zero on the
                // control account while still showing the movement in the day book.
                lines.Add(new PostVoucherLine(
                    resolved.ControlLedger.Id,
                    Debit: decreases,
                    Credit: 0m,
                    Narration: "Stock transferred in"));
                lines.Add(new PostVoucherLine(
                    resolved.ControlLedger.Id,
                    Debit: 0m,
                    Credit: decreases,
                    Narration: "Stock transferred out"));
                break;
        }

        return lines;
    }

    /// <summary>
    /// Creates serials for receipt-side lines; sells them on issues; moves them on transfers.
    /// </summary>
    private async Task ApplySerialLifecycleAsync(
        StockVoucher voucher,
        StockVoucherLine line,
        PlannedLine plan,
        ResolvedVoucher resolved,
        CancellationToken cancellationToken)
    {
        if (plan.SerialNumbers is null || plan.SerialNumbers.Count == 0)
        {
            return;
        }

        if (line.Direction == StockMovementDirection.Increase)
        {
            foreach (var serial in plan.SerialNumbers)
            {
                await _unitOfWork.Repository<SerialNumber>().AddAsync(
                    new SerialNumber(
                        voucher.CompanyId,
                        line.ItemId,
                        serial,
                        line.GodownId,
                        line.Id,
                        voucher.VoucherDate),
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var serial in plan.SerialNumbers)
        {
            var existing = await FindSerialAsync(
                    voucher.CompanyId,
                    line.ItemId,
                    NameNormaliser.Normalise(serial),
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                throw new InvalidOperationException(
                    $"Serial '{serial}' vanished between validation and posting; nothing was committed.");
            }

            if (resolved.Purpose == StockVoucherPurpose.Transfer)
            {
                existing.MoveTo(line.GodownId);
            }
            else
            {
                existing.MarkSold(line.Id, voucher.VoucherDate);
            }

            _unitOfWork.Repository<SerialNumber>().Update(existing);
        }
    }

    private static StockMovementKind ResolveKind(
        StockVoucherPurpose purpose,
        StockMovementDirection direction) =>
        purpose switch
        {
            StockVoucherPurpose.OpeningBalance => StockMovementKind.OpeningBalance,
            StockVoucherPurpose.Receipt => StockMovementKind.Receipt,
            StockVoucherPurpose.Issue => StockMovementKind.Issue,
            StockVoucherPurpose.Transfer => direction == StockMovementDirection.Increase
                ? StockMovementKind.TransferIn
                : StockMovementKind.TransferOut,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown purpose."),
        };

    /// <summary>The value one posted line contributes to stock, in minor units.</summary>
    private static long LineValueMinor(StockVoucherLine line) =>
        StockLedgerEntry.ComputeValueMinor(line.Quantity, line.UnitCostMinor);

    private static string ComposeNarration(PostStockVoucherRequest request)
    {
        var narration = string.IsNullOrWhiteSpace(request.Narration)
            ? string.Empty
            : $" — {request.Narration.Trim()}";

        return $"Stock {request.Purpose}{narration}";
    }

    private static PostedStockVoucherSummary Summarise(
        StockVoucher voucher,
        string number,
        ResolvedVoucher resolved,
        IReadOnlyList<(PlannedLine Plan, StockVoucherLine Line)> pairs)
    {
        var lines = pairs
            .Select(pair => new PostedStockVoucherLineSummary(
                pair.Line.LineNumber,
                pair.Line.Direction,
                pair.Line.ItemId,
                pair.Plan.Item.Name,
                pair.Line.GodownId,
                resolved.GodownNamesById.GetValueOrDefault(pair.Line.GodownId, "?"),
                pair.Line.Quantity,
                MoneyScale.FromMinor(pair.Line.UnitCostMinor),
                pair.Line.BatchNumber))
            .ToList();

        return new PostedStockVoucherSummary(
            voucher.Id,
            number,
            voucher.Purpose,
            voucher.VoucherDate,
            voucher.PostedVoucherId,
            voucher.CounterLedgerId,
            voucher.Narration,
            voucher.Lines.Sum(LineValueMinor),
            lines);
    }

    private async Task<VoucherType> RequireStockJournalVoucherTypeAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(StockJournalVoucherTypeName);

        var voucherType = await _unitOfWork.Repository<VoucherType>()
            .SingleOrDefaultAsync(new VoucherTypeByName(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (voucherType is null)
        {
            throw new InvalidOperationException(
                $"The '{StockJournalVoucherTypeName}' voucher type is missing from this company's "
                + "books. Run inventory provisioning before posting stock.");
        }

        return voucherType;
    }

    private async Task<Ledger> RequireControlLedgerAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(InventoryControlLedgerName);

        var ledger = await _unitOfWork.Repository<Ledger>()
            .SingleOrDefaultAsync(new LedgerByName(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null)
        {
            throw new InvalidOperationException(
                $"The '{InventoryControlLedgerName}' control account is missing from this company's "
                + "chart of accounts, so stock movements could not reach the books.");
        }

        return ledger;
    }

    private async Task<Ledger> RequireLedgerAsync(
        Guid companyId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null || ledger.CompanyId != companyId)
        {
            throw new InvalidOperationException("That ledger does not exist in this company's books.");
        }

        return ledger;
    }

    private async Task<long> NextGlobalSequenceAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var highest = await _unitOfWork.Repository<StockLedgerEntry>()
            .SingleOrDefaultAsync(new HighestSequence(companyId), cancellationToken)
            .ConfigureAwait(false);

        return (highest?.GlobalSequence ?? 0L) + 1L;
    }

    private async Task<SerialNumber?> FindSerialAsync(
        Guid companyId,
        Guid itemId,
        string valueNormalised,
        CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<SerialNumber>()
            .SingleOrDefaultAsync(
                new SerialByKey(companyId, itemId, valueNormalised),
                cancellationToken)
            .ConfigureAwait(false);

    private sealed class RecentVouchers : Specification<StockVoucher>
    {
        public RecentVouchers(Guid companyId, int take)
        {
            // VoucherDate, not CreatedAtUtc: SQLite cannot order by a DateTimeOffset (see
            // SingleCompanyContext), and the business date is what a movement list reads by anyway.
            Where(voucher => voucher.CompanyId == companyId);
            ApplyOrderByDescending(voucher => voucher.VoucherDate);
            ApplyPaging(0, take);
            ApplyNoTracking();
        }
    }

    private sealed class LinesOfVouchers : Specification<StockVoucherLine>
    {
        public LinesOfVouchers(IReadOnlyList<Guid> voucherIds)
        {
            Where(line => voucherIds.Contains(line.StockVoucherId));
            ApplyNoTracking();
        }
    }

    private sealed class ItemsOfCompany : Specification<Item>
    {
        public ItemsOfCompany(Guid companyId)
        {
            Where(item => item.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class GodownsOfCompany : Specification<Godown>
    {
        public GodownsOfCompany(Guid companyId)
        {
            Where(godown => godown.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class ComponentsOfKit : Specification<ItemKitComponent>
    {
        public ComponentsOfKit(Guid kitItemId)
        {
            Where(component => component.KitItemId == kitItemId);
            ApplyNoTracking();
        }
    }

    private sealed class EntriesOfItems : Specification<StockLedgerEntry>
    {
        public EntriesOfItems(Guid companyId, IReadOnlyList<Guid> itemIds)
        {
            Where(entry => entry.CompanyId == companyId && itemIds.Contains(entry.ItemId));
            ApplyNoTracking();
        }
    }

    private sealed class VoucherTypeByName : Specification<VoucherType>
    {
        public VoucherTypeByName(Guid companyId, string nameNormalised)
        {
            Where(type => type.CompanyId == companyId && type.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class LedgerByName : Specification<Ledger>
    {
        public LedgerByName(Guid companyId, string nameNormalised)
        {
            Where(ledger => ledger.CompanyId == companyId && ledger.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class HighestSequence : Specification<StockLedgerEntry>
    {
        public HighestSequence(Guid companyId)
        {
            Where(entry => entry.CompanyId == companyId);
            ApplyOrderByDescending(entry => entry.GlobalSequence);
            ApplyPaging(0, 1);
            ApplyNoTracking();
        }
    }

    private sealed class SerialByKey : Specification<SerialNumber>
    {
        public SerialByKey(Guid companyId, Guid itemId, string valueNormalised)
        {
            Where(serial => serial.CompanyId == companyId
                && serial.ItemId == itemId
                && serial.ValueNormalised == valueNormalised);
            ApplyNoTracking();
        }
    }
}
