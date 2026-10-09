using System.Globalization;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Security;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Trade;

/// <summary>
/// Runs the sales and purchase pipelines: documents along the chains, atomic invoice posting,
/// conversion without re-keying, and the outstanding reports
/// (phase-3-sales-purchase-pipeline.md §1–§3).
/// </summary>
/// <remarks>
/// Exactly two kinds post — <see cref="TradeDocumentKind.TaxInvoice"/> and
/// <see cref="TradeDocumentKind.PurchaseInvoice"/> — and they post once: journal voucher, stock
/// ledger rows and serial moves inside one unit-of-work transaction (checkpoint C3.3). Every other
/// kind is data only, so a quotation touches no ledger row and no stock row by construction
/// (checkpoint C3.5) and Phase 2's stock-value-to-control-account reconciliation survives untouched.
/// <para>
/// A posted invoice moves stock through a mirror <see cref="StockVoucher"/> aggregate (purpose
/// issue or receipt) created in the same transaction. That keeps every existing foreign-key path —
/// stock-ledger rows, serial receipts and sales, valuation replay — working unchanged; the mirror's
/// counter-ledger field records the party it moved against but posts nothing itself.
/// </para>
/// </remarks>
public sealed class TradeDocumentService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    IVoucherNumberAllocator numberAllocator,
    IVoucherPostingService voucherPostingService,
    IStockValuationService valuationService,
    IGstComputationService taxComputationService,
    IPermissionAuthority permissions) : ITradeDocumentService
{
    /// <summary>The seeded control account stock value reconciles to (checkpoint C2.1).</summary>
    public const string ControlLedgerName = "Stock-in-Hand";

    /// <summary>The seeded income account a tax invoice credits.</summary>
    public const string SalesLedgerName = "Sales";

    /// <summary>The seeded expense account a purchase invoice's non-stock value lands on.</summary>
    public const string PurchasesLedgerName = "Purchases";

    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    // Phase 9 §4a D4: the price-override right now has a holder rather than being on or off for
    // everybody. The Sales setting stays the company-wide switch; this is the per-person gate.
    private readonly IPermissionAuthority _permissions = permissions
        ?? throw new ArgumentNullException(nameof(permissions));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly IVoucherNumberAllocator _numberAllocator = numberAllocator
        ?? throw new ArgumentNullException(nameof(numberAllocator));

    private readonly IVoucherPostingService _voucherPostingService = voucherPostingService
        ?? throw new ArgumentNullException(nameof(voucherPostingService));

    private readonly IStockValuationService _valuationService = valuationService
        ?? throw new ArgumentNullException(nameof(valuationService));

    private readonly IGstComputationService _taxComputationService = taxComputationService
        ?? throw new ArgumentNullException(nameof(taxComputationService));

    /// <summary>Where each kind may be carried forward to. Anything else refuses.</summary>
    private static IReadOnlyList<TradeDocumentKind> AllowedTargets(TradeDocumentKind source) => source switch
    {
        TradeDocumentKind.Quotation =>
            [TradeDocumentKind.SalesOrder, TradeDocumentKind.TaxInvoice],
        TradeDocumentKind.SalesOrder =>
            [TradeDocumentKind.DeliveryChallan, TradeDocumentKind.TaxInvoice],
        TradeDocumentKind.DeliveryChallan =>
            [TradeDocumentKind.TaxInvoice],
        TradeDocumentKind.PurchaseOrder =>
            [TradeDocumentKind.GoodsReceipt, TradeDocumentKind.PurchaseInvoice],
        TradeDocumentKind.GoodsReceipt =>
            [TradeDocumentKind.PurchaseInvoice],
        _ => [],
    };

    /// <inheritdoc />
    public async Task<TradeDocumentSummary> CreateAsync(
        SaveTradeDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var resolved = await ValidateHeaderAndLinesAsync(companyId, request, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.Errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, resolved.Errors));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var document = BuildDocument(companyId, request, resolved.ItemsById);

        // Every kind except the two invoices claims its number here, inside the save. An invoice
        // drafts unnumbered on purpose: its number comes from the journal engine at posting time,
        // so one document consumes exactly one number from its series.
        if (!document.StockMoving)
        {
            var allocated = await AllocateNumberAsync(
                    companyId,
                    request.Kind,
                    request.DocumentDate,
                    cancellationToken)
                .ConfigureAwait(false);

            document.AttachNumber(allocated.Number);
        }

        await _unitOfWork.Repository<TradeDocument>()
            .AddAsync(document, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await SummariseWithLookupsAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TradeDocumentSummary> UpdateDraftAsync(
        Guid documentId,
        SaveTradeDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var document = await RequireDocumentAsync(documentId, companyId, cancellationToken)
            .ConfigureAwait(false);

        if (!document.CanEdit)
        {
            throw new InvalidOperationException(
                $"A {document.Kind} in status {document.Status} cannot be revised. Only drafts "
                + "and open documents may change.");
        }

        var resolved = await ValidateHeaderAndLinesAsync(companyId, request, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.Errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, resolved.Errors));
        }

        document.SetDocumentDate(request.DocumentDate);
        document.SetParty(request.PartyLedgerId);
        document.SetNarration(request.Narration);
        document.SetPartyReference(request.PartyReferenceNumber, request.PartyReferenceDate);
        document.SetPartyGstin(request.PartyGstin);
        document.SetReverseCharge(request.IsReverseCharge);

        // The previous lines are removed explicitly — Restrict delete behaviour deliberately has no
        // orphan sweep — then the replacement set goes through the aggregate, which enforces the
        // kind's shape afresh.
        foreach (var previous in await _unitOfWork.Repository<TradeDocumentLine>()
                     .ListAsync(new LinesOfDocuments([documentId]), cancellationToken)
                     .ConfigureAwait(false))
        {
            _unitOfWork.Repository<TradeDocumentLine>().Remove(previous);
        }

        document.ResetLines(ToLineInputs(request.Lines, resolved.ItemsById));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await SummariseWithLookupsAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        Guid documentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var document = await RequireDocumentAsync(documentId, companyId, cancellationToken)
            .ConfigureAwait(false);

        if (!document.CanEdit)
        {
            throw new InvalidOperationException(
                $"A {document.Kind} in status {document.Status} cannot be deleted. Only drafts and "
                + "open documents may be withdrawn, and a posted invoice is reversed through the "
                + "journal engine instead.");
        }

        if (await _unitOfWork.Repository<TradeDocument>()
                .AnyAsync(new ConvertedFrom(documentId), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"This {document.Kind} has already been carried forward into another document; "
                + "delete the successors first.");
        }

        foreach (var line in await _unitOfWork.Repository<TradeDocumentLine>()
                     .ListAsync(new LinesOfDocuments([documentId]), cancellationToken)
                     .ConfigureAwait(false))
        {
            _unitOfWork.Repository<TradeDocumentLine>().Remove(line);
        }

        _unitOfWork.Repository<TradeDocument>().Remove(document);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TradeDocumentSummary?> GetAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var document = await _unitOfWork.Repository<TradeDocument>()
            .SingleOrDefaultAsync(new DocumentWithLines(documentId), cancellationToken)
            .ConfigureAwait(false);

        if (document is null || document.CompanyId != companyId)
        {
            return null;
        }

        return await SummariseWithLookupsAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TradeDocumentSummary>> ListAsync(
        TradeDocumentFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(filter.Take);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var documents = await _unitOfWork.Repository<TradeDocument>()
            .ListAsync(new RecentDocuments(companyId, filter.Kinds ?? [], filter.Take), cancellationToken)
            .ConfigureAwait(false);

        if (documents.Count == 0)
        {
            return [];
        }

        var itemNames = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id, item => item.Name);

        var godownNames = (await _unitOfWork.Repository<Godown>()
                .ListAsync(new GodownsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(godown => godown.Id, godown => godown.Name);

        var partyNames = await PartyNamesAsync(
                documents.Select(document => document.PartyLedgerId).Distinct().ToList(),
                cancellationToken)
            .ConfigureAwait(false);

        return documents
            .Select(document => SummariseCore(
                document,
                itemNames,
                godownNames,
                partyNames.GetValueOrDefault(document.PartyLedgerId, "?")))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<TradeDocumentSummary> ConvertAsync(
        ConvertTradeDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var source = await RequireDocumentWithLinesAsync(
                request.SourceDocumentId,
                companyId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!source.CanEdit)
        {
            throw new InvalidOperationException(
                $"A {source.Kind} in status {source.Status} cannot be converted.");
        }

        var targets = AllowedTargets(source.Kind);

        if (!targets.Contains(request.TargetKind))
        {
            throw new InvalidOperationException(
                $"A {source.Kind} converts into {Describe(targets)}; a {request.TargetKind} is not "
                + "one of them.");
        }

        var itemsById = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id);

        var godownsById = (await _unitOfWork.Repository<Godown>()
                .ListAsync(new GodownsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(godown => godown.Id);

        var carried = CarryForward(
            source.Lines.OrderBy(line => line.LineNumber).ToList(),
            request.TargetKind,
            request.DefaultGodownId,
            itemsById,
            godownsById,
            out var carryErrors);

        if (carryErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, carryErrors));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var successor = new TradeDocument(
            companyId,
            request.TargetKind,
            request.DocumentDate,
            source.PartyLedgerId,
            request.Narration ?? source.Narration);

        successor.SetPartyReference(source.PartyReferenceNumber, source.PartyReferenceDate);
        successor.SetPartyGstin(source.PartyGstin);
        successor.SetReverseCharge(source.IsReverseCharge);
        successor.SetConvertedFrom(
            source.Id,
            source.Number ?? throw new InvalidOperationException(
                $"The {source.Kind} being converted carries no number, which should be impossible "
                + "outside corrupted books. Nothing was converted."));

        foreach (var input in carried)
        {
            successor.AddLine(
                input.ItemId,
                input.GodownId,
                input.Quantity,
                input.Rate,
                input.DiscountPercent,
                input.IsSerialTracked,
                input.BatchNumber,
                input.SerialNumbers);
        }

        if (!successor.StockMoving)
        {
            var allocated = await AllocateNumberAsync(
                    companyId,
                    request.TargetKind,
                    request.DocumentDate,
                    cancellationToken)
                .ConfigureAwait(false);

            successor.AttachNumber(allocated.Number);
        }

        await _unitOfWork.Repository<TradeDocument>()
            .AddAsync(successor, cancellationToken)
            .ConfigureAwait(false);

        // An order stays open until what it ordered has actually left the building: its successors'
        // *postings* close it, not their raising (checkpoint C3.4). Closing at conversion time would
        // drop a partly delivered order off the outstanding report the moment its invoice was
        // drafted, and the trimmed draft — the normal partial-delivery flow — would strand the
        // balance forever. Quotations, challans and goods receipts have no pending quantity to
        // protect, so they still close as soon as they are carried forward.
        var sourceIsOrder = source.Kind is TradeDocumentKind.SalesOrder or TradeDocumentKind.PurchaseOrder;

        if (!sourceIsOrder)
        {
            await CloseSourceAfterConversionAsync(successor, source, cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await SummariseWithLookupsAsync(successor, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<InvoiceValidationResult> ValidateInvoiceAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var document = await RequireDocumentWithLinesAsync(documentId, companyId, cancellationToken)
            .ConfigureAwait(false);

        var (errors, warnings, _) =
            await ValidateInvoiceCoreAsync(companyId, document, cancellationToken).ConfigureAwait(false);

        return new InvoiceValidationResult(errors, warnings);
    }

    /// <inheritdoc />
    public async Task<PostedInvoiceResult> PostInvoiceAsync(
        Guid documentId,
        bool confirmNegativeStockWarnings = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var document = await RequireDocumentWithLinesAsync(documentId, companyId, cancellationToken)
            .ConfigureAwait(false);

        var (errors, warnings, plan) =
            await ValidateInvoiceCoreAsync(companyId, document, cancellationToken).ConfigureAwait(false);

        if (errors.Count > 0 || plan is null)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        if (warnings.Count > 0 && !confirmNegativeStockWarnings)
        {
            throw new InvalidOperationException(
                "This invoice would take stock below zero somewhere. Confirm to post anyway:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, warnings));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // The mirror carries the movement through every existing foreign-key path — ledger rows,
        // serial receipts and sales, valuation replay — while posting nothing itself.
        var mirror = BuildMirrorVoucher(document, plan);

        // Phase 5: resolve the tax snapshot inside the same transaction — rates effective on the
        // document date, place of supply, per-line figures — and stamp it onto the aggregate so
        // returns read what was posted, never recompute it (phase-5-gst-returns.md §2).
        var tax = await _taxComputationService.ComputeAndApplyAsync(document, cancellationToken)
            .ConfigureAwait(false);
        var gstLedgers = await RequireGstLedgersAsync(companyId, plan.IsSale, tax, cancellationToken)
            .ConfigureAwait(false);

        // One number, one posting: the journal engine allocates the invoice's number from the Tax
        // Invoice / Purchase Invoice series as it posts.
        var posted = await _voucherPostingService.PostAsync(
                new PostVoucherRequest(
                    plan.VoucherType.Id,
                    document.DocumentDate,
                    BuildAccountingLines(document, plan, tax, gstLedgers),
                    Narration: ComposeNarration(document, plan),
                    ReferenceNumber: document.PartyReferenceNumber,
                    ReferenceDate: document.PartyReferenceDate,
                    ManualNumber: null),
                cancellationToken)
            .ConfigureAwait(false);

        mirror.AttachPostedVoucher(posted.VoucherId, posted.Number);
        document.AttachPostedVoucher(posted.VoucherId, posted.Number);
        document.AttachNumber(posted.Number);

        await _unitOfWork.Repository<StockVoucher>()
            .AddAsync(mirror, cancellationToken)
            .ConfigureAwait(false);

        // The mirror's lines were appended in movement order, so pairing by position is exact.
        var nextSequence = await NextGlobalSequenceAsync(companyId, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < plan.Movements.Count; index++)
        {
            var movement = plan.Movements[index];
            var mirrorLine = mirror.Lines[index];

            await _unitOfWork.Repository<StockLedgerEntry>().AddAsync(
                new StockLedgerEntry(
                    companyId,
                    nextSequence++,
                    document.DocumentDate,
                    plan.MovementKind,
                    movement.Item.Id,
                    movement.GodownId,
                    movement.Quantity,
                    movement.UnitCostMinor,
                    mirror.Id,
                    mirrorLine.Id,
                    movement.BatchNumber),
                cancellationToken).ConfigureAwait(false);

            await ApplySerialLifecycleAsync(
                    companyId,
                    document.DocumentDate,
                    movement,
                    mirrorLine,
                    plan.IsSale,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // The walk reads successors through SQL filters (cancelled ones excluded), so every
        // in-memory status change must be flushed first — still inside this transaction, so a
        // failure beyond this point rolls the flush back with everything else.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await CloseAncestorChainAfterPostingAsync(document, cancellationToken).ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new PostedInvoiceResult(
            document.Id,
            document.Number!,
            posted.Number,
            document.DocumentDate,
            document.Lines.Sum(line => line.NetValue),
            plan.CostMinorTotal,
            tax.TotalTaxMinor,
            tax.RoundOffMinor,
            tax.Warnings);
    }

    /// <inheritdoc />
    public async Task<TradeDocumentSummary> CancelPostedInvoiceAsync(
        Guid documentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var document = await RequireDocumentWithLinesAsync(documentId, companyId, cancellationToken)
            .ConfigureAwait(false);

        if (document.Status != TradeDocumentStatus.Issued || !document.PostedVoucherId.HasValue)
        {
            throw new InvalidOperationException(
                $"Only an issued invoice can be reversed; this one is {document.Status}.");
        }

        var mirror = await _unitOfWork.Repository<StockVoucher>()
            .SingleOrDefaultAsync(
                new MirrorOfVoucher(companyId, document.PostedVoucherId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (mirror is null)
        {
            throw new InvalidOperationException(
                "This invoice's stock movements are missing from the ledger, which should be "
                + "impossible outside corrupted books. Nothing was changed.");
        }

        var originalEntries = await _unitOfWork.Repository<StockLedgerEntry>()
            .ListAsync(new EntriesOfVoucher(mirror.Id), cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // The cancelling revision holds the number against reuse (§8.6); the reversal entries below
        // put the goods back exactly as they left, and the sold serials come home with them.
        await _voucherPostingService.CancelAsync(document.PostedVoucherId.Value, reason, cancellationToken)
            .ConfigureAwait(false);

        var nextSequence = await NextGlobalSequenceAsync(companyId, cancellationToken).ConfigureAwait(false);

        foreach (var entry in originalEntries)
        {
            await _unitOfWork.Repository<StockLedgerEntry>().AddAsync(
                new StockLedgerEntry(
                    companyId,
                    nextSequence++,
                    document.DocumentDate,
                    ReverseKind(entry.Kind),
                    entry.ItemId,
                    entry.GodownId,
                    entry.QuantityIn + entry.QuantityOut,
                    entry.UnitCostMinor,
                    entry.StockVoucherId,
                    entry.StockVoucherLineId,
                    entry.BatchNumber),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var serial in await _unitOfWork.Repository<SerialNumber>()
                     .ListAsync(new SerialsSoldOn(mirror.Lines.Select(line => line.Id).ToList()),
                         cancellationToken)
                     .ConfigureAwait(false))
        {
            serial.ReturnToStock(serial.CurrentGodownId);
            _unitOfWork.Repository<SerialNumber>().Update(serial);
        }

        document.MarkIssuedCancelled();

        // Flushed before the walk for the same reason as in PostInvoiceAsync: the reopen chain
        // excludes cancelled successors through SQL, and this invoice's cancellation is still only
        // an in-memory fact until it reaches the database (inside the same transaction).
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ReopenAncestorChainAfterCancellationAsync(document, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await SummariseWithLookupsAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderOutstandingRow>> SalesOrderOutstandingAsync(
        CancellationToken cancellationToken = default) =>
        await OutstandingForAsync(TradeDocumentKind.SalesOrder, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderOutstandingRow>> PurchaseOrderOutstandingAsync(
        CancellationToken cancellationToken = default) =>
        await OutstandingForAsync(TradeDocumentKind.PurchaseOrder, cancellationToken).ConfigureAwait(false);

    // -----------------------------------------------------------------------------------------
    // Header and line validation (create / update / convert)
    // -----------------------------------------------------------------------------------------

    private sealed record ResolvedLines(
        List<string> Errors,
        Dictionary<Guid, Item> ItemsById,
        Dictionary<Guid, Godown> GodownsById);

    private async Task<ResolvedLines> ValidateHeaderAndLinesAsync(
        Guid companyId,
        SaveTradeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), request.Kind, "Unknown trade document kind.");
        }

        var errors = new List<string>();

        if (request.Lines.Count == 0)
        {
            errors.Add("A trade document needs at least one line.");
        }

        var party = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(request.PartyLedgerId, cancellationToken)
            .ConfigureAwait(false);

        if (party is null || party.CompanyId != companyId)
        {
            errors.Add("The party ledger does not exist in this company's books.");
        }
        else if (!party.IsActive)
        {
            errors.Add($"'{party.Name}' is inactive and cannot be used on a document.");
        }
        else if (TradeDocument.IsStockMoving(request.Kind) && !party.AllowsManualPosting)
        {
            errors.Add($"'{party.Name}' does not accept postings; pick another ledger for the party.");
        }

        var itemsById = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id);

        var godownsById = (await _unitOfWork.Repository<Godown>()
                .ListAsync(new GodownsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(godown => godown.Id);

        var needsGodown = TradeDocument.RequiresGodownPerLine(request.Kind);

        var ordinal = 0;

        foreach (var line in request.Lines)
        {
            ordinal++;

            if (!itemsById.TryGetValue(line.ItemId, out var item))
            {
                errors.Add($"Item '{line.ItemId}' on line {ordinal} does not exist in this company's books.");
                continue;
            }

            if (!item.IsActive)
            {
                errors.Add($"'{item.Name}' on line {ordinal} is inactive and cannot be traded.");
            }

            if (needsGodown)
            {
                if (!line.GodownId.HasValue)
                {
                    errors.Add($"Line {ordinal} ('{item.Name}') must name the godown it moves through.");
                }
                else if (!godownsById.TryGetValue(line.GodownId.Value, out var godown))
                {
                    errors.Add($"Godown '{line.GodownId.Value}' on line {ordinal} does not exist in this company's books.");
                }
                else if (!godown.IsActive)
                {
                    errors.Add($"'{godown.Name}' on line {ordinal} is inactive and cannot move stock.");
                }
            }
            else if (line.GodownId.HasValue)
            {
                errors.Add($"A {request.Kind} moves no stock, so line {ordinal} cannot name a godown.");
            }

            if (item.IsSerialTracked)
            {
                if (line.Quantity != decimal.Truncate(line.Quantity))
                {
                    errors.Add(
                        $"'{item.Name}' on line {ordinal} is serial-tracked: whole units only.");
                }

                CheckStagedSerialCounts(line, item.Name, ordinal, errors);
            }

            if ((request.Kind == TradeDocumentKind.GoodsReceipt
                    || request.Kind == TradeDocumentKind.PurchaseInvoice)
                && item.IsBatchTracked
                && string.IsNullOrWhiteSpace(line.BatchNumber))
            {
                errors.Add($"A receipt of '{item.Name}' on line {ordinal} must carry its batch number.");
            }
        }

        // C4.5's backstop, now carrying Phase 9's per-person gate as well (§4a D4). An override
        // needs the company-wide switch *and* the permission, and requiring both is what makes the
        // upgrade behaviour-identical: an install with the switch off still refuses every override,
        // and an install that has not created its first account yet grants the permission to
        // everybody — which is exactly how Phase 8 behaved. The UI hides the path; this refuses any
        // other route to it, which is the half C9.2 is about.
        if (TradeDocument.IsSalesSide(request.Kind))
        {
            var settingsRows = await _unitOfWork.Repository<SalesSettings>()
                .ListAsync(new SalesSettingsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false);
            var settings = settingsRows.Count > 0 ? settingsRows[0] : null;

            var lockedOff = settings is { AllowPriceOverride: false };
            var mayOverride = await _permissions
                .HasPermissionAsync(PermissionKind.OverrideSalesPrice, cancellationToken)
                .ConfigureAwait(false);

            if (lockedOff || !mayOverride)
            {
                // The switch is reported before the permission, because it is the one the operator
                // can see on a settings screen and ask about.
                var because = lockedOff
                    ? "price overrides are locked off in Sales settings"
                    : "your role does not allow a price override";

                ordinal = 0;

                foreach (var line in request.Lines)
                {
                    ordinal++;

                    var autoPriced = line.PriceSource != SalePriceSource.Manual;
                    var movedOffApplied = line.AppliedRateMinor.HasValue
                        && line.AppliedRateMinor.Value
                        != MoneyScale.ToMinor(MoneyScale.Round(line.Rate));

                    if (line.IsPriceOverridden || (autoPriced && movedOffApplied))
                    {
                        errors.Add(
                            $"Line {ordinal} carries a price different from its auto-applied price, "
                            + $"but {because}.");
                    }
                }
            }
        }

        return new ResolvedLines(errors, itemsById, godownsById);
    }

    private sealed class SalesSettingsOfCompany : Specification<SalesSettings>
    {
        public SalesSettingsOfCompany(Guid companyId)
        {
            Where(settings => settings.CompanyId == companyId);
        }
    }

    private static void CheckStagedSerialCounts(
        TradeDocumentLineInput line,
        string itemName,
        int ordinal,
        List<string> errors)
    {
        if (line.SerialNumbers is not { Count: > 0 })
        {
            return;
        }

        var expected = (int)line.Quantity;
        var distinct = line.SerialNumbers
            .Select(serial => NameNormaliser.Normalise(serial))
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (line.SerialNumbers.Count != expected)
        {
            errors.Add(
                $"'{itemName}' on line {ordinal} is serial-tracked: list exactly {expected} serial "
                + $"number(s) for {line.Quantity.ToString("0.####", CultureInfo.InvariantCulture)} unit(s).");
        }
        else if (distinct != expected)
        {
            errors.Add($"'{itemName}' on line {ordinal} lists the same serial more than once.");
        }
    }

    private static TradeDocument BuildDocument(
        Guid companyId,
        SaveTradeDocumentRequest request,
        Dictionary<Guid, Item> itemsById)
    {
        var document = new TradeDocument(
            companyId,
            request.Kind,
            request.DocumentDate,
            request.PartyLedgerId,
            request.Narration);

        document.SetPartyReference(request.PartyReferenceNumber, request.PartyReferenceDate);
        document.SetPartyGstin(request.PartyGstin);
        document.SetReverseCharge(request.IsReverseCharge);

        foreach (var line in request.Lines)
        {
            var item = itemsById[line.ItemId];

            document.AddLine(
                item.Id,
                line.GodownId,
                line.Quantity,
                line.Rate,
                line.DiscountPercent,
                item.IsSerialTracked,
                line.BatchNumber,
                line.SerialNumbers,
                line.AppliedRateMinor,
                line.IsPriceOverridden,
                line.PriceSource);
        }

        return document;
    }

    private static IEnumerable<TradeLineInput> ToLineInputs(
        IEnumerable<TradeDocumentLineInput> lines,
        Dictionary<Guid, Item> itemsById)
    {
        foreach (var line in lines)
        {
            var item = itemsById[line.ItemId];

            yield return new TradeLineInput(
                item.Id,
                line.GodownId,
                line.Quantity,
                line.Rate,
                line.DiscountPercent,
                item.IsSerialTracked,
                line.BatchNumber,
                line.SerialNumbers,
                line.AppliedRateMinor,
                line.IsPriceOverridden,
                line.PriceSource);
        }
    }

    /// <summary>
    /// Carries a source's lines forward into the target kind without re-keying (checkpoint C3.1):
    /// items, quantities, rates and discounts travel verbatim; godowns travel where the target
    /// moves stock — falling back to the caller's default for order-to-invoice jumps — and
    /// batches/serials only where they mean anything.
    /// </summary>
    private static List<TradeLineInput> CarryForward(
        IReadOnlyList<TradeDocumentLine> lines,
        TradeDocumentKind targetKind,
        Guid? defaultGodownId,
        Dictionary<Guid, Item> itemsById,
        Dictionary<Guid, Godown> godownsById,
        out List<string> errors)
    {
        errors = [];
        var inputs = new List<TradeLineInput>(lines.Count);
        var needsGodown = TradeDocument.RequiresGodownPerLine(targetKind);

        foreach (var line in lines)
        {
            if (!itemsById.TryGetValue(line.ItemId, out var item))
            {
                errors.Add($"Item '{line.ItemId}' on line {line.LineNumber} no longer exists.");
                continue;
            }

            Guid? godownId = null;

            if (needsGodown)
            {
                godownId = line.GodownId ?? defaultGodownId;

                if (!godownId.HasValue)
                {
                    errors.Add(
                        $"Line {line.LineNumber} ('{item.Name}'): a {targetKind} moves stock, so a "
                        + "godown is required and the source named none. Pick a default godown.");
                    continue;
                }

                if (!godownsById.TryGetValue(godownId.Value, out var godown) || !godown.IsActive)
                {
                    errors.Add(
                        $"Line {line.LineNumber} ('{item.Name}'): the godown does not exist in this "
                        + "company's books or is inactive.");
                    continue;
                }
            }

            var carriesBatch = needsGodown && item.IsBatchTracked;
            var carriesSerials = TradeDocument.IsStockMoving(targetKind)
                && item.IsSerialTracked
                && line.ParsedSerials.Count > 0;

            if (item.IsSerialTracked && line.Quantity != decimal.Truncate(line.Quantity))
            {
                errors.Add(
                    $"'{item.Name}' on line {line.LineNumber} is now serial-tracked; its quantity "
                    + "must be whole before it can be carried forward.");
                continue;
            }

            inputs.Add(new TradeLineInput(
                item.Id,
                godownId,
                line.Quantity,
                line.Rate,
                line.DiscountPercent,
                item.IsSerialTracked,
                carriesBatch ? line.BatchNumber : null,
                carriesSerials ? line.ParsedSerials : null,

                // Conversion never reprices, so the pricing evidence travels with the rate.
                line.AppliedRateMinor,
                line.IsPriceOverridden,
                line.PriceSource));
        }

        return inputs;
    }

    // -----------------------------------------------------------------------------------------
    // Fulfilment tracking (checkpoint C3.4)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Closes a conversion source once its direct non-cancelled successors — including the one
    /// being raised right now, which is not yet saved — have carried forward everything it ordered.
    /// Called at conversion time for quotations, challans and goods receipts only: an order is
    /// never closed by a successor being raised, because the operator routinely trims the draft it
    /// became, and a prematurely closed order would drop off the outstanding report with its
    /// balance stranded (checkpoint C3.4). Orders close through
    /// <see cref="CloseAncestorChainAfterPostingAsync"/> when their goods actually post out.
    /// </summary>
    private async Task CloseSourceAfterConversionAsync(
        TradeDocument successor,
        TradeDocument source,
        CancellationToken cancellationToken)
    {
        if (!source.CanEdit)
        {
            return;
        }

        var siblings = await _unitOfWork.Repository<TradeDocument>()
            .ListAsync(new ConvertedFromDocuments([source.Id]), cancellationToken)
            .ConfigureAwait(false);

        var consumedByItem = new Dictionary<Guid, decimal>();

        foreach (var line in successor.Lines)
        {
            consumedByItem[line.ItemId] = consumedByItem.GetValueOrDefault(line.ItemId) + line.Quantity;
        }

        if (await ConsumedBeyondAsync(
                    source,
                    siblings,
                    consumedByItem,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            CloseAsFullyConsumed(source);
            _unitOfWork.Repository<TradeDocument>().Update(source);
        }
    }

    /// <summary>
    /// Walks the <c>ConvertedFrom</c> chain upwards from a posted invoice, closing each ancestor
    /// whose direct non-cancelled successors have now carried away everything it ordered — orders
    /// become fulfilled, quotations/challans/goods receipts converted. The walk stops at the first
    /// uncovered ancestor, since nothing above that one can be covered either.
    /// </summary>
    /// <remarks>
    /// Consumption is measured over *direct* successors only, each generation summing its own
    /// children: counting transitively would bill the same goods twice against the order (once as
    /// the challan, once again as the invoice the challan became). The posted invoice itself is
    /// already persisted at this point, so it appears among those children without being added by
    /// hand — adding it again was exactly the double-count this walk replaced.
    /// <para>
    /// An ancestor that is already closed (a goods receipt the invoice came from converted the
    /// moment its successor was raised) is skipped, not a wall: the order above it is still owed an
    /// evaluation of its own coverage.
    /// </para>
    /// </remarks>
    private async Task CloseAncestorChainAfterPostingAsync(
        TradeDocument postedInvoice,
        CancellationToken cancellationToken)
    {
        var ancestorId = postedInvoice.ConvertedFromDocumentId;

        while (ancestorId.HasValue)
        {
            var ancestor = await _unitOfWork.Repository<TradeDocument>()
                .SingleOrDefaultAsync(new DocumentWithLines(ancestorId.Value), cancellationToken)
                .ConfigureAwait(false);

            if (ancestor is null)
            {
                return;
            }

            if (ancestor.CanEdit)
            {
                var successors = await _unitOfWork.Repository<TradeDocument>()
                    .ListAsync(new ConvertedFromDocuments([ancestor.Id]), cancellationToken)
                    .ConfigureAwait(false);

                if (successors.Count == 0
                    || !await ConsumedBeyondAsync(ancestor, successors, [], cancellationToken)
                        .ConfigureAwait(false))
                {
                    return;
                }

                CloseAsFullyConsumed(ancestor);
                _unitOfWork.Repository<TradeDocument>().Update(ancestor);
            }

            ancestorId = ancestor.ConvertedFromDocumentId;
        }
    }

    /// <summary>
    /// After a reversal freed quantity back, walks the same chain putting any ancestor that had
    /// already been marked fulfilled — but no longer measures fully consumed — back in play
    /// (status open), so its balance returns to the outstanding report.
    /// </summary>
    private async Task ReopenAncestorChainAfterCancellationAsync(
        TradeDocument cancelledInvoice,
        CancellationToken cancellationToken)
    {
        var ancestorId = cancelledInvoice.ConvertedFromDocumentId;

        while (ancestorId.HasValue)
        {
            var ancestor = await _unitOfWork.Repository<TradeDocument>()
                .SingleOrDefaultAsync(new DocumentWithLines(ancestorId.Value), cancellationToken)
                .ConfigureAwait(false);

            if (ancestor is null
                || ancestor.Status is not (TradeDocumentStatus.Open or TradeDocumentStatus.Fulfilled))
            {
                return;
            }

            var successors = await _unitOfWork.Repository<TradeDocument>()
                .ListAsync(new ConvertedFromDocuments([ancestor.Id]), cancellationToken)
                .ConfigureAwait(false);

            var stillConsumed = successors.Count > 0 && await ConsumedBeyondAsync(
                    ancestor,
                    successors,
                    [],
                    cancellationToken)
                .ConfigureAwait(false);

            if (!stillConsumed && ancestor.Status == TradeDocumentStatus.Fulfilled)
            {
                ancestor.Reopen();
                _unitOfWork.Repository<TradeDocument>().Update(ancestor);
            }

            if (!stillConsumed)
            {
                return;
            }

            ancestorId = ancestor.ConvertedFromDocumentId;
        }
    }

    /// <summary>
    /// Whether <paramref name="source"/>'s lines are covered by what its saved
    /// <paramref name="successors"/> carry, on top of whatever has already been summed into
    /// <paramref name="consumedByItem"/> (the unsaved successor's lines at conversion time).
    /// </summary>
    private async Task<bool> ConsumedBeyondAsync(
        TradeDocument source,
        IReadOnlyList<TradeDocument> successors,
        Dictionary<Guid, decimal> consumedByItem,
        CancellationToken cancellationToken)
    {
        if (source.Lines.Count == 0)
        {
            return false;
        }

        if (successors.Count > 0)
        {
            foreach (var line in await _unitOfWork.Repository<TradeDocumentLine>()
                         .ListAsync(new LinesOfDocuments(successors.Select(row => row.Id).ToList()),
                             cancellationToken)
                         .ConfigureAwait(false))
            {
                consumedByItem[line.ItemId] = consumedByItem.GetValueOrDefault(line.ItemId) + line.Quantity;
            }
        }

        return source.Lines.All(line => consumedByItem.GetValueOrDefault(line.ItemId) >= line.Quantity);
    }

    private static void CloseAsFullyConsumed(TradeDocument source)
    {
        if (source.Kind is TradeDocumentKind.SalesOrder or TradeDocumentKind.PurchaseOrder)
        {
            source.MarkFulfilled();
        }
        else
        {
            source.MarkConverted();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Invoice validation and planning
    // -----------------------------------------------------------------------------------------

    private sealed record PlannedMovement(
        Item Item,
        Guid GodownId,
        decimal Quantity,
        long UnitCostMinor,
        string? BatchNumber,
        IReadOnlyList<string> Serials,
        TradeDocumentLine SourceLine)
    {
        public long ValueMinor => StockLedgerEntry.ComputeValueMinor(Quantity, UnitCostMinor);
    }

    private sealed record InvoicePlan(
        bool IsSale,
        StockMovementKind MovementKind,
        Ledger PartyLedger,
        Ledger ControlLedger,
        Ledger IncomeOrExpenseLedger,
        Ledger CogsLedger,
        VoucherType VoucherType,
        IReadOnlyList<PlannedMovement> Movements,
        long CostMinorTotal,
        long ReceiptValueMinor);

    /// <summary>
    /// Everything checkable about an invoice before anything is written: status and shape, the four
    /// ledgers the posting needs, kit expansion on the sale side, costing of every issue against
    /// the ledger as it stands, the negative-stock projection (#20 policy) and serial availability
    /// (C2.3). Returns a null plan whenever the errors list is non-empty.
    /// </summary>
    private async Task<(List<string> Errors, List<string> Warnings, InvoicePlan? Plan)>
        ValidateInvoiceCoreAsync(
            Guid companyId,
            TradeDocument document,
            CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!document.StockMoving)
        {
            errors.Add($"A {document.Kind} never posts to the books; only invoices do.");
            return (errors, warnings, null);
        }

        if (document.Status != TradeDocumentStatus.Draft)
        {
            errors.Add(
                $"Only a draft invoice can post; this one is {document.Status}. A posted invoice is "
                + "reversed, never re-posted.");
            return (errors, warnings, null);
        }

        if (document.Lines.Count == 0)
        {
            errors.Add("An invoice needs at least one line before it can post.");
        }

        var total = document.Lines.Sum(line => line.NetValue);

        if (total <= 0m)
        {
            errors.Add("An invoice must total an amount above zero before it can post.");
        }

        var party = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(document.PartyLedgerId, cancellationToken)
            .ConfigureAwait(false);

        if (party is null || party.CompanyId != companyId)
        {
            errors.Add("The invoice's party ledger does not exist in this company's books.");
        }
        else if (!party.IsActive || !party.AllowsManualPosting)
        {
            errors.Add($"'{party.Name}' cannot take this posting.");
        }

        var control = await RequireNamedLedgerAsync(
                companyId, ControlLedgerName, "stock could not reach the books", errors, cancellationToken)
            .ConfigureAwait(false);

        var incomeOrExpense = await RequireNamedLedgerAsync(
                companyId,
                document.SalesSide ? SalesLedgerName : PurchasesLedgerName,
                "the commercial leg had nowhere to post",
                errors,
                cancellationToken)
            .ConfigureAwait(false);

        var cogs = await RequireNamedLedgerAsync(
                companyId,
                PipelineSeedData.CostOfGoodsSoldLedgerName,
                "the cost of goods sold could not post",
                errors,
                cancellationToken)
            .ConfigureAwait(false);

        var voucherTypeName = PipelineSeedData.VoucherTypeNameFor(document.Kind);

        var voucherType = await _unitOfWork.Repository<VoucherType>()
            .SingleOrDefaultAsync(
                new VoucherTypeByName(companyId, NameNormaliser.Normalise(voucherTypeName)),
                cancellationToken)
            .ConfigureAwait(false);

        if (voucherType is null)
        {
            throw new InvalidOperationException(
                $"The '{voucherTypeName}' voucher type is missing from this company's books. Run "
                + "pipeline provisioning before posting invoices.");
        }

        if (control is null || incomeOrExpense is null || cogs is null || party is null)
        {
            return (errors, warnings, null);
        }

        var itemsById = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id);

        var isSale = document.SalesSide;
        var movements = new List<PlannedMovement>();
        long receiptValueMinor = 0L;

        foreach (var line in document.Lines.OrderBy(line => line.LineNumber))
        {
            if (!itemsById.TryGetValue(line.ItemId, out var item))
            {
                errors.Add($"Item '{line.ItemId}' no longer exists in this company's books.");
                continue;
            }

            if (isSale)
            {
                if (item.IsKit)
                {
                    await ExpandKitIssueAsync(item, line, itemsById, movements, errors, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    movements.Add(new PlannedMovement(
                        item,
                        line.GodownId!.Value,
                        line.Quantity,
                        UnitCostMinor: 0L,
                        line.BatchNumber,
                        line.ParsedSerials,
                        line));
                }
            }
            else
            {
                if (item.IsKit)
                {
                    errors.Add(
                        $"'{item.Name}' is a kit and cannot be received; purchase its components instead.");
                    continue;
                }

                if (item.IsBatchTracked && string.IsNullOrWhiteSpace(line.BatchNumber))
                {
                    errors.Add(
                        $"A receipt of '{item.Name}' on line {line.LineNumber} must carry its batch number.");
                }

                // Goods land at what was paid: net rate per unit, minor units (ADR-0003).
                var netRate = MoneyScale.Round(line.Rate * (1m - line.DiscountPercent / 100m));
                var unitCostMinor = MoneyScale.ToMinor(netRate);

                receiptValueMinor += StockLedgerEntry.ComputeValueMinor(line.Quantity, unitCostMinor);

                movements.Add(new PlannedMovement(
                    item,
                    line.GodownId!.Value,
                    line.Quantity,
                    unitCostMinor,
                    line.BatchNumber,
                    line.ParsedSerials,
                    line));
            }
        }

        // Cost every issue side against the ledger as it stands before the invoice, carrying prior
        // consumption within this posting so a repeated item costs on what genuinely remains.
        // Receipt sides are skipped deliberately: they are already self-costed at what was paid,
        // and running a purchase through issue valuation would warn "exceeded cost layers" on the
        // very first receipt of an item — blocking every fresh purchase behind a confirmation.
        var issuedByItem = new Dictionary<Guid, decimal>();
        long costMinorTotal = 0L;

        if (isSale)
        {
            for (var index = 0; index < movements.Count; index++)
            {
                var movement = movements[index];
                var prior = issuedByItem.TryGetValue(movement.Item.Id, out var seen) ? seen : 0m;

                var valued = await _valuationService.ValueIssueAsync(
                        movement.Item.Id,
                        movement.Quantity,
                        document.DocumentDate,
                        prior,
                        cancellationToken)
                    .ConfigureAwait(false);

                warnings.AddRange(valued.Warnings);
                issuedByItem[movement.Item.Id] = prior + movement.Quantity;

                costMinorTotal += StockLedgerEntry.ComputeValueMinor(movement.Quantity, valued.UnitCostMinor);
                movements[index] = movement with { UnitCostMinor = valued.UnitCostMinor };
            }
        }

        await ProjectNegativeStockAsync(companyId, movements, isSale, warnings, cancellationToken)
            .ConfigureAwait(false);

        await CheckSerialsCoreAsync(companyId, movements, isSale, errors, cancellationToken)
            .ConfigureAwait(false);

        if (isSale && costMinorTotal == 0L && movements.Count > 0)
        {
            warnings.Add(
                "The goods sold carry no stock value, so no cost-of-goods-sold entry will post. "
                + "Post only if that is intended.");
        }

        var plan = new InvoicePlan(
            isSale,
            isSale ? StockMovementKind.Sale : StockMovementKind.Purchase,
            party,
            control,
            incomeOrExpense,
            cogs,
            voucherType,
            movements,
            costMinorTotal,
            receiptValueMinor);

        return (errors, warnings, plan);
    }

    private async Task ExpandKitIssueAsync(
        Item kit,
        TradeDocumentLine line,
        Dictionary<Guid, Item> itemsById,
        List<PlannedMovement> movements,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var components = await _unitOfWork.Repository<ItemKitComponent>()
            .ListAsync(new ComponentsOfKit(kit.Id), cancellationToken)
            .ConfigureAwait(false);

        if (components.Count == 0)
        {
            errors.Add(
                $"'{kit.Name}' is a kit with no components configured; set them on the item master first.");
            return;
        }

        if (kit.IsSerialTracked && line.ParsedSerials.Count > 0)
        {
            errors.Add(
                $"'{kit.Name}' is a kit: stage serial numbers on its components, never on the kit line.");
        }

        foreach (var component in components)
        {
            if (!itemsById.TryGetValue(component.ComponentItemId, out var componentItem))
            {
                errors.Add($"Kit component '{component.ComponentItemId}' of '{kit.Name}' does not exist.");
                continue;
            }

            if (componentItem.IsSerialTracked)
            {
                errors.Add(
                    $"'{componentItem.Name}' is a serial-tracked component of kit '{kit.Name}'. Invoice "
                    + "its components directly with their serials; kit expansion cannot guess which "
                    + "units to take.");
                continue;
            }

            movements.Add(new PlannedMovement(
                componentItem,
                line.GodownId!.Value,
                line.Quantity * component.Quantity,
                UnitCostMinor: 0L,
                BatchNumber: null,
                [],
                line));
        }
    }

    /// <summary>Applies issue-side movements over current on-hand and warns wherever stock dips below zero.</summary>
    private async Task ProjectNegativeStockAsync(
        Guid companyId,
        IReadOnlyList<PlannedMovement> movements,
        bool isSale,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!isSale || movements.Count == 0)
        {
            return;
        }

        var itemIds = movements.Select(movement => movement.Item.Id).Distinct().ToList();

        var entries = await _unitOfWork.Repository<StockLedgerEntry>()
            .ListAsync(new EntriesOfItems(companyId, itemIds), cancellationToken)
            .ConfigureAwait(false);

        var onHandByItem = new Dictionary<Guid, Dictionary<Guid, decimal>>();

        foreach (var entry in entries)
        {
            if (!onHandByItem.TryGetValue(entry.ItemId, out var byGodown))
            {
                byGodown = [];
                onHandByItem[entry.ItemId] = byGodown;
            }

            byGodown[entry.GodownId] = byGodown.GetValueOrDefault(entry.GodownId) + entry.SignedQuantity;
        }

        foreach (var movement in movements)
        {
            if (!onHandByItem.TryGetValue(movement.Item.Id, out var byGodown))
            {
                byGodown = [];
                onHandByItem[movement.Item.Id] = byGodown;
            }

            var resulting = byGodown.GetValueOrDefault(movement.GodownId) - movement.Quantity;
            byGodown[movement.GodownId] = resulting;

            if (resulting >= 0m)
            {
                continue;
            }

            warnings.Add(
                $"'{movement.Item.Name}' would fall to "
                + $"{resulting.ToString("0.####", CultureInfo.InvariantCulture)} in the godown the "
                + "invoice names. Post only if this shortfall is intended.");
        }
    }

    /// <summary>
    /// Availability on the sale side and duplicate/existence checks on the receipt side, over the
    /// expanded movements (checkpoint C2.3). Never merely a warning: an invoice cannot promise a
    /// unit that is not in the godown it leaves from.
    /// </summary>
    private async Task CheckSerialsCoreAsync(
        Guid companyId,
        IReadOnlyList<PlannedMovement> movements,
        bool isSale,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var staged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var movement in movements)
        {
            if (!movement.Item.IsSerialTracked)
            {
                continue;
            }

            var expected = (int)movement.Quantity;
            var serials = movement.Serials;

            if (serials.Count != expected)
            {
                errors.Add(
                    $"'{movement.Item.Name}' is serial-tracked: list exactly {expected} serial "
                    + $"number(s) for {movement.Quantity.ToString("0.####", CultureInfo.InvariantCulture)} unit(s).");
                continue;
            }

            foreach (var serialValue in serials)
            {
                var key = NameNormaliser.Normalise(serialValue);

                if (!staged.Add(key))
                {
                    errors.Add(
                        $"Serial '{serialValue}' appears more than once among this invoice's lines.");
                    continue;
                }

                var existing = await FindSerialAsync(companyId, movement.Item.Id, key, cancellationToken)
                    .ConfigureAwait(false);

                if (isSale)
                {
                    if (existing is null)
                    {
                        errors.Add(
                            $"Serial '{serialValue}' of '{movement.Item.Name}' is not in the books; "
                            + "it cannot be sold because it was never received (checkpoint C2.3).");
                        continue;
                    }

                    if (existing.Status != SerialNumberStatus.InStock)
                    {
                        errors.Add(
                            $"Serial '{serialValue}' of '{movement.Item.Name}' was already sold on "
                            + $"{existing.SoldOn:yyyy-MM-dd}; the same serial cannot leave stock twice.");
                        continue;
                    }

                    if (existing.CurrentGodownId != movement.GodownId)
                    {
                        errors.Add(
                            $"Serial '{serialValue}' of '{movement.Item.Name}' sits in another godown "
                            + "than the invoice line names. Correct the line or move the unit first.");
                    }
                }
                else if (existing is not null)
                {
                    errors.Add(
                        $"Serial '{serialValue}' of '{movement.Item.Name}' already exists in the "
                        + "books — a serial can be received only once.");
                }
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Posting mechanics
    // -----------------------------------------------------------------------------------------

    private static StockVoucher BuildMirrorVoucher(TradeDocument document, InvoicePlan plan)
    {
        var purpose = plan.IsSale ? StockVoucherPurpose.Issue : StockVoucherPurpose.Receipt;

        var mirror = new StockVoucher(
            document.CompanyId,
            purpose,
            document.DocumentDate,
            $"Mirror of {document.Kind} {(document.Number is null ? "(unposted)" : document.Number)}");

        // Informational only: the accounting effect posts through the journal engine below, never
        // through the stock-voucher path. The party is recorded because commercially that is who
        // the goods moved against.
        mirror.SetCounterLedger(plan.PartyLedger.Id);

        foreach (var movement in plan.Movements)
        {
            var direction = plan.IsSale
                ? StockMovementDirection.Decrease
                : StockMovementDirection.Increase;

            var mirrorLine = mirror.AddLine(
                direction,
                movement.Item.Id,
                movement.GodownId,
                movement.Quantity,
                movement.Item.IsSerialTracked,
                movement.Item.IsBatchTracked,
                unitCost: MoneyScale.FromMinor(movement.UnitCostMinor),
                batchNumber: movement.BatchNumber);

            if (plan.IsSale)
            {
                // Issue sides are valued by the service; the mirror line carries the same figure so
                // anything summing voucher values agrees with the ledger rows.
                mirrorLine.ApplyValuedCost(movement.UnitCostMinor);
            }
        }

        return mirror;
    }

    /// <summary>
    /// The posting legs. A sale balances twice over — party/sales at what was billed, COGS/control
    /// at what the goods were worth — and either pair drops out cleanly if it would post nothing. A
    /// purchase books the goods at cost and sends any difference between invoice total and stock
    /// value to Purchases, so the control account still ties to the stock ledger exactly. Phase 5
    /// adds the tax heads (phase-5-gst-returns.md §2): output tax on a sale credits the Output
    /// ledgers; input credit on a purchase debits the Input ledgers; a reverse-charge supply
    /// self-assesses through the same pair without touching the party; and the per-invoice rupee
    /// round-off lands on its designated ledger — never silently dropped.
    /// </summary>
    /// <remarks>
    /// <see cref="PostVoucherLine"/> carries rupee decimals — it is the journal engine's entry
    /// shape, and <c>JournalLine</c> does its own scaling to minor units on the way down. Every
    /// figure this builder holds in minor units is converted exactly once, here, on its way out;
    /// handing a minor-unit long to a decimal parameter silently bills ten-thousand-fold.
    /// </remarks>
    private static List<PostVoucherLine> BuildAccountingLines(
        TradeDocument document,
        InvoicePlan plan,
        InvoiceTaxComputationResult tax,
        IReadOnlyDictionary<string, Ledger> gstLedgers)
    {
        var taxableMinor = MoneyScale.ToMinor(document.Lines.Sum(line => line.NetValue));
        var lines = new List<PostVoucherLine>(8);

        // Reverse charge bills the party net only: the self-assessed tax moves Input↔Output.
        var billableMinor = tax.IsReverseCharge
            ? taxableMinor
            : tax.RoundedInvoiceValueMinor;

        var billable = MoneyScale.FromMinor(billableMinor);

        if (plan.IsSale)
        {
            lines.Add(new PostVoucherLine(plan.PartyLedger.Id, billable, 0m, Narration: "Customer"));
            lines.Add(new PostVoucherLine(
                plan.IncomeOrExpenseLedger.Id,
                0m,
                MoneyScale.FromMinor(taxableMinor),
                Narration: "Sales"));

            if (plan.CostMinorTotal > 0L)
            {
                var cost = MoneyScale.FromMinor(plan.CostMinorTotal);
                lines.Add(new PostVoucherLine(plan.CogsLedger.Id, cost, 0m, Narration: "Cost of goods sold"));
                lines.Add(new PostVoucherLine(plan.ControlLedger.Id, 0m, cost, Narration: "Stock issued"));
            }
        }
        else
        {
            if (plan.ReceiptValueMinor > 0L)
            {
                lines.Add(new PostVoucherLine(
                    plan.ControlLedger.Id,
                    MoneyScale.FromMinor(plan.ReceiptValueMinor),
                    0m,
                    Narration: "Stock received"));
            }

            var difference = taxableMinor - plan.ReceiptValueMinor;

            if (difference > 0L)
            {
                lines.Add(new PostVoucherLine(
                    plan.IncomeOrExpenseLedger.Id,
                    MoneyScale.FromMinor(difference),
                    0m,
                    Narration: "Charges above stock value"));
            }
            else if (difference < 0L)
            {
                lines.Add(new PostVoucherLine(
                    plan.IncomeOrExpenseLedger.Id,
                    0m,
                    MoneyScale.FromMinor(-difference),
                    Narration: "Receipt below cost"));
            }

            lines.Add(new PostVoucherLine(plan.PartyLedger.Id, 0m, billable, Narration: "Supplier"));
        }

        AddTaxLegs(lines, plan, tax, gstLedgers);
        AddRoundOffLeg(lines, plan, tax, gstLedgers);

        return lines;
    }

    /// <summary>
    /// The four tax heads as they post: charged-to-party taxes credit Output (sale) or debit Input
    /// (purchase); reverse-charge self-assessment does both, booking the credit alongside the
    /// matching input claim (the common treatment, flagged for GSTR-3B reporting via the snapshot).
    /// </summary>
    private static void AddTaxLegs(
        List<PostVoucherLine> lines,
        InvoicePlan plan,
        InvoiceTaxComputationResult tax,
        IReadOnlyDictionary<string, Ledger> gstLedgers)
    {
        foreach (var (ledgerName, amountMinor) in new[]
                 {
                     (GstSeedData.OutputLedgerNames[0], tax.TotalCgstMinor),
                     (GstSeedData.OutputLedgerNames[1], tax.TotalSgstMinor),
                     (GstSeedData.OutputLedgerNames[2], tax.TotalIgstMinor),
                     (GstSeedData.OutputLedgerNames[3], tax.TotalCessMinor),
                 })
        {
            if (amountMinor == 0L)
            {
                continue;
            }

            var amount = MoneyScale.FromMinor(amountMinor);

            // Self-assessed reverse charge: credit the output liability…
            if (tax.IsReverseCharge)
            {
                lines.Add(new PostVoucherLine(gstLedgers[ledgerName].Id, 0m, amount, Narration: "Self-assessed reverse-charge tax"));
            }
            else if (plan.IsSale)
            {
                // …and an ordinary sale simply charges it out.
                lines.Add(new PostVoucherLine(gstLedgers[ledgerName].Id, 0m, amount, Narration: ledgerName));
            }

            // The matching input leg: claimed directly on a non-reverse-charge purchase, and set
            // against the self-assessment when the tax arrived under reverse charge.
            var inputName = InputLedgerNameFor(ledgerName);

            if (!plan.IsSale || tax.IsReverseCharge)
            {
                lines.Add(new PostVoucherLine(gstLedgers[inputName].Id, amount, 0m, Narration: inputName));
            }
        }
    }

    private static string InputLedgerNameFor(string outputLedgerName) => outputLedgerName switch
    {
        "Output CGST" => GstSeedData.InputLedgerNames[0],
        "Output SGST" => GstSeedData.InputLedgerNames[1],
        "Output IGST" => GstSeedData.InputLedgerNames[2],
        "Output Cess" => GstSeedData.InputLedgerNames[3],
        _ => throw new ArgumentOutOfRangeException(nameof(outputLedgerName), outputLedgerName, "Unknown output tax ledger."),
    };

    /// <summary>The rupee round-off difference posts visibly or not at all (phase-5-gst-returns.md §2).</summary>
    private static void AddRoundOffLeg(
        List<PostVoucherLine> lines,
        InvoicePlan plan,
        InvoiceTaxComputationResult tax,
        IReadOnlyDictionary<string, Ledger> gstLedgers)
    {
        if (tax.RoundOffMinor == 0L || (plan.IsSale && tax.IsReverseCharge))
        {
            return;
        }

        var roundOff = MoneyScale.FromMinor(Math.Abs(tax.RoundOffMinor));
        var ledger = gstLedgers[tax.RoundOffLedgerName];

        // On a sale the difference pads (credits) or trims (debits) the billed side; on a purchase
        // it mirrors onto the expense side so the party still owes exactly what was billed.
        var isCredit = plan.IsSale ? tax.RoundOffMinor > 0L : tax.RoundOffMinor < 0L;

        lines.Add(isCredit
            ? new PostVoucherLine(ledger.Id, 0m, roundOff, Narration: "Round off")
            : new PostVoucherLine(ledger.Id, roundOff, 0m, Narration: "Round off"));
    }

    /// <summary>
    /// Resolves only the tax ledgers this invoice actually uses, so a company raising plain
    /// unconfigured invoices needs nothing beyond the chart of accounts it already has. A missing
    /// ledger that the computed figures demand stops the posting loudly — inside the transaction,
    /// which rolls back with everything else.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, Ledger>> RequireGstLedgersAsync(
        Guid companyId,
        bool isSale,
        InvoiceTaxComputationResult tax,
        CancellationToken cancellationToken)
    {
        var wanted = new List<string>();

        foreach (var (outputName, amountMinor) in new[]
                 {
                     (GstSeedData.OutputLedgerNames[0], tax.TotalCgstMinor),
                     (GstSeedData.OutputLedgerNames[1], tax.TotalSgstMinor),
                     (GstSeedData.OutputLedgerNames[2], tax.TotalIgstMinor),
                     (GstSeedData.OutputLedgerNames[3], tax.TotalCessMinor),
                 })
        {
            if (amountMinor == 0L)
            {
                continue;
            }

            wanted.Add(outputName);

            // Reverse charge books both sides of the head; a purchase claims the input side; a
            // plain sale charges output only.
            if (tax.IsReverseCharge || !isSale)
            {
                wanted.Add(InputLedgerNameFor(outputName));
            }
        }

        if (tax.RoundOffMinor != 0L && !(isSale && tax.IsReverseCharge))
        {
            wanted.Add(tax.RoundOffLedgerName);
        }

        var resolved = new Dictionary<string, Ledger>(StringComparer.Ordinal);

        foreach (var name in wanted.Distinct(StringComparer.Ordinal))
        {
            var normalised = NameNormaliser.Normalise(name);

            var ledger = await _unitOfWork.Repository<Ledger>()
                .SingleOrDefaultAsync(new LedgerByName(companyId, normalised), cancellationToken)
                .ConfigureAwait(false);

            resolved[name] = ledger ?? throw new InvalidOperationException(
                $"The '{name}' account is missing from this company's chart of accounts, so this "
                + "invoice's GST cannot post. Run GST provisioning before posting configured invoices.");
        }

        return resolved;
    }

    private static string ComposeNarration(TradeDocument document, InvoicePlan plan) =>
        $"{document.Kind}: {plan.PartyLedger.Name}"
        + (string.IsNullOrWhiteSpace(document.Narration) ? string.Empty : $" — {document.Narration.Trim()}");

    private async Task ApplySerialLifecycleAsync(
        Guid companyId,
        DateOnly documentDate,
        PlannedMovement movement,
        StockVoucherLine mirrorLine,
        bool isSale,
        CancellationToken cancellationToken)
    {
        if (!movement.Item.IsSerialTracked || movement.Serials.Count == 0)
        {
            return;
        }

        foreach (var serialValue in movement.Serials)
        {
            var key = NameNormaliser.Normalise(serialValue);
            var existing = await FindSerialAsync(companyId, movement.Item.Id, key, cancellationToken)
                .ConfigureAwait(false);

            if (isSale)
            {
                // Validation already checked availability; a vanished unit here means something is
                // writing concurrently, so refusing loudly beats guessing.
                if (existing is null || existing.Status != SerialNumberStatus.InStock)
                {
                    throw new InvalidOperationException(
                        $"Serial '{serialValue}' of '{movement.Item.Name}' changed state between "
                        + "validation and posting; nothing was committed.");
                }

                existing.MarkSold(mirrorLine.Id, documentDate);
                _unitOfWork.Repository<SerialNumber>().Update(existing);
            }
            else
            {
                await _unitOfWork.Repository<SerialNumber>().AddAsync(
                    new SerialNumber(
                        companyId,
                        movement.Item.Id,
                        serialValue,
                        movement.GodownId,
                        mirrorLine.Id,
                        documentDate),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static StockMovementKind ReverseKind(StockMovementKind kind) => kind switch
    {
        StockMovementKind.Sale => StockMovementKind.SaleCancellation,
        StockMovementKind.Purchase => StockMovementKind.PurchaseCancellation,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "Only invoice movements can be reversed."),
    };

    // -----------------------------------------------------------------------------------------
    // Outstanding reports (checkpoint C3.4)
    // -----------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<OrderOutstandingRow>> OutstandingForAsync(
        TradeDocumentKind orderKind,
        CancellationToken cancellationToken)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var orders = await _unitOfWork.Repository<TradeDocument>()
            .ListAsync(new OpenOrders(companyId, orderKind), cancellationToken)
            .ConfigureAwait(false);

        if (orders.Count == 0)
        {
            return [];
        }

        var orderIds = orders.Select(order => order.Id).ToList();

        var successors = await _unitOfWork.Repository<TradeDocument>()
            .ListAsync(new ConvertedFromDocuments(orderIds), cancellationToken)
            .ConfigureAwait(false);

        var orderIdBySuccessorId = successors.ToDictionary(row => row.Id, row => row.ConvertedFromDocumentId!.Value);

        var consumedByOrderAndItem = new Dictionary<(Guid OrderId, Guid ItemId), decimal>();

        if (successors.Count > 0)
        {
            foreach (var line in await _unitOfWork.Repository<TradeDocumentLine>()
                         .ListAsync(new LinesOfDocuments(successors.Select(row => row.Id).ToList()),
                             cancellationToken)
                         .ConfigureAwait(false))
            {
                var key = (orderIdBySuccessorId[line.TradeDocumentId], line.ItemId);
                consumedByOrderAndItem[key] = consumedByOrderAndItem.GetValueOrDefault(key) + line.Quantity;
            }
        }

        var itemNames = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id, item => item.Name);

        var partyNames = await PartyNamesAsync(
                orders.Select(order => order.PartyLedgerId).Distinct().ToList(),
                cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<OrderOutstandingRow>();

        foreach (var order in orders.OrderBy(order => order.DocumentDate))
        {
            foreach (var line in order.Lines.OrderBy(line => line.LineNumber))
            {
                var pending = line.Quantity
                    - consumedByOrderAndItem.GetValueOrDefault((order.Id, line.ItemId));

                if (pending <= 0m)
                {
                    continue;
                }

                rows.Add(new OrderOutstandingRow(
                    order.Id,
                    order.Number ?? string.Empty,
                    order.DocumentDate,
                    order.PartyLedgerId,
                    partyNames.GetValueOrDefault(order.PartyLedgerId, "?"),
                    line.ItemId,
                    itemNames.GetValueOrDefault(line.ItemId, "?"),
                    line.Quantity,
                    pending,
                    line.Rate,
                    MoneyScale.Round(pending * line.Rate)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> PartyNamesAsync(
        IReadOnlyList<Guid> ledgerIds,
        CancellationToken cancellationToken)
    {
        if (ledgerIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return (await _unitOfWork.Repository<Ledger>()
                .ListAsync(new LedgersByIds(ledgerIds), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(ledger => ledger.Id, ledger => ledger.Name);
    }

    // -----------------------------------------------------------------------------------------
    // Small helpers
    // -----------------------------------------------------------------------------------------

    private async Task<AllocatedVoucherNumber> AllocateNumberAsync(
        Guid companyId,
        TradeDocumentKind kind,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        var voucherType = await RequirePipelineVoucherTypeAsync(companyId, kind, cancellationToken)
            .ConfigureAwait(false);

        return await _numberAllocator.AllocateAsync(
                companyId,
                voucherType.Id,
                documentDate,
                manualNumber: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<VoucherType> RequirePipelineVoucherTypeAsync(
        Guid companyId,
        TradeDocumentKind kind,
        CancellationToken cancellationToken)
    {
        var name = PipelineSeedData.VoucherTypeNameFor(kind);

        var voucherType = await _unitOfWork.Repository<VoucherType>()
            .SingleOrDefaultAsync(
                new VoucherTypeByName(companyId, NameNormaliser.Normalise(name)),
                cancellationToken)
            .ConfigureAwait(false);

        return voucherType ?? throw new InvalidOperationException(
            $"The '{name}' voucher type is missing from this company's books. Run pipeline "
            + "provisioning before raising trade documents.");
    }

    private async Task<Ledger?> RequireNamedLedgerAsync(
        Guid companyId,
        string name,
        string consequence,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(name);

        var ledger = await _unitOfWork.Repository<Ledger>()
            .SingleOrDefaultAsync(new LedgerByName(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null)
        {
            errors.Add(
                $"The '{name}' account is missing from this company's chart of accounts, so "
                + $"{consequence}. Run provisioning before posting.");
            return null;
        }

        return ledger;
    }

    private async Task<TradeDocument> RequireDocumentAsync(
        Guid documentId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var document = await _unitOfWork.Repository<TradeDocument>()
            .GetByIdAsync(documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null || document.CompanyId != companyId)
        {
            throw new InvalidOperationException("That trade document does not exist in this company's books.");
        }

        return document;
    }

    private async Task<TradeDocument> RequireDocumentWithLinesAsync(
        Guid documentId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var document = await _unitOfWork.Repository<TradeDocument>()
            .SingleOrDefaultAsync(new DocumentWithLines(documentId), cancellationToken)
            .ConfigureAwait(false);

        if (document is null || document.CompanyId != companyId)
        {
            throw new InvalidOperationException("That trade document does not exist in this company's books.");
        }

        return document;
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

    private static string Describe(IReadOnlyList<TradeDocumentKind> targets) =>
        targets.Count == 0
            ? "nothing"
            : string.Join(" or ", targets.Select(target => target.ToString()));

    // -----------------------------------------------------------------------------------------
    // Summaries
    // -----------------------------------------------------------------------------------------

    private async Task<TradeDocumentSummary> SummariseWithLookupsAsync(
        TradeDocument document,
        CancellationToken cancellationToken)
    {
        var companyId = document.CompanyId;

        var itemNames = (await _unitOfWork.Repository<Item>()
                .ListAsync(new ItemsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id, item => item.Name);

        var godownNames = (await _unitOfWork.Repository<Godown>()
                .ListAsync(new GodownsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(godown => godown.Id, godown => godown.Name);

        var party = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(document.PartyLedgerId, cancellationToken)
            .ConfigureAwait(false);

        return SummariseCore(document, itemNames, godownNames, party?.Name ?? "?");
    }

    private static TradeDocumentSummary SummariseCore(
        TradeDocument document,
        IReadOnlyDictionary<Guid, string> itemNames,
        IReadOnlyDictionary<Guid, string> godownNames,
        string partyName) =>
        new(
            document.Id,
            document.Kind,
            document.Status,
            document.Number ?? string.Empty,
            document.DocumentDate,
            document.PartyLedgerId,
            partyName,
            document.Narration,
            document.PartyReferenceNumber,
            document.PartyReferenceDate,
            document.ConvertedFromDocumentId,
            document.ConvertedFromNumber,
            document.PostedVoucherId,
            document.PostedNumber,
            document.Lines.Sum(line => line.NetValue),
            document.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new TradeDocumentLineSummary(
                    line.LineNumber,
                    line.ItemId,
                    itemNames.GetValueOrDefault(line.ItemId, "?"),
                    line.GodownId,
                    line.GodownId.HasValue ? godownNames.GetValueOrDefault(line.GodownId.Value, "?") : null,
                    line.Quantity,
                    line.Rate,
                    line.DiscountPercent,
                    line.NetValue,
                    line.BatchNumber,
                    line.ParsedSerials,
                    line.AppliedRateMinor,
                    line.IsPriceOverridden,
                    line.PriceSource))
                .ToList());

    // -----------------------------------------------------------------------------------------
    // Specifications
    // -----------------------------------------------------------------------------------------

    private sealed class DocumentWithLines : Specification<TradeDocument>
    {
        public DocumentWithLines(Guid id)
        {
            Where(document => document.Id == id);
            AddInclude(document => document.Lines);
        }
    }

    private sealed class RecentDocuments : Specification<TradeDocument>
    {
        public RecentDocuments(Guid companyId, IReadOnlyList<TradeDocumentKind> kinds, int take)
        {
            Where(document => document.CompanyId == companyId
                && (kinds.Count == 0 || kinds.Contains(document.Kind)));
            ApplyOrderByDescending(document => document.DocumentDate);
            ApplyPaging(0, take);
            AddInclude(document => document.Lines);
            ApplyNoTracking();
        }
    }

    private sealed class OpenOrders : Specification<TradeDocument>
    {
        public OpenOrders(Guid companyId, TradeDocumentKind kind)
        {
            Where(document => document.CompanyId == companyId
                && document.Kind == kind
                && document.Status == TradeDocumentStatus.Open);
            ApplyOrderBy(document => document.DocumentDate);
            AddInclude(document => document.Lines);
            ApplyNoTracking();
        }
    }

    private sealed class LinesOfDocuments : Specification<TradeDocumentLine>
    {
        public LinesOfDocuments(IReadOnlyList<Guid> documentIds)
        {
            Where(line => documentIds.Contains(line.TradeDocumentId));
            ApplyOrderBy(line => line.LineNumber);
        }
    }

    private sealed class ConvertedFrom : Specification<TradeDocument>
    {
        public ConvertedFrom(Guid sourceId)
        {
            Where(document => document.ConvertedFromDocumentId == sourceId);
            ApplyNoTracking();
        }
    }

    private sealed class ConvertedFromDocuments : Specification<TradeDocument>
    {
        public ConvertedFromDocuments(IReadOnlyList<Guid> sourceIds)
        {
            Where(document => document.ConvertedFromDocumentId != null
                && sourceIds.Contains(document.ConvertedFromDocumentId.Value)
                && document.Status != TradeDocumentStatus.Cancelled);
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

    private sealed class LedgersByIds : Specification<Ledger>
    {
        public LedgersByIds(IReadOnlyList<Guid> ids)
        {
            Where(ledger => ids.Contains(ledger.Id));
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

    private sealed class VoucherTypeByName : Specification<VoucherType>
    {
        public VoucherTypeByName(Guid companyId, string nameNormalised)
        {
            Where(type => type.CompanyId == companyId && type.NameNormalised == nameNormalised);
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

    private sealed class EntriesOfVoucher : Specification<StockLedgerEntry>
    {
        public EntriesOfVoucher(Guid stockVoucherId)
        {
            Where(entry => entry.StockVoucherId == stockVoucherId);
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

    private sealed class SerialsSoldOn : Specification<SerialNumber>
    {
        public SerialsSoldOn(IReadOnlyList<Guid> saleLineIds)
        {
            Where(serial => serial.Status == SerialNumberStatus.Sold
                && serial.SaleLineId != null
                && saleLineIds.Contains(serial.SaleLineId.Value));
            ApplyNoTracking();
        }
    }

    private sealed class MirrorOfVoucher : Specification<StockVoucher>
    {
        public MirrorOfVoucher(Guid companyId, Guid postedVoucherId)
        {
            Where(voucher => voucher.CompanyId == companyId
                && voucher.PostedVoucherId == postedVoucherId);
            AddInclude(voucher => voucher.Lines);
        }
    }
}
