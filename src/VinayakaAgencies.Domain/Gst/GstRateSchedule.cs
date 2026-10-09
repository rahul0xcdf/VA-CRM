using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Gst;

/// <summary>
/// One GST rate row for an HSN/SAC code, valid from <see cref="EffectiveFrom"/> until
/// <see cref="EffectiveTo"/> (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Rates change by notification, so a rate is a dated data row and never a constant: computation
/// resolves the schedule effective on the document's business date (checkpoint C5.5) — a change
/// effective 1 July applies to invoices dated on or after 1 July and to nothing earlier. The
/// current rate is simply the open-ended row (<see cref="EffectiveTo"/> null). Closing a schedule
/// when a new one begins keeps history intact; rows are never edited in place.
/// </remarks>
public sealed class GstRateSchedule : Entity, IAuditStamped
{
    private GstRateSchedule()
    {
        // EF Core materialisation.
    }

    public GstRateSchedule(
        Guid companyId,
        Guid hsnSacId,
        long ratePercentMinor,
        DateOnly effectiveFrom,
        long compensationCessPercentMinor = 0L,
        DateOnly? effectiveTo = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (hsnSacId == Guid.Empty)
        {
            throw new ArgumentException(
                "A rate schedule must name its HSN/SAC master row.", nameof(hsnSacId));
        }

        if (ratePercentMinor < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ratePercentMinor),
                ratePercentMinor,
                "A GST rate cannot be negative.");
        }

        if (compensationCessPercentMinor < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compensationCessPercentMinor),
                compensationCessPercentMinor,
                "A compensation cess cannot be negative.");
        }

        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveTo),
                effectiveTo,
                "A rate schedule cannot end before it begins.");
        }

        CompanyId = companyId;
        HsnSacId = hsnSacId;
        RatePercentMinor = ratePercentMinor;
        CompensationCessPercentMinor = compensationCessPercentMinor;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public Guid CompanyId { get; private set; }

    public Guid HsnSacId { get; private set; }

    /// <summary>
    /// The GST rate as a percentage, in minor units of 1/10,000 of a percent (ADR-0003):
    /// 18% is 180_000.
    /// </summary>
    public long RatePercentMinor { get; private set; }

    /// <summary>The compensation cess percentage, same scale — zero where none applies.</summary>
    public long CompensationCessPercentMinor { get; private set; }

    /// <summary>First business date the rate applies to.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Last business date the rate applies to; null means still current.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>Whether this schedule governs supplies made on the given business date.</summary>
    public bool Covers(DateOnly date) =>
        date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    /// <summary>
    /// Ends this schedule at the last day it applies, so a newer row can take over from the next
    /// date. History is closed, never rewritten.
    /// </summary>
    public void CloseAsOf(DateOnly lastEffectiveDate)
    {
        if (lastEffectiveDate < EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastEffectiveDate),
                lastEffectiveDate,
                "A rate schedule cannot close before it begins.");
        }

        EffectiveTo = lastEffectiveDate;
    }
}
