namespace VinayakaAgencies.Application.Gst;

/// <summary>Read model for one GST rate schedule row.</summary>
public sealed record GstRateScheduleSummary(
    Guid Id,
    Guid HsnSacId,
    long RatePercentMinor,
    long CompensationCessPercentMinor,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>
/// Creates a rate row. Existing rows are closed, never edited: rates change by notification and
/// their history is the audit evidence for every return built from it.
/// </summary>
public sealed record SaveGstRateScheduleRequest(
    Guid HsnSacId,
    long RatePercentMinor,
    DateOnly EffectiveFrom,
    long CompensationCessPercentMinor = 0L);

/// <summary>The rate that resolved for a code on a business date; zero-valued when none exists.</summary>
public sealed record ResolvedGstRate(
    Guid ScheduleId,
    Guid HsnSacId,
    long RatePercentMinor,
    long CompensationCessPercentMinor)
{
    public static ResolvedGstRate None(Guid hsnSacId) =>
        new(hsnSacId, hsnSacId, 0L, 0L);
}
