using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// One Professional Tax slab: a wage band and the flat monthly tax charged inside it
/// (phase-7-tds-payroll-banking.md §2, §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// <b>A slab, not a rate, because the statute is written that way.</b> Professional Tax is a state
/// levy — Karnataka's schedule differs from Maharashtra's, and both are amended — charged as a flat
/// rupee amount per band rather than as a percentage. Modelling it as a rate would mean deriving
/// percentages that do not exist in the notification, and the derived figure would be wrong at
/// every wage but one.
/// </para>
/// <para>
/// <b>The upper bound is exclusive and nullable.</b> Exclusive so that two adjacent bands written
/// from a notification — "15,000 to 25,000" then "25,000 and above" — cannot both claim ₹25,000,
/// which is the ambiguity that makes a slab table charge twice or not at all. Nullable because the
/// top band genuinely has no upper limit, and a sentinel like <c>long.MaxValue</c> would be a
/// number somebody eventually compares against.
/// </para>
/// </remarks>
public sealed class ProfessionalTaxSlab : Entity, IAuditStamped
{
    private ProfessionalTaxSlab()
    {
        // EF Core materialisation.
    }

    public ProfessionalTaxSlab(
        Guid companyId,
        long wageFromMinor,
        long? wageToMinor,
        long taxAmountMinor,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(wageFromMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(taxAmountMinor);

        if (wageToMinor is { } upper && upper <= wageFromMinor)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wageToMinor),
                wageToMinor,
                "A slab's upper bound must be above its lower bound.");
        }

        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveTo), effectiveTo, "A slab cannot end before it begins.");
        }

        CompanyId = companyId;
        WageFromMinor = wageFromMinor;
        WageToMinor = wageToMinor;
        TaxAmountMinor = taxAmountMinor;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Lowest wage in this band, inclusive.</summary>
    public long WageFromMinor { get; private set; }

    /// <summary>Upper bound of the band, exclusive; null on the open top band.</summary>
    public long? WageToMinor { get; private set; }

    /// <summary>The flat monthly tax charged inside the band, in minor units.</summary>
    public long TaxAmountMinor { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>Whether this slab governs a payroll run for the given date.</summary>
    public bool Covers(DateOnly date) =>
        date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    /// <summary>Whether a wage falls in this band — lower bound inclusive, upper exclusive.</summary>
    public bool Contains(long wageMinor) =>
        wageMinor >= WageFromMinor && (!WageToMinor.HasValue || wageMinor < WageToMinor.Value);

    /// <summary>Ends this slab so a re-notified table takes over from the next day.</summary>
    public void CloseAsOf(DateOnly lastEffectiveDate)
    {
        if (lastEffectiveDate < EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastEffectiveDate),
                lastEffectiveDate,
                "A slab cannot close before it begins.");
        }

        EffectiveTo = lastEffectiveDate;
    }
}
