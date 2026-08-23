namespace VinayakaAgencies.Application.Inventory;

/// <summary>On-hand quantity and value for one item in one godown.</summary>
public sealed record StockOnHandRow(
    Guid ItemId,
    string ItemName,
    string? ItemCode,
    Guid GodownId,
    string GodownName,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal OnHand,
    long ValueMinor);

/// <summary>The C2.1 reconciliation: stock value against the inventory control account.</summary>
public sealed record StockValuationRow(
    Guid ItemId,
    string ItemName,
    decimal OnHand,
    long ValueMinor);

public sealed record StockValuationReconciliation(
    IReadOnlyList<StockValuationRow> Items,
    long TotalStockValueMinor,
    Guid ControlLedgerId,
    string ControlLedgerName,
    long ControlAccountBalanceMinor);

/// <summary>An item whose on-hand has fallen to or below its reorder level.</summary>
public sealed record ReorderAlertRow(
    Guid ItemId,
    string ItemName,
    string? ItemCode,
    decimal OnHand,
    decimal ReorderLevel);

/// <summary>Quantity still in stock, bucketed by how long ago it was received.</summary>
public sealed record StockAgingRow(
    Guid ItemId,
    string ItemName,
    string? BatchNumber,
    int BucketDaysFrom,
    int BucketDaysTo,
    decimal Quantity,
    long ValueMinor);

/// <summary>A row of the item lookup used by entry screens (checkpoint C2.6).</summary>
public sealed record ItemLookupRow(
    Guid Id,
    string Name,
    string? Code,
    string? Barcode,
    bool IsSerialTracked,
    bool IsBatchTracked,
    bool IsKit,
    string BaseUnitCode);
