using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>
/// Names of the inventory reporting queries, resolved to provider SQL by the dialect layer
/// (<see cref="ISqlQueryExecutor"/>). Callers never hold a SQL string — ADR-0001.
/// </summary>
public static class InventoryQueryKeys
{
    /// <summary>On-hand and value per item per godown, optionally filtered to one godown.</summary>
    public static readonly SqlQueryKey StockOnHand = new("Inventory.StockOnHand");

    /// <summary>Total stock value per item as of a date — the C2.1 reconciliation's stock side.</summary>
    public static readonly SqlQueryKey StockValuation = new("Inventory.StockValuation");

    /// <summary>Items at or below reorder level with their current on-hand.</summary>
    public static readonly SqlQueryKey ReorderAlerts = new("Inventory.ReorderAlerts");

    /// <summary>Remaining receipt-side layers (item, godown, batch, date, qty, cost) for aging.</summary>
    public static readonly SqlQueryKey StockAgingLayers = new("Inventory.StockAgingLayers");

    /// <summary>Item lookup by name / code / barcode over the full catalogue (C2.6).</summary>
    public static readonly SqlQueryKey ItemSearch = new("Inventory.ItemSearch");

    /// <summary>
    /// Closing balance of one named ledger up to a date — used by the C2.1 reconciliation to read
    /// the inventory control account without redefining "closing balance".
    /// </summary>
    public static readonly SqlQueryKey ControlAccountBalance = new("Inventory.ControlAccountBalance");
}
