using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>
/// Inventory reports read through the reporting seam: on-hand, the C2.1 valuation reconciliation,
/// reorder alerts and stock aging (phase-2-inventory.md §1).
/// </summary>
/// <remarks>
/// Every figure is a <c>SUM</c> over the unified stock ledger's integer minor units (ADR-0003),
/// converted to decimals exactly once in the mappers. Aging replays receipt layers against
/// consumption so a bucket holds only what is genuinely still on the shelf.
/// </remarks>
public interface IStockReportService
{
    /// <summary>On-hand quantity and value per item per godown as of a date.</summary>
    Task<IReadOnlyList<StockOnHandRow>> GetOnHandAsync(
        DateOnly asOfDate,
        Guid? godownId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The checkpoint C2.1 reconciliation: total stock value from the ledger against the balance of
    /// the inventory control account at the same date.
    /// </summary>
    Task<StockValuationReconciliation> ReconcileValuationAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default);

    /// <summary>Items whose on-hand has fallen to or below their reorder level.</summary>
    Task<IReadOnlyList<ReorderAlertRow>> GetReorderAlertsAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default);

    /// <summary>Remaining stock bucketed by how long ago it was received.</summary>
    Task<IReadOnlyList<StockAgingRow>> GetAgingBucketsAsync(
        DateOnly asOfDate,
        Guid? godownId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The item lookup entry screens bill against: name / code / barcode over the whole catalogue,
    /// resolved in SQL so a five-thousand-SKU catalogue answers within the UI budget (C2.6).
    /// </summary>
    Task<IReadOnlyList<ItemLookupRow>> SearchItemsAsync(
        string? searchText = null,
        int take = 50,
        CancellationToken cancellationToken = default);
}
