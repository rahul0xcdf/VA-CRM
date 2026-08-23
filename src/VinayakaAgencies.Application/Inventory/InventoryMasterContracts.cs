using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>Read model for a stock group.</summary>
public sealed record StockGroupSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive);

public sealed record CreateStockGroupRequest(string Name, string? Description = null);

public sealed record UpdateStockGroupRequest(Guid Id, string Name, string? Description);

/// <summary>Read model for a godown.</summary>
public sealed record GodownSummary(
    Guid Id,
    string Name,
    string? Address,
    bool IsSystem,
    bool IsActive);

public sealed record CreateGodownRequest(string Name, string? Address = null);

public sealed record UpdateGodownRequest(Guid Id, string Name, string? Address);

/// <summary>Read model for a unit of measure.</summary>
public sealed record UnitOfMeasureSummary(
    Guid Id,
    string Code,
    int DecimalPlaces,
    string? Description,
    bool IsActive);

public sealed record CreateUnitRequest(string Code, int DecimalPlaces = 0, string? Description = null);

/// <summary>
/// Read model for an item. Carries display names resolved server-side so screens never join.
/// </summary>
public sealed record ItemSummary(
    Guid Id,
    string Name,
    string? Code,
    string? Barcode,
    Guid? StockGroupId,
    string? StockGroupName,
    Guid BaseUnitId,
    string BaseUnitCode,
    Guid? AlternateUnitId,
    string? AlternateUnitCode,
    decimal? ConversionFactor,
    bool IsSerialTracked,
    bool IsBatchTracked,
    ValuationMethod? ValuationOverride,
    decimal ReorderLevel,
    bool IsKit,
    bool IsActive);

public sealed record SaveItemRequest(
    Guid? Id,
    string Name,
    Guid BaseUnitId,
    Guid? StockGroupId = null,
    string? Code = null,
    string? Barcode = null,
    Guid? AlternateUnitId = null,
    decimal? ConversionFactor = null,
    bool IsSerialTracked = false,
    bool IsBatchTracked = false,
    ValuationMethod? ValuationOverride = null,
    decimal ReorderLevel = 0m,
    bool IsKit = false);

/// <summary>One kit component row on the item screen.</summary>
public sealed record KitComponentLine(
    Guid ComponentItemId,
    string ComponentName,
    decimal Quantity);
