using System.Globalization;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>
/// Inventory reports off the unified stock ledger through the reporting seam
/// (phase-2-inventory.md §1, checkpoints C2.1, C2.6).
/// </summary>
public sealed class StockReportService(
    ISqlQueryExecutor queryExecutor,
    ICompanyContext companyContext,
    IUnitOfWork unitOfWork) : IStockReportService
{
    /// <summary>Aging bucket boundaries in days: ≤30, ≤90, ≤180, older.</summary>
    private static readonly (int From, int To)[] AgingBuckets = [(0, 30), (31, 90), (91, 180), (181, int.MaxValue)];

    private readonly ISqlQueryExecutor _queryExecutor = queryExecutor
        ?? throw new ArgumentNullException(nameof(queryExecutor));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockOnHandRow>> GetOnHandAsync(
        DateOnly asOfDate,
        Guid? godownId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<OnHandSqlRow>(
            InventoryQueryKeys.StockOnHand,
            new { CompanyId = companyId, AsOfDate = CalendarText.For(asOfDate), GodownId = godownId },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new StockOnHandRow(
                ToGuid(row.ItemId),
                row.ItemName,
                row.ItemCode,
                ToGuid(row.GodownId),
                row.GodownName,
                QtyFromMinor(row.QuantityInMinor),
                QtyFromMinor(row.QuantityOutMinor),
                QtyFromMinor(row.OnHandMinor),
                row.ValueMinor))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<StockValuationReconciliation> ReconcileValuationAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var itemRows = await _queryExecutor.QueryAsync<ValuationSqlRow>(
            InventoryQueryKeys.StockValuation,
            new { CompanyId = companyId, AsOfDate = CalendarText.For(asOfDate) },
            cancellationToken).ConfigureAwait(false);

        var controlLedger = await RequireControlLedgerAsync(companyId, cancellationToken).ConfigureAwait(false);

        var balanceRow = await _queryExecutor.QuerySingleOrDefaultAsync<BalanceSqlRow>(
            InventoryQueryKeys.ControlAccountBalance,
            new
            {
                CompanyId = companyId,
                AsOfDate = CalendarText.For(asOfDate),
                ControlLedgerName = controlLedger.NameNormalised,
            },
            cancellationToken).ConfigureAwait(false);

        var items = itemRows
            .Select(row => new StockValuationRow(
                ToGuid(row.ItemId),
                row.ItemName,
                QtyFromMinor(row.OnHandMinor),
                row.ValueMinor))
            .ToList();

        var stockValue = items.Sum(row => row.ValueMinor);

        return new StockValuationReconciliation(
            items,
            stockValue,
            controlLedger.Id,
            controlLedger.Name,
            balanceRow?.BalanceMinor ?? 0L);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReorderAlertRow>> GetReorderAlertsAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<ReorderSqlRow>(
            InventoryQueryKeys.ReorderAlerts,
            new { CompanyId = companyId, AsOfDate = CalendarText.For(asOfDate) },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new ReorderAlertRow(
                ToGuid(row.ItemId),
                row.ItemName,
                row.ItemCode,
                QtyFromMinor(row.OnHandMinor),
                QtyFromMinor(row.ReorderLevelMinor)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockAgingRow>> GetAgingBucketsAsync(
        DateOnly asOfDate,
        Guid? godownId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<AgingLayerSqlRow>(
            InventoryQueryKeys.StockAgingLayers,
            new { CompanyId = companyId, AsOfDate = CalendarText.For(asOfDate), GodownId = godownId },
            cancellationToken).ConfigureAwait(false);

        // Replay strictly in posting order: each issue consumes the oldest layers that existed when
        // it was posted, exactly as FIFO valuation does, so what survives is genuinely on the shelf.
        var layers = new List<AgingLayer>();

        foreach (var row in rows.OrderBy(entry => entry.GlobalSequence))
        {
            var itemId = ToGuid(row.ItemId);

            if (row.Direction == "Increase")
            {
                layers.Add(new AgingLayer
                {
                    ItemId = itemId,
                    ReceivedOn = DateOnly.ParseExact(row.EntryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Quantity = QtyFromMinor(row.QuantityMinor),
                    UnitCostMinor = row.UnitCostMinor,
                });
                continue;
            }

            var remaining = QtyFromMinor(row.QuantityMinor);

            for (var index = 0; index < layers.Count && remaining > 0m; index++)
            {
                var layer = layers[index];

                if (layer.ItemId != itemId || layer.Quantity <= 0m)
                {
                    continue;
                }

                var taken = Math.Min(layer.Quantity, remaining);
                layer.Quantity -= taken;
                remaining -= taken;
            }
        }

        var itemNames = (await _unitOfWork.Repository<Domain.Inventory.Item>()
                .ListAsync(new CompanyItems(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id, item => item.Name);

        return layers
            .Where(layer => layer.Quantity > 0m)
            .Select(layer =>
            {
                var ageDays = asOfDate.DayNumber - layer.ReceivedOn.DayNumber;
                var bucket = AgingBuckets.First(range => ageDays >= range.From && ageDays <= range.To);

                return new StockAgingRow(
                    layer.ItemId,
                    itemNames.GetValueOrDefault(layer.ItemId, "?"),
                    null,
                    bucket.From,
                    bucket.To,
                    layer.Quantity,
                    StockLedgerEntry.ComputeValueMinor(layer.Quantity, layer.UnitCostMinor));
            })
            .ToList();
    }

    /// <summary>A receipt-side FIFO layer still (partly) on the shelf after the replay.</summary>
    private sealed class AgingLayer
    {
        public Guid ItemId { get; init; }

        public DateOnly ReceivedOn { get; init; }

        public decimal Quantity { get; set; }

        public long UnitCostMinor { get; init; }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemLookupRow>> SearchItemsAsync(
        string? searchText = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<ItemLookupSqlRow>(
            InventoryQueryKeys.ItemSearch,
            new { CompanyId = companyId, Search = searchText ?? string.Empty, Take = take },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new ItemLookupRow(
                ToGuid(row.Id),
                row.Name,
                row.Code,
                row.Barcode,
                Convert.ToBoolean(row.IsSerialTracked, CultureInfo.InvariantCulture),
                Convert.ToBoolean(row.IsBatchTracked, CultureInfo.InvariantCulture),
                Convert.ToBoolean(row.IsKit, CultureInfo.InvariantCulture),
                row.BaseUnitCode))
            .ToList();
    }

    private async Task<string> ResolveItemNameAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Repository<Domain.Inventory.Item>()
            .GetByIdAsync(itemId, cancellationToken)
            .ConfigureAwait(false);

        return item?.Name ?? "?";
    }

    private sealed class CompanyItems : Domain.Abstractions.Specification<Domain.Inventory.Item>
    {
        public CompanyItems(Guid companyId)
        {
            Where(item => item.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private async Task<Domain.Accounting.Ledger> RequireControlLedgerAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(StockVoucherService.InventoryControlLedgerName);

        var ledger = await _unitOfWork.Repository<Domain.Accounting.Ledger>()
            .SingleOrDefaultAsync(new ControlLedgerByName(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null)
        {
            throw new InvalidOperationException(
                $"The '{StockVoucherService.InventoryControlLedgerName}' control account is missing, "
                + "so stock value cannot be reconciled.");
        }

        return ledger;
    }

    private static Guid ToGuid(string text) => Guid.Parse(text);

    /// <summary>
    /// Quantities travel as integer minor units of 1/10,000 — the same scaled-integer discipline as
    /// money (ADR-0003) — and are converted to decimals exactly once, here.
    /// </summary>
    private static decimal QtyFromMinor(long minor) =>
        minor / 10_000m;

    private sealed class ControlLedgerByName : Domain.Abstractions.Specification<Domain.Accounting.Ledger>
    {
        public ControlLedgerByName(Guid companyId, string nameNormalised)
        {
            Where(ledger => ledger.CompanyId == companyId && ledger.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    internal sealed record OnHandSqlRow(
        string ItemId,
        string ItemName,
        string? ItemCode,
        string GodownId,
        string GodownName,
        long QuantityInMinor,
        long QuantityOutMinor,
        long OnHandMinor,
        long ValueMinor);

    /// <summary>
    /// The valuation read model. A settable class, not a positional record, and deliberately so.
    /// </summary>
    /// <remarks>
    /// Dapper builds its deserialiser from the reader's schema before it knows whether any rows came
    /// back. On an empty result SQLite can report no type for an aggregate expression, and
    /// Microsoft.Data.Sqlite falls back to <c>byte[]</c> — so a positional record is asked for a
    /// constructor taking <c>byte[]</c>, finds none, and throws. Every other report in this suite
    /// returns a row per master and so never hits it; this one groups over the stock ledger, which is
    /// empty on a company that has not moved any stock yet. Mapping by property instead defers the
    /// conversion to a row that actually exists.
    /// <para>
    /// Found by the Phase 8 importer, which reads this to reconcile an import (C8.6) and therefore
    /// reads it on a fresh company. It would otherwise have surfaced as the Stock Valuation screen
    /// throwing on a new install.
    /// </para>
    /// </remarks>
    internal sealed class ValuationSqlRow
    {
        public string ItemId { get; init; } = string.Empty;

        public string ItemName { get; init; } = string.Empty;

        public long OnHandMinor { get; init; }

        public long ValueMinor { get; init; }
    }

    internal sealed record ReorderSqlRow(
        string ItemId,
        string ItemName,
        string? ItemCode,
        long OnHandMinor,
        long ReorderLevelMinor);

    internal sealed record LedgerBalanceSqlRow(string LedgerId, long ClosingMinor);

    internal sealed record BalanceSqlRow(long BalanceMinor);

    internal sealed record AgingLayerSqlRow(
        long GlobalSequence,
        string ItemId,
        string GodownId,
        string? BatchNumber,
        string Direction,
        string EntryDate,
        long QuantityMinor,
        long UnitCostMinor);

    internal sealed record ItemLookupSqlRow(
        string Id,
        string Name,
        string? Code,
        string? Barcode,
        long IsSerialTracked,
        long IsBatchTracked,
        long IsKit,
        string BaseUnitCode);
}
