namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// A voucher number that has been claimed, together with what it was claimed from.
/// </summary>
/// <remarks>
/// The series and financial year travel with the number because the caller needs all three to write
/// the <c>Voucher</c> and its revision, and re-deriving them would be a second chance to derive them
/// differently. <see cref="SequenceNumber"/> is the numeric part alone, which is what the §8.7 gap
/// report reasons about.
/// </remarks>
/// <param name="SeriesId">The series the number came from.</param>
/// <param name="FinancialYearId">The financial year the counter is scoped to (§7.2).</param>
/// <param name="Number">The formatted number as it appears on the voucher, e.g. <c>PMT/0001</c>.</param>
/// <param name="SequenceNumber">The numeric part, before prefix and padding.</param>
/// <param name="IsManual">
/// Whether an operator supplied the number instead of the series allocating it. Recorded on the
/// revision and in the edit log, so an override is visible afterwards rather than inferred from a
/// gap.
/// </param>
public sealed record AllocatedVoucherNumber(
    Guid SeriesId,
    Guid FinancialYearId,
    string Number,
    long SequenceNumber,
    bool IsManual);
