using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>One line of a stock voucher being posted.</summary>
public sealed record StockVoucherLineRequest(
    StockMovementDirection Direction,
    Guid ItemId,
    Guid GodownId,
    decimal Quantity,
    decimal UnitCost = 0m,
    string? BatchNumber = null,
    IReadOnlyList<string>? SerialNumbers = null);

public sealed record PostStockVoucherRequest(
    StockVoucherPurpose Purpose,
    DateOnly VoucherDate,
    IReadOnlyList<StockVoucherLineRequest> Lines,
    string Narration,
    Guid? CounterLedgerId = null,
    bool ConfirmNegativeStockWarnings = false);

/// <summary>
/// The outcome of validating a proposed stock voucher without posting it. Errors block posting;
/// warnings are the negative-stock policy (<c>open-questions.md</c> #20) surfaced for confirmation.
/// </summary>
public sealed record StockValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static readonly StockValidationResult Clean = new([], []);

    public bool HasErrors => Errors.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>A posted stock voucher shown back on the entry screen and in the movement list.</summary>
public sealed record PostedStockVoucherSummary(
    Guid Id,
    string Number,
    StockVoucherPurpose Purpose,
    DateOnly VoucherDate,
    Guid? PostedVoucherId,
    Guid? CounterLedgerId,
    string? Narration,
    long TotalValueMinor,
    IReadOnlyList<PostedStockVoucherLineSummary> Lines);

public sealed record PostedStockVoucherLineSummary(
    int LineNumber,
    StockMovementDirection Direction,
    Guid ItemId,
    string ItemName,
    Guid GodownId,
    string GodownName,
    decimal Quantity,
    decimal UnitCost,
    string? BatchNumber);
