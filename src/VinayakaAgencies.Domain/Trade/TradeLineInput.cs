namespace VinayakaAgencies.Domain.Trade;

/// <summary>
/// One replacement line handed to <see cref="TradeDocument.ResetLines"/>: the shape the editing
/// service carries between the old line rows it removes and the new ones it adds.
/// </summary>
public sealed record TradeLineInput(
    Guid ItemId,
    Guid? GodownId,
    decimal Quantity,
    decimal Rate,
    decimal DiscountPercent,
    bool IsSerialTracked,
    string? BatchNumber = null,
    IReadOnlyList<string>? SerialNumbers = null);
