namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// Manages GST rate schedules and resolves the rate effective on a business date
/// (phase-5-gst-returns.md §2, checkpoint C5.5).
/// </summary>
public interface IGstRateService
{
    Task<IReadOnlyList<GstRateScheduleSummary>> ListAsync(
        Guid? hsnSacId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new dated row. Any open-ended row for the same code is closed the day before the new
    /// row begins, so exactly one schedule ever covers a given date.
    /// </summary>
    Task<GstRateScheduleSummary> AddAsync(
        SaveGstRateScheduleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The schedule governing <paramref name="hsnSacId"/> on <paramref name="onDate"/>, or a
    /// zero-rate result when none is configured (the invoice still posts, with a warning).
    /// </summary>
    Task<ResolvedGstRate> ResolveAsync(
        Guid hsnSacId,
        DateOnly onDate,
        CancellationToken cancellationToken = default);
}
