using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Gst;

namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// Manages GST rate schedules as dated data rows (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// A notification changing a rate is one new row: the previous open-ended row closes the day
/// before, and every invoice — past or future — computes against exactly one covering schedule
/// (checkpoint C5.5). Nothing anywhere in the product holds a tax rate constant; this service and
/// its table are the only place rates live.
/// </remarks>
public sealed class GstRateService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : IGstRateService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<GstRateScheduleSummary>> ListAsync(
        Guid? hsnSacId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return (await _unitOfWork.Repository<GstRateSchedule>()
                .ListAsync(new SchedulesOfCompany(companyId, hsnSacId), cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(schedule => schedule.EffectiveFrom)
            .ThenBy(schedule => schedule.HsnSacId)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<GstRateScheduleSummary> AddAsync(
        SaveGstRateScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var code = await RequireCodeAsync(companyId, request.HsnSacId, cancellationToken)
            .ConfigureAwait(false);

        if (!code.IsActive)
        {
            throw new InvalidOperationException(
                $"HSN/SAC code '{code.Code}' is inactive; activate it before attaching rates.");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Close any open-ended row on the same code up to the day before the newcomer begins, so
        // at most one schedule covers any date. Overlapping dated rows are refused outright.
        //
        // The newcomer is open-ended and therefore owns every date from EffectiveFrom onward: a
        // row beginning on or after that date would be swallowed by it, and a closed row still
        // running past it would overlap. An *open* row that began earlier is the ordinary case —
        // a notification superseding the current rate — and is closed the day before.
        //
        // Testing Covers() alone cannot separate these, because an open row covers every later
        // date by definition; doing so rejected the supersession this method exists to perform,
        // which made a GST rate change impossible to record. Found while building the Phase 7
        // TDS equivalent, which was modelled on this method and inherited the same defect.
        foreach (var existing in await _unitOfWork.Repository<GstRateSchedule>()
                     .ListAsync(new SchedulesOfCode(companyId, request.HsnSacId), cancellationToken)
                     .ConfigureAwait(false))
        {
            if (existing.EffectiveFrom >= request.EffectiveFrom
                || (existing.EffectiveTo.HasValue && existing.EffectiveTo.Value >= request.EffectiveFrom))
            {
                throw new InvalidOperationException(
                    "A rate schedule already covers this HSN/SAC code on or after the requested "
                    + "effective date. Rates are added as non-overlapping dated rows, never overlaid.");
            }

            if (!existing.EffectiveTo.HasValue)
            {
                existing.CloseAsOf(request.EffectiveFrom.AddDays(-1));
                _unitOfWork.Repository<GstRateSchedule>().Update(existing);
            }
        }

        var created = new GstRateSchedule(
            companyId,
            request.HsnSacId,
            request.RatePercentMinor,
            request.EffectiveFrom,
            request.CompensationCessPercentMinor,
            effectiveTo: null);

        await _unitOfWork.Repository<GstRateSchedule>()
            .AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(created);
    }

    /// <inheritdoc />
    public async Task<ResolvedGstRate> ResolveAsync(
        Guid hsnSacId,
        DateOnly onDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var schedules = await _unitOfWork.Repository<GstRateSchedule>()
            .ListAsync(new SchedulesOfCode(companyId, hsnSacId), cancellationToken)
            .ConfigureAwait(false);

        var covering = schedules.SingleOrDefault(schedule => schedule.Covers(onDate));

        return covering is null
            ? ResolvedGstRate.None(hsnSacId)
            : new ResolvedGstRate(covering.Id, covering.HsnSacId, covering.RatePercentMinor,
                covering.CompensationCessPercentMinor);
    }

    private async Task<HsnSac> RequireCodeAsync(
        Guid companyId,
        Guid hsnSacId,
        CancellationToken cancellationToken)
    {
        var code = await _unitOfWork.Repository<HsnSac>()
            .GetByIdAsync(hsnSacId, cancellationToken)
            .ConfigureAwait(false);

        return code is null || code.CompanyId != companyId
            ? throw new InvalidOperationException("This HSN/SAC code does not exist in this company's books.")
            : code;
    }

    private static GstRateScheduleSummary ToSummary(GstRateSchedule schedule) =>
        new(schedule.Id,
            schedule.HsnSacId,
            schedule.RatePercentMinor,
            schedule.CompensationCessPercentMinor,
            schedule.EffectiveFrom,
            schedule.EffectiveTo);

    private sealed class SchedulesOfCompany : Specification<GstRateSchedule>
    {
        public SchedulesOfCompany(Guid companyId, Guid? hsnSacId)
        {
            Where(schedule => schedule.CompanyId == companyId
                && (!hsnSacId.HasValue || schedule.HsnSacId == hsnSacId.Value));
            ApplyNoTracking();
        }
    }

    private sealed class SchedulesOfCode : Specification<GstRateSchedule>
    {
        public SchedulesOfCode(Guid companyId, Guid hsnSacId)
        {
            Where(schedule => schedule.CompanyId == companyId && schedule.HsnSacId == hsnSacId);
            ApplyNoTracking();
        }
    }

    private sealed class OpenSchedulesOfCode : Specification<GstRateSchedule>
    {
        public OpenSchedulesOfCode(Guid companyId, Guid hsnSacId)
        {
            Where(schedule => schedule.CompanyId == companyId
                && schedule.HsnSacId == hsnSacId
                && schedule.EffectiveTo == null);
            ApplyNoTracking();
        }
    }
}
