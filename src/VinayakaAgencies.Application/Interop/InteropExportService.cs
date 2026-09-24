using System.Linq.Expressions;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Reads the books into the canonical model (phase-8-interoperability.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Every record carries the stable entity GUID assigned at construction (ADR-0001, and the remark on
/// <c>Entity</c> itself). That is the whole mechanism behind C8.2: Tally overwrites a record whose
/// GUID it already holds, so exporting the same books twice updates rather than duplicates. A GUID
/// that changed between exports would silently double the accountant's chart of accounts.
/// </para>
/// <para>
/// Vouchers are read from current revisions only. The journal is append-only (ADR-0002), so an
/// altered voucher has several revisions and exactly one of them is the truth; exporting superseded
/// revisions would post the same voucher several times over.
/// </para>
/// </remarks>
public sealed class InteropExportService : IInteropExportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICompanyContext _companyContext;
    private readonly IClock _clock;

    public InteropExportService(IUnitOfWork unitOfWork, ICompanyContext companyContext, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(clock);

        _unitOfWork = unitOfWork;
        _companyContext = companyContext;
        _clock = clock;
    }

    public async Task<InteropDocument> BuildAsync(
        InteropExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var company = await _unitOfWork.Repository<Company>()
            .GetByIdAsync(companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("No company is open, so there are no books to export.");

        var masters = request.IncludeMasters
            ? await BuildMastersAsync(companyId, cancellationToken).ConfigureAwait(false)
            : MasterSet.Empty;

        var vouchers = request.IncludeVouchers
            ? await BuildVouchersAsync(companyId, request, cancellationToken).ConfigureAwait(false)
            : [];

        // Always written when vouchers are, and also with a masters-only export: the accountant's
        // company has to hold the type before it can accept a voucher filed under it, and the owner's
        // own types ("Stock Journal", the Phase 3 invoice types) are not names Tally ships with.
        var voucherTypes = request.IncludeVouchers || request.IncludeMasters
            ? await BuildVoucherTypesAsync(companyId, cancellationToken).ConfigureAwait(false)
            : [];

        return new InteropDocument
        {
            CompanyName = company.Name,
            CompanyGstin = company.Gstin,
            GeneratedAtUtc = _clock.UtcNow,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            AccountGroups = masters.AccountGroups,
            Ledgers = masters.Ledgers,
            Units = masters.Units,
            StockGroups = masters.StockGroups,
            Godowns = masters.Godowns,
            Items = masters.Items,
            VoucherTypes = voucherTypes,
            Vouchers = vouchers,
            DeclaredTotals = new InteropTotals(
                vouchers.Sum(voucher => voucher.TotalDebitMinor),
                vouchers.Sum(voucher => voucher.TotalCreditMinor)),
        };
    }

    public async Task<InteropExportSummary> WriteAsync(
        InteropDocument document,
        InteropFormat format,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        switch (format)
        {
            case InteropFormat.TallyXml:
                await TallyXmlWriter.WriteAsync(document, destination, cancellationToken).ConfigureAwait(false);
                break;

            case InteropFormat.TallyJson:
                await TallyJsonWriter.WriteAsync(document, destination, cancellationToken).ConfigureAwait(false);
                break;

            case InteropFormat.Excel:
                throw new InvalidOperationException(
                    "A workbook is written through IInteropWorkbookExporter, which owns the ClosedXML "
                    + "adapter. Resolve that service instead of routing Excel through this one.");

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format.");
        }

        return InteropExportSummary.For(document, format);
    }

    private async Task<MasterSet> BuildMastersAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var groups = await ListAsync(new Filtered<AccountGroup>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var ledgers = await ListAsync(new Filtered<Ledger>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var units = await ListAsync(new Filtered<UnitOfMeasure>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var stockGroups = await ListAsync(new Filtered<StockGroup>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var godowns = await ListAsync(new Filtered<Godown>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var items = await ListAsync(new Filtered<Item>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var hsnCodes = await ListAsync(new Filtered<HsnSac>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);

        var groupNameById = groups.ToDictionary(group => group.Id, group => group.Name);
        var unitCodeById = units.ToDictionary(unit => unit.Id, unit => unit.Code);
        var stockGroupNameById = stockGroups.ToDictionary(group => group.Id, group => group.Name);
        var hsnCodeById = hsnCodes.ToDictionary(code => code.Id, code => code.Code);

        return new MasterSet(
            // System groups are Tally's own standard tree — Sundry Debtors, Cash-in-Hand, Sales
            // Accounts and the rest — and every Tally company already has them under these names.
            // Sending ours would at best be a no-op and at worst duplicate the chart of accounts.
            // Ledgers below still name them as parents, and Tally resolves a parent by name.
            [
                .. groups
                    .Where(group => !group.IsSystem)
                    .OrderBy(group => group.MaterialisedPath, StringComparer.Ordinal)
                    .Select(group => new InteropAccountGroup(
                        group.Name,
                        group.ParentGroupId is { } parentId && groupNameById.TryGetValue(parentId, out var parentName)
                            ? parentName
                            : null,
                        group.Nature,
                        group.Id,
                        group.AffectsGrossProfit,
                        group.IsActive)),
            ],
            [
                .. ledgers
                    .OrderBy(ledger => ledger.NameNormalised, StringComparer.Ordinal)
                    .Select(ledger => new InteropLedger(
                        ledger.Name,
                        groupNameById.TryGetValue(ledger.AccountGroupId, out var parentName)
                            ? parentName
                            : string.Empty,
                        // Tally's built-in ledgers — Cash and Profit & Loss A/c — exist in the target
                        // company already, under a GUID of Tally's own. Sending ours against the same
                        // name asks the importer to reconcile two identities for one record. Omitting
                        // it lets the name match, which is how Tally identifies its built-ins, and
                        // this application's importer falls back to the same normalised name.
                        ledger.IsSystem ? null : ledger.Id,
                        ledger.Alias,
                        ledger.Description,
                        Gstin: null,
                        ledger.IsActive)),
            ],
            [
                .. units
                    .OrderBy(unit => unit.CodeNormalised, StringComparer.Ordinal)
                    .Select(unit => new InteropUnit(
                        unit.Code,
                        unit.DecimalPlaces,
                        unit.Id,
                        unit.Description,
                        unit.IsActive)),
            ],
            [
                .. stockGroups
                    .OrderBy(group => group.NameNormalised, StringComparer.Ordinal)
                    .Select(group => new InteropStockGroup(
                        group.Name,
                        group.Id,
                        group.Description,
                        group.IsActive)),
            ],
            [
                .. godowns
                    .OrderBy(godown => godown.NameNormalised, StringComparer.Ordinal)
                    .Select(godown => new InteropGodown(
                        godown.Name,
                        godown.IsSystem ? null : godown.Id,
                        godown.Address,
                        godown.IsActive)),
            ],
            [
                .. items
                    .OrderBy(item => item.NameNormalised, StringComparer.Ordinal)
                    .Select(item => new InteropItem(
                        item.Name,
                        unitCodeById.TryGetValue(item.BaseUnitId, out var unitCode) ? unitCode : string.Empty,
                        item.Id,
                        item.Code,
                        item.StockGroupId is { } stockGroupId
                            && stockGroupNameById.TryGetValue(stockGroupId, out var stockGroupName)
                            ? stockGroupName
                            : null,
                        item.HsnSacId is { } hsnId && hsnCodeById.TryGetValue(hsnId, out var hsn) ? hsn : null,
                        item.IsActive)),
            ]);
    }

    private async Task<IReadOnlyList<InteropVoucherType>> BuildVoucherTypesAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var types = await ListAsync(new Filtered<VoucherType>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var series = await ListAsync(new Filtered<VoucherNumberSeries>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var prefixBySeriesId = series.ToDictionary(row => row.Id, row => row.Prefix);

        return
        [
            .. types
                .OrderBy(type => type.NameNormalised, StringComparer.Ordinal)
                .Select(type => new InteropVoucherType(
                    type.Name,
                    type.Kind,
                    type.Id,
                    type.DefaultSeriesId is { } seriesId && prefixBySeriesId.TryGetValue(seriesId, out var prefix)
                        ? prefix
                        : null,
                    type.IsActive)),
        ];
    }

    private async Task<IReadOnlyList<InteropVoucher>> BuildVouchersAsync(
        Guid companyId,
        InteropExportRequest request,
        CancellationToken cancellationToken)
    {
        var revisions = await ListAsync(
            new CurrentRevisionsInPeriod(companyId, request.FromDate, request.ToDate),
            cancellationToken).ConfigureAwait(false);

        if (revisions.Count == 0)
        {
            return [];
        }

        var voucherTypes = await ListAsync(new Filtered<VoucherType>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var ledgers = await ListAsync(new Filtered<Ledger>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var costCentres = await ListAsync(new Filtered<CostCentre>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var items = await ListAsync(new Filtered<Item>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var units = await ListAsync(new Filtered<UnitOfMeasure>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var godowns = await ListAsync(new Filtered<Godown>(x => x.CompanyId == companyId), cancellationToken).ConfigureAwait(false);
        var stockVouchers = await ListAsync(new PostedStockVouchersOf(companyId), cancellationToken).ConfigureAwait(false);

        var typeById = voucherTypes.ToDictionary(type => type.Id);
        var ledgerNameById = ledgers.ToDictionary(ledger => ledger.Id, ledger => ledger.Name);
        var costCentreNameById = costCentres.ToDictionary(centre => centre.Id, centre => centre.Name);
        var itemById = items.ToDictionary(item => item.Id);
        var unitCodeById = units.ToDictionary(unit => unit.Id, unit => unit.Code);
        var godownNameById = godowns.ToDictionary(godown => godown.Id, godown => godown.Name);

        // The stock side of a voucher lives on its own aggregate, linked back by PostedVoucherId
        // (Phase 2). Indexing it once here keeps the voucher loop from running a query per row, which
        // on a full history is the difference between an export and an afternoon.
        var stockByVoucherId = stockVouchers
            .Where(voucher => voucher.PostedVoucherId is not null)
            .GroupBy(voucher => voucher.PostedVoucherId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new List<InteropVoucher>(revisions.Count);

        foreach (var revision in revisions
            .OrderBy(revision => revision.VoucherDate)
            .ThenBy(revision => revision.NumberNormalised, StringComparer.Ordinal))
        {
            if (!typeById.TryGetValue(revision.VoucherTypeId, out var voucherType))
            {
                continue;
            }

            result.Add(new InteropVoucher
            {
                Id = revision.VoucherId,
                VoucherTypeName = voucherType.Name,
                Kind = voucherType.Kind,
                Date = revision.VoucherDate,
                Number = revision.Number,
                Narration = revision.Narration,
                ReferenceNumber = revision.ReferenceNumber,
                ReferenceDate = revision.ReferenceDate,
                IsCancelled = revision.Status == VoucherStatus.Cancelled,
                LedgerEntries =
                [
                    .. revision.Lines
                        .OrderBy(line => line.LineNumber)
                        .Select(line => new InteropLedgerEntry(
                            ledgerNameById.TryGetValue(line.LedgerId, out var name) ? name : string.Empty,
                            line.AmountMinor,
                            line.CostCentreId is { } centreId
                                && costCentreNameById.TryGetValue(centreId, out var centreName)
                                ? centreName
                                : null,
                            line.Narration)),
                ],
                InventoryEntries = InventoryEntriesFor(
                    revision.VoucherId,
                    stockByVoucherId,
                    itemById,
                    unitCodeById,
                    godownNameById),
            });
        }

        return result;
    }

    private static IReadOnlyList<InteropInventoryEntry> InventoryEntriesFor(
        Guid voucherId,
        Dictionary<Guid, List<StockVoucher>> stockByVoucherId,
        Dictionary<Guid, Item> itemById,
        Dictionary<Guid, string> unitCodeById,
        Dictionary<Guid, string> godownNameById)
    {
        if (!stockByVoucherId.TryGetValue(voucherId, out var stockVouchers))
        {
            return [];
        }

        return
        [
            .. stockVouchers
                .SelectMany(stockVoucher => stockVoucher.Lines.OrderBy(line => line.LineNumber))
                .Select(line =>
                {
                    // Inward is positive, outward negative, so the sign on the wire says which way
                    // the stock moved without the reader having to know the voucher's purpose.
                    var signedQuantity = line.Direction == StockMovementDirection.Increase
                        ? line.QuantityMinor
                        : -line.QuantityMinor;

                    var item = itemById.TryGetValue(line.ItemId, out var found) ? found : null;

                    return new InteropInventoryEntry(
                        item?.Name ?? string.Empty,
                        signedQuantity,
                        AmountOf(signedQuantity, line.UnitCostMinor),
                        godownNameById.TryGetValue(line.GodownId, out var godownName) ? godownName : null,
                        item is not null && unitCodeById.TryGetValue(item.BaseUnitId, out var unitCode)
                            ? unitCode
                            : null,
                        line.UnitCostMinor,
                        line.BatchNumber);
                }),
        ];
    }

    /// <summary>The value a movement carries, computed the way the stock ledger computes it.</summary>
    /// <remarks>
    /// Deliberately the stock ledger's own function rather than the same arithmetic written again.
    /// C8.6 compares what an import moved the books by against what the file said it carried, and
    /// that comparison is only meaningful if both sides round identically — two independent
    /// implementations agreeing to the rupee and differing in the fourth decimal would fail the
    /// checkpoint for a reason that has nothing to do with the data.
    /// </remarks>
    private static long AmountOf(long quantityMinor, long unitCostMinor) =>
        StockLedgerEntry.ComputeValueMinor(MoneyScale.FromMinor(quantityMinor), unitCostMinor);

    private Task<IReadOnlyList<TEntity>> ListAsync<TEntity>(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken)
        where TEntity : Domain.Common.Entity =>
        _unitOfWork.Repository<TEntity>().ListAsync(specification, cancellationToken);

    private sealed record MasterSet(
        IReadOnlyList<InteropAccountGroup> AccountGroups,
        IReadOnlyList<InteropLedger> Ledgers,
        IReadOnlyList<InteropUnit> Units,
        IReadOnlyList<InteropStockGroup> StockGroups,
        IReadOnlyList<InteropGodown> Godowns,
        IReadOnlyList<InteropItem> Items)
    {
        public static MasterSet Empty { get; } = new([], [], [], [], [], []);
    }

    /// <summary>
    /// A read-only query over one entity type, described at the call site.
    /// </summary>
    /// <remarks>
    /// The export touches seven master types and would otherwise need seven near-identical
    /// specification classes saying "belongs to this company". The alternative — a marker interface
    /// on every company-scoped entity — is a change across Phases 1 to 7 to serve one reader in
    /// Phase 8, which is more than this needs.
    /// </remarks>
    private sealed class Filtered<TEntity> : Specification<TEntity>
        where TEntity : Domain.Common.Entity
    {
        public Filtered(Expression<Func<TEntity, bool>> criteria)
        {
            Where(criteria);
            ApplyNoTracking();
        }
    }

    private sealed class CurrentRevisionsInPeriod : Specification<VoucherRevision>
    {
        public CurrentRevisionsInPeriod(Guid companyId, DateOnly? from, DateOnly? to)
        {
            Where(revision => revision.CompanyId == companyId
                && revision.SupersededByRevisionId == null
                && (from == null || revision.VoucherDate >= from)
                && (to == null || revision.VoucherDate <= to));

            AddInclude(revision => revision.Lines);
            ApplyNoTracking();
        }
    }

    private sealed class PostedStockVouchersOf : Specification<StockVoucher>
    {
        public PostedStockVouchersOf(Guid companyId)
        {
            Where(voucher => voucher.CompanyId == companyId && voucher.PostedVoucherId != null);
            AddInclude(voucher => voucher.Lines);
            ApplyNoTracking();
        }
    }
}
