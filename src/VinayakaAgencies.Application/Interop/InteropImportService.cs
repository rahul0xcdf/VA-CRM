using System.Linq.Expressions;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// The two-pass importer (phase-8-interoperability.md §2, §4a D2).
/// </summary>
/// <remarks>
/// <para>
/// Pass one reads the whole source and reports every problem in it. Pass two reads it again and
/// writes, inside one transaction. Reading twice rather than holding the file is what lets both
/// promises hold at once: §2 requires the whole file validated before anything is written, and §4a
/// D2 requires that a full history never be materialised in memory. Only the master index — names
/// and identities, thousands of rows — is held across the passes.
/// </para>
/// <para>
/// Accounting reaches the books through <see cref="IVoucherPostingService"/> and nowhere else. An
/// importer writing journal lines directly would bypass the balance check, the period lock, the
/// numbering series and the edit log in one stroke, and a migration is the worst possible moment to
/// have done so — it is the one run where nobody is watching each voucher go in.
/// </para>
/// </remarks>
public sealed class InteropImportService : IInteropImportService
{
    /// <summary>
    /// Records written between one <c>SaveChanges</c> and the next.
    /// </summary>
    /// <remarks>
    /// The transaction still spans the whole import — this only bounds how much the change tracker
    /// holds at once, which is what stops a full history from growing memory linearly as it goes.
    /// </remarks>
    private const int BatchSize = 200;

    /// <summary>Zero-pad width for a series created on import, matching the §7.2 default.</summary>
    private const int DefaultSeriesWidth = 4;

    /// <summary>Far enough ahead that a balance "as on" it includes every imported date.</summary>
    /// <remarks>
    /// 9998 rather than 9999 because resolving a date to its financial year adds a year to it
    /// (<c>FinancialYear.RangeFor</c>), and <c>DateOnly.MaxValue</c> has no year to add.
    /// </remarks>
    private static readonly DateOnly EndOfTime = new(9998, 12, 31);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICompanyContext _companyContext;
    private readonly IVoucherPostingService _voucherPostingService;
    private readonly IStockReportService _stockReports;
    private readonly ISqlQueryExecutor _queryExecutor;

    public InteropImportService(
        IUnitOfWork unitOfWork,
        ICompanyContext companyContext,
        IVoucherPostingService voucherPostingService,
        IStockReportService stockReports,
        ISqlQueryExecutor queryExecutor)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(voucherPostingService);
        ArgumentNullException.ThrowIfNull(stockReports);
        ArgumentNullException.ThrowIfNull(queryExecutor);

        _unitOfWork = unitOfWork;
        _companyContext = companyContext;
        _voucherPostingService = voucherPostingService;
        _stockReports = stockReports;
        _queryExecutor = queryExecutor;
    }

    public async Task<InteropImportPreview> ValidateAsync(
        IInteropSource source,
        InteropImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);

        return await ValidateCoreAsync(source, options, index, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InteropImportResult> CommitAsync(
        IInteropSource source,
        InteropImportOptions options,
        IProgress<InteropImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var preview = await ValidateAsync(source, options, cancellationToken).ConfigureAwait(false);

        if (preview.Problems.Count > 0)
        {
            return InteropImportResult.Refused(
                preview,
                $"{preview.Problems.Count} problem(s) in {source.Description}. Nothing was written — "
                + "fix the source and run it again.");
        }

        if (preview.RecordCount == 0)
        {
            return InteropImportResult.Refused(preview, $"{source.Description} holds nothing to import.");
        }

        // Read before the transaction opens, because the report services read through their own
        // connection (Dapper via ISqlQueryExecutor) and would not see uncommitted rows. C8.6 asks
        // what the books moved by, which is a before-and-after question anyway.
        var before = await SnapshotAsync(cancellationToken).ConfigureAwait(false);

        // A second index, loaded fresh. The validation pass registered records it had not written, so
        // reusing it would have the writer believe masters exist that were never created.
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);

        await using (var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await WritePassAsync(source, options, index, preview, progress, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        var after = await SnapshotAsync(cancellationToken).ConfigureAwait(false);

        return new InteropImportResult(
            preview,
            Committed: true,
            RefusalReason: null,
            Reconciliation: new InteropReconciliation(
                preview.ParsedTotals.TotalDebitMinor,
                preview.ParsedTotals.TotalCreditMinor,
                after.DebitMinor - before.DebitMinor,
                after.CreditMinor - before.CreditMinor,
                preview.ParsedTotals.ClosingStockValueMinor ?? 0L,
                after.StockValueMinor - before.StockValueMinor,
                // No Tally format carries a declared total — the envelope has no element for one — so
                // nothing is claimed here. InteropReconciliation reports that as "not declared"
                // rather than as "matched", which is the distinction §4a D2 cares about.
                DeclaredDebitMinor: null,
                DeclaredCreditMinor: null));
    }

    private async Task<InteropMasterIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var company = await _unitOfWork.Repository<Company>()
            .GetByIdAsync(companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("No company is open, so there is nothing to import into.");

        return await InteropMasterIndex
            .LoadAsync(_unitOfWork, companyId, company.FinancialYearStartMonth, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<BooksSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var totals = await _queryExecutor.QuerySingleOrDefaultAsync<JournalTotalsRow>(
            InteropQueryKeys.JournalTotals,
            new { CompanyId = companyId },
            cancellationToken).ConfigureAwait(false);

        var stock = await _stockReports
            .ReconcileValuationAsync(EndOfTime, cancellationToken)
            .ConfigureAwait(false);

        return new BooksSnapshot(
            totals?.DebitMinor ?? 0L,
            totals?.CreditMinor ?? 0L,
            stock.TotalStockValueMinor);
    }

    private static async Task<InteropImportPreview> ValidateCoreAsync(
        IInteropSource source,
        InteropImportOptions options,
        InteropMasterIndex index,
        CancellationToken cancellationToken)
    {
        var pass = new ValidationPass(index, options);

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            pass.Accept(record);
        }

        return pass.ToPreview(source);
    }

    private async Task WritePassAsync(
        IInteropSource source,
        InteropImportOptions options,
        InteropMasterIndex index,
        InteropImportPreview preview,
        IProgress<InteropImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var written = 0;
        var sinceSave = 0;
        var sequence = await NextStockSequenceAsync(index.CompanyId, cancellationToken).ConfigureAwait(false);

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Wanted(record, options))
            {
                continue;
            }

            switch (record)
            {
                case InteropRecord.Unit unit:
                    await WriteUnitAsync(unit.Value, index, cancellationToken).ConfigureAwait(false);
                    break;

                case InteropRecord.StockGroup group:
                    await WriteStockGroupAsync(group.Value, index, cancellationToken).ConfigureAwait(false);
                    break;

                case InteropRecord.Godown godown:
                    await WriteGodownAsync(godown.Value, index, cancellationToken).ConfigureAwait(false);
                    break;

                case InteropRecord.Item item:
                    await WriteStockItemAsync(item.Value, index, cancellationToken).ConfigureAwait(false);
                    break;

                case InteropRecord.AccountGroup accountGroup:
                    await WriteAccountGroupAsync(accountGroup.Value, index, cancellationToken).ConfigureAwait(false);
                    break;

                case InteropRecord.Ledger ledger:
                    await WriteLedgerAsync(ledger.Value, index, cancellationToken).ConfigureAwait(false);
                    break;

                case InteropRecord.VoucherType voucherType:
                    await WriteVoucherTypeAsync(voucherType.Value, index, cancellationToken).ConfigureAwait(false);
                    break;

                case InteropRecord.Voucher voucher:
                    // Masters staged earlier in this pass have to be in the database before a voucher
                    // can reference them: the posting service reads ledgers through its own queries.
                    await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    sinceSave = 0;

                    sequence = await WriteVoucherAsync(
                        voucher.Value,
                        index,
                        source.Description,
                        sequence,
                        cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    continue;
            }

            written++;
            sinceSave++;

            if (sinceSave >= BatchSize)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                sinceSave = 0;
            }

            progress?.Report(new InteropImportProgress(written, preview.RecordCount, record.Kind));
        }
    }

    private static bool Wanted(InteropRecord record, InteropImportOptions options) => record switch
    {
        InteropRecord.Voucher => options.ImportVouchers,

        // A voucher type comes in under either selection: it is a master, but refusing it under
        // "vouchers only" would refuse every voucher filed under it.
        InteropRecord.VoucherType => true,
        InteropRecord.Unreadable => false,
        _ => options.ImportMasters,
    };

    private async Task WriteUnitAsync(InteropUnit source, InteropMasterIndex index, CancellationToken cancellationToken)
    {
        var existingId = index.Units.Match(source.Id, source.Code);

        if (existingId is { } id
            && await _unitOfWork.Repository<UnitOfMeasure>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            Apply(existing, source);
            index.Units.Register(existing.Id, existing.Code);
            return;
        }

        var created = new UnitOfMeasure(index.CompanyId, source.Code, source.DecimalPlaces, source.Description);
        SetActive(created.Activate, created.Deactivate, source.IsActive);

        await _unitOfWork.Repository<UnitOfMeasure>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        index.Units.Register(created.Id, created.Code);
    }

    private static void Apply(UnitOfMeasure existing, InteropUnit source)
    {
        if (!string.Equals(existing.Code, source.Code, StringComparison.Ordinal))
        {
            existing.Rename(source.Code);
        }

        if (existing.DecimalPlaces != source.DecimalPlaces)
        {
            existing.SetDecimalPlaces(source.DecimalPlaces);
        }

        existing.SetDescription(source.Description);
        SetActive(existing.Activate, existing.Deactivate, source.IsActive);
    }

    private async Task WriteStockGroupAsync(InteropStockGroup source, InteropMasterIndex index, CancellationToken cancellationToken)
    {
        var existingId = index.StockGroups.Match(source.Id, source.Name);

        if (existingId is { } id
            && await _unitOfWork.Repository<StockGroup>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (!string.Equals(existing.Name, source.Name, StringComparison.Ordinal))
            {
                existing.Rename(source.Name);
            }

            existing.SetDescription(source.Description);
            SetActive(existing.Activate, existing.Deactivate, source.IsActive);
            index.StockGroups.Register(existing.Id, existing.Name);
            return;
        }

        var created = new StockGroup(index.CompanyId, source.Name, source.Description);
        SetActive(created.Activate, created.Deactivate, source.IsActive);

        await _unitOfWork.Repository<StockGroup>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        index.StockGroups.Register(created.Id, created.Name);
    }

    private async Task WriteGodownAsync(InteropGodown source, InteropMasterIndex index, CancellationToken cancellationToken)
    {
        var existingId = index.Godowns.Match(source.Id, source.Name);

        if (existingId is { } id
            && await _unitOfWork.Repository<Godown>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (!string.Equals(existing.Name, source.Name, StringComparison.Ordinal))
            {
                existing.Rename(source.Name);
            }

            existing.SetAddress(source.Address);
            SetActive(existing.Activate, existing.Deactivate, source.IsActive);
            index.Godowns.Register(existing.Id, existing.Name);
            return;
        }

        var created = new Godown(index.CompanyId, source.Name, source.Address);
        SetActive(created.Activate, created.Deactivate, source.IsActive);

        await _unitOfWork.Repository<Godown>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        index.Godowns.Register(created.Id, created.Name);
    }

    private async Task WriteStockItemAsync(
        InteropItem source,
        InteropMasterIndex index,
        CancellationToken cancellationToken)
    {
        var baseUnitId = index.Units.Resolve(source.BaseUnitCode)!.Value;
        var stockGroupId = index.StockGroups.Resolve(source.StockGroupName);
        var hsnId = await ResolveOrCreateHsnAsync(source.HsnSacCode, index, cancellationToken).ConfigureAwait(false);
        var existingId = index.Items.Match(source.Id, source.Name);

        if (existingId is { } id
            && await _unitOfWork.Repository<Item>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (!string.Equals(existing.Name, source.Name, StringComparison.Ordinal))
            {
                existing.Rename(source.Name);
            }

            existing.SetCode(source.Code);
            existing.MoveToGroup(stockGroupId);
            existing.AssignHsnSac(hsnId);

            if (existing.BaseUnitId != baseUnitId)
            {
                existing.SetUnits(baseUnitId, existing.AlternateUnitId, existing.ConversionFactor);
            }

            SetActive(existing.Activate, existing.Deactivate, source.IsActive);
            index.Items.Register(existing.Id, existing.Name);
            index.ItemsById[existing.Id] = existing;
            return;
        }

        var created = new Item(index.CompanyId, source.Name, baseUnitId, stockGroupId, source.Code);
        created.AssignHsnSac(hsnId);
        SetActive(created.Activate, created.Deactivate, source.IsActive);

        await _unitOfWork.Repository<Item>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        index.Items.Register(created.Id, created.Name);
        index.ItemsById[created.Id] = created;
    }

    /// <summary>
    /// The HSN/SAC row an imported item's code refers to, created if the books have not seen it.
    /// </summary>
    /// <remarks>
    /// Creating one here does not breach the "nothing tax-related is hardcoded" rule (CLAUDE.md
    /// non-negotiable 6). That rule is about codes and rates being data rather than constants in
    /// <c>src/</c>; a code arriving in the owner's own Tally export is the owner's data, and refusing
    /// to record it would lose the classification the invoices were actually raised under. No
    /// <i>rate</i> is invented — the schedule stays empty until someone enters one.
    /// </remarks>
    private async Task<Guid?> ResolveOrCreateHsnAsync(
        string? code,
        InteropMasterIndex index,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (index.HsnCodes.Resolve(code) is { } existing)
        {
            return existing;
        }

        var created = new HsnSac(index.CompanyId, code);
        await _unitOfWork.Repository<HsnSac>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        index.HsnCodes.Register(created.Id, created.Code);

        return created.Id;
    }

    private async Task WriteAccountGroupAsync(
        InteropAccountGroup source,
        InteropMasterIndex index,
        CancellationToken cancellationToken)
    {
        var parent = index.ResolveAccountGroup(source.ParentName);
        var existing = index.FindAccountGroup(source.Id, source.Name);

        if (existing is not null)
        {
            if (!string.Equals(existing.Name, source.Name, StringComparison.Ordinal))
            {
                existing.Rename(source.Name);
            }

            if (existing.ParentGroupId != parent?.Id)
            {
                existing.MoveTo(parent?.Id);
                existing.SetMaterialisedPath(parent?.MaterialisedPath);
            }

            SetActive(existing.Activate, existing.Deactivate, source.IsActive);
            index.RegisterAccountGroup(existing);
            return;
        }

        var created = new AccountGroup(
            index.CompanyId,
            source.Name,
            // A group whose parent is in the books inherits its nature, exactly as provisioning does:
            // a sub-group disagreeing with its parent lands its balances on the wrong statement.
            parent?.Nature ?? source.Nature,
            parent?.Id,
            parent?.AffectsGrossProfit ?? source.AffectsGrossProfit);

        created.SetMaterialisedPath(parent?.MaterialisedPath);
        SetActive(created.Activate, created.Deactivate, source.IsActive);

        await _unitOfWork.Repository<AccountGroup>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        index.RegisterAccountGroup(created);
    }

    private async Task WriteLedgerAsync(
        InteropLedger source,
        InteropMasterIndex index,
        CancellationToken cancellationToken)
    {
        var group = index.ResolveAccountGroup(source.ParentGroupName)!;
        var existingId = index.Ledgers.Match(source.Id, source.Name);

        if (existingId is { } id
            && await _unitOfWork.Repository<Ledger>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (!string.Equals(existing.Name, source.Name, StringComparison.Ordinal)
                || !string.Equals(existing.Alias, source.Alias, StringComparison.Ordinal))
            {
                existing.Rename(source.Name, source.Alias);
            }

            if (existing.AccountGroupId != group.Id)
            {
                existing.MoveTo(group.Id);
            }

            existing.SetDescription(source.Description);
            SetActive(existing.Activate, existing.Deactivate, source.IsActive);
            index.Ledgers.Register(existing.Id, existing.Name);
            return;
        }

        var created = new Ledger(
            index.CompanyId,
            source.Name,
            group.Id,
            source.Alias,
            isSystem: false,
            allowsManualPosting: true,
            source.Description);

        SetActive(created.Activate, created.Deactivate, source.IsActive);

        await _unitOfWork.Repository<Ledger>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        index.Ledgers.Register(created.Id, created.Name);
    }

    /// <summary>
    /// Creates a voucher type the books have not got, together with the series it numbers from.
    /// </summary>
    /// <remarks>
    /// A voucher type cannot post without a numbering series — <c>Voucher</c> requires a series id,
    /// and it is a key column of the index that enforces gapless numbering. Creating the type alone
    /// would produce a master that looks fine on screen and refuses every voucher filed under it, so
    /// the pair is created together exactly as company provisioning creates them.
    /// <para>
    /// The prefix comes from the file when it carries one, so a series that read "SJ/0001" before the
    /// migration still reads "SJ/0001" after it.
    /// </para>
    /// </remarks>
    private async Task WriteVoucherTypeAsync(
        InteropVoucherType source,
        InteropMasterIndex index,
        CancellationToken cancellationToken)
    {
        var existing = index.ResolveVoucherType(source.Name, kind: null);

        if (existing is not null)
        {
            if (!source.IsActive)
            {
                existing.Deactivate();
            }
            else
            {
                existing.Activate();
            }

            index.RegisterVoucherType(existing);
            return;
        }

        var created = new VoucherType(index.CompanyId, source.Name, source.Kind);

        var series = new VoucherNumberSeries(
            index.CompanyId,
            created.Id,
            source.Name,
            string.IsNullOrWhiteSpace(source.NumberPrefix) ? DefaultPrefixFor(source.Name) : source.NumberPrefix,
            width: DefaultSeriesWidth,
            suffix: null,
            startNumber: 1L,
            resetPolicy: NumberResetPolicy.FinancialYear,
            allowManualOverride: true);

        created.SetDefaultSeries(series.Id);

        if (!source.IsActive)
        {
            created.Deactivate();
        }

        await _unitOfWork.Repository<VoucherType>().AddAsync(created, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.Repository<VoucherNumberSeries>().AddAsync(series, cancellationToken).ConfigureAwait(false);

        index.RegisterVoucherType(created);
    }

    /// <summary>A prefix built from the type's name, for a file that carried none.</summary>
    /// <remarks>
    /// Three letters and a slash, matching the §7.2 default the eight seeded series use. Imported
    /// vouchers carry their own numbers as manual overrides, so this only governs what happens when
    /// the owner later files a new voucher under the imported type.
    /// </remarks>
    private static string DefaultPrefixFor(string name)
    {
        var letters = new string([.. name.Where(char.IsLetter).Take(3)]).ToUpperInvariant();

        return (letters.Length == 0 ? "IMP" : letters) + "/";
    }

    private async Task<long> WriteVoucherAsync(
        InteropVoucher source,
        InteropMasterIndex index,
        string sourceDescription,
        long nextSequence,
        CancellationToken cancellationToken)
    {
        var type = index.ResolveVoucherType(source.VoucherTypeName, source.Kind)!;
        var key = index.VoucherKey(type.Id, source.Number, source.Date);

        var existingId = source.Id is { } id && index.VoucherIds.Contains(id)
            ? id
            : index.VoucherIdByTypeNumberAndYear.TryGetValue(key, out var found) ? found : (Guid?)null;

        if (source.IsCancelled)
        {
            // A cancellation the books have never seen has nothing to act on. Validation has already
            // told the owner about it; inventing a voucher solely to cancel it would burn a number in
            // a series that has to stay gapless (open-questions.md #11).
            if (existingId is { } cancelId)
            {
                var current = await _voucherPostingService
                    .GetCurrentAsync(cancelId, cancellationToken)
                    .ConfigureAwait(false);

                if (current is not null && current.Status != VoucherStatus.Cancelled)
                {
                    await _voucherPostingService
                        .CancelAsync(cancelId, $"Cancelled in {sourceDescription}", cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return nextSequence;
        }

        var lines = BuildLines(source, index);

        if (existingId is { } alterId)
        {
            var current = await _voucherPostingService
                .GetCurrentAsync(alterId, cancellationToken)
                .ConfigureAwait(false);

            // An unchanged voucher is left alone. Altering writes a new revision (ADR-0002), so
            // re-running the same file would otherwise add a revision to every voucher in the books
            // and leave an edit log full of changes that changed nothing.
            if (current is not null && Matches(current, source, lines))
            {
                return nextSequence;
            }

            await _voucherPostingService.AlterAsync(
                    new AlterVoucherRequest(
                        alterId,
                        source.Date,
                        lines,
                        $"Re-imported from {sourceDescription}",
                        source.Narration,
                        source.ReferenceNumber,
                        source.ReferenceDate),
                    cancellationToken)
                .ConfigureAwait(false);

            return nextSequence;
        }

        var posted = await _voucherPostingService.PostAsync(
                new PostVoucherRequest(
                    type.Id,
                    source.Date,
                    lines,
                    source.Narration,
                    source.ReferenceNumber,
                    source.ReferenceDate,
                    ManualNumber: string.IsNullOrWhiteSpace(source.Number) ? null : source.Number),
                cancellationToken)
            .ConfigureAwait(false);

        index.VoucherIds.Add(posted.VoucherId);
        index.VoucherIdByTypeNumberAndYear[index.VoucherKey(type.Id, posted.Number, source.Date)] = posted.VoucherId;

        return await WriteStockMirrorsAsync(source, posted, index, nextSequence, cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<PostVoucherLine> BuildLines(InteropVoucher source, InteropMasterIndex index) =>
    [
        .. source.LedgerEntries.Select(entry => new PostVoucherLine(
            index.Ledgers.Resolve(entry.LedgerName)!.Value,
            entry.AmountMinor > 0 ? MoneyScale.FromMinor(entry.AmountMinor) : 0m,
            entry.AmountMinor < 0 ? MoneyScale.FromMinor(-entry.AmountMinor) : 0m,
            entry.Narration,
            index.CostCentres.Resolve(entry.CostCentreName))),
    ];

    /// <summary>Whether the posted voucher already says exactly what the file says.</summary>
    private static bool Matches(
        PostedVoucherSummary current,
        InteropVoucher source,
        IReadOnlyList<PostVoucherLine> lines)
    {
        if (current.VoucherDate != source.Date
            || !string.Equals(current.Narration, source.Narration, StringComparison.Ordinal)
            || current.Lines.Count != lines.Count)
        {
            return false;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var posted = current.Lines[index];
            var incoming = lines[index];

            if (posted.LedgerId != incoming.LedgerId
                || posted.Debit != incoming.Debit
                || posted.Credit != incoming.Credit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Mirrors the voucher's stock movements onto the unified stock ledger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shape Phase 3 posts an invoice in: a <see cref="StockVoucher"/> attached to the
    /// posted accounting voucher, plus one append-only <see cref="StockLedgerEntry"/> per line. The
    /// accounting effect is not posted twice — it came from the file's own ledger entries, so these
    /// mirrors carry no counter ledger.
    /// </para>
    /// <para>
    /// Costs come from the file rather than from this application's valuation engine, and that is
    /// deliberate. A migration carries history that has already been valued by the system it came
    /// from; re-costing it under our FIFO would produce a closing stock figure that does not match
    /// the source, which is precisely what C8.6 exists to catch.
    /// </para>
    /// </remarks>
    private async Task<long> WriteStockMirrorsAsync(
        InteropVoucher source,
        PostedVoucherSummary posted,
        InteropMasterIndex index,
        long nextSequence,
        CancellationToken cancellationToken)
    {
        if (source.InventoryEntries.Count == 0)
        {
            return nextSequence;
        }

        foreach (var direction in new[] { StockMovementDirection.Increase, StockMovementDirection.Decrease })
        {
            var entries = source.InventoryEntries
                .Where(entry => Direction(entry) == direction)
                .ToList();

            if (entries.Count == 0)
            {
                continue;
            }

            var purpose = direction == StockMovementDirection.Increase
                ? StockVoucherPurpose.Receipt
                : StockVoucherPurpose.Issue;

            var kind = direction == StockMovementDirection.Increase
                ? StockMovementKind.Receipt
                : StockMovementKind.Issue;

            var mirror = new StockVoucher(
                index.CompanyId,
                purpose,
                source.Date,
                $"Imported with {posted.Number}");

            mirror.AttachPostedVoucher(posted.VoucherId, posted.Number);

            var ledgerRows = new List<(StockVoucherLine Line, Guid ItemId, Guid GodownId, decimal Quantity, long CostMinor, string? Batch)>(entries.Count);

            foreach (var entry in entries)
            {
                var itemId = index.Items.Resolve(entry.StockItemName)!.Value;
                var item = index.ItemsById[itemId];
                var godownId = index.Godowns.Resolve(entry.GodownName) ?? DefaultGodown(index);
                var quantity = MoneyScale.FromMinor(Math.Abs(entry.QuantityMinor));
                var costMinor = entry.RateMinor ?? 0L;

                var line = mirror.AddLine(
                    direction,
                    itemId,
                    godownId,
                    quantity,
                    item.IsSerialTracked,
                    item.IsBatchTracked,
                    direction == StockMovementDirection.Increase ? MoneyScale.FromMinor(costMinor) : 0m,
                    entry.BatchName);

                // The issue side is left unvalued at construction because valuation normally owns it
                // (StockVoucherLine). Here the file owns it, so it is applied straight after.
                if (direction == StockMovementDirection.Decrease && costMinor > 0L)
                {
                    line.ApplyValuedCost(costMinor);
                }

                ledgerRows.Add((line, itemId, godownId, quantity, costMinor, entry.BatchName));
            }

            await _unitOfWork.Repository<StockVoucher>().AddAsync(mirror, cancellationToken).ConfigureAwait(false);

            foreach (var row in ledgerRows)
            {
                await _unitOfWork.Repository<StockLedgerEntry>().AddAsync(
                    new StockLedgerEntry(
                        index.CompanyId,
                        nextSequence++,
                        source.Date,
                        kind,
                        row.ItemId,
                        row.GodownId,
                        row.Quantity,
                        row.CostMinor,
                        mirror.Id,
                        row.Line.Id,
                        row.Batch),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return nextSequence;
    }

    private static StockMovementDirection Direction(InteropInventoryEntry entry) =>
        entry.QuantityMinor >= 0 ? StockMovementDirection.Increase : StockMovementDirection.Decrease;

    private static Guid DefaultGodown(InteropMasterIndex index) =>
        index.Godowns.Resolve(Godown.MainLocationName)
        ?? throw new InvalidOperationException(
            "The file names no godown and this company has no Main Location to fall back on.");

    private async Task<long> NextStockSequenceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var entries = await _unitOfWork.Repository<StockLedgerEntry>()
            .ListAsync(new NoTrackingCriteria<StockLedgerEntry>(entry => entry.CompanyId == companyId), cancellationToken)
            .ConfigureAwait(false);

        return entries.Count == 0 ? 1L : entries.Max(entry => entry.GlobalSequence) + 1L;
    }

    private static void SetActive(Action activate, Action deactivate, bool isActive)
    {
        if (isActive)
        {
            activate();
        }
        else
        {
            deactivate();
        }
    }

    private sealed record BooksSnapshot(long DebitMinor, long CreditMinor, long StockValueMinor);

    private sealed record JournalTotalsRow(long DebitMinor, long CreditMinor);

    private sealed class NoTrackingCriteria<TEntity> : Specification<TEntity>
        where TEntity : Entity
    {
        public NoTrackingCriteria(Expression<Func<TEntity, bool>> criteria)
        {
            Where(criteria);
            ApplyNoTracking();
        }
    }
}
