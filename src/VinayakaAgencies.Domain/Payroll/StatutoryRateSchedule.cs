using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// One dated contribution-rate row for a wage-based statute — provident fund or state insurance
/// (phase-7-tds-payroll-banking.md §2, §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every figure here is notified, therefore every figure here is data.</b> §2 extends
/// non-negotiable #6 — which already covers GST, TDS and TCS rates — to PF and ESI contribution
/// rates and their wage ceilings by name. A contribution rate written as a constant is a constant
/// somebody has to find and edit under deadline the next time it moves, and it has moved.
/// </para>
/// <para>
/// <b>The ceiling is a wage cap, not a cut-off.</b> PF is contributed on wages up to the ceiling
/// and on nothing above it — an employee on ₹40,000 with a ₹15,000 ceiling contributes on ₹15,000,
/// not nothing. ESI works the opposite way: above its threshold an employee is out of the scheme
/// entirely. The two are different rules, so they are two columns rather than one reused field:
/// <see cref="WageCeilingMinor"/> caps the base, and <see cref="EligibilityCeilingMinor"/> excludes
/// the employee outright. A statute uses one or the other, and a null means "no such rule".
/// </para>
/// <para>
/// Professional Tax is deliberately absent: it is charged in slabs, not at a rate, and forcing it
/// into a rate row would mean carrying a fourth interpretation of these same columns. It has
/// <see cref="ProfessionalTaxSlab"/> instead.
/// </para>
/// </remarks>
public sealed class StatutoryRateSchedule : Entity, IAuditStamped
{
    private StatutoryRateSchedule()
    {
        // EF Core materialisation.
    }

    public StatutoryRateSchedule(
        Guid companyId,
        StatuteKind statute,
        long employeeRatePercentMinor,
        long employerRatePercentMinor,
        DateOnly effectiveFrom,
        long? wageCeilingMinor = null,
        long? eligibilityCeilingMinor = null,
        DateOnly? effectiveTo = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (statute == StatuteKind.ProfessionalTax)
        {
            throw new ArgumentException(
                "Professional Tax is charged in slabs, not at a contribution rate. Configure it "
                + "through professional tax slab rows instead.",
                nameof(statute));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(employeeRatePercentMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(employerRatePercentMinor);

        if (wageCeilingMinor is < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wageCeilingMinor), wageCeilingMinor, "A wage ceiling cannot be negative.");
        }

        if (eligibilityCeilingMinor is < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eligibilityCeilingMinor),
                eligibilityCeilingMinor,
                "An eligibility ceiling cannot be negative.");
        }

        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveTo), effectiveTo, "A rate schedule cannot end before it begins.");
        }

        CompanyId = companyId;
        Statute = statute;
        EmployeeRatePercentMinor = employeeRatePercentMinor;
        EmployerRatePercentMinor = employerRatePercentMinor;
        WageCeilingMinor = wageCeilingMinor;
        EligibilityCeilingMinor = eligibilityCeilingMinor;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public Guid CompanyId { get; private set; }

    public StatuteKind Statute { get; private set; }

    /// <summary>The employee's share, in minor units of 1/10,000 of a percent: 12% is 120_000.</summary>
    public long EmployeeRatePercentMinor { get; private set; }

    /// <summary>The employer's share, same scale. Posted as an expense, never withheld.</summary>
    public long EmployerRatePercentMinor { get; private set; }

    /// <summary>
    /// Wages above this do not attract contribution, but the employee stays in the scheme and
    /// contributes on the capped amount. Null where the statute caps nothing.
    /// </summary>
    public long? WageCeilingMinor { get; private set; }

    /// <summary>
    /// Above this wage the employee is outside the scheme altogether and contributes nothing.
    /// Null where the statute excludes nobody.
    /// </summary>
    public long? EligibilityCeilingMinor { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>Whether this row governs a payroll run for the given date.</summary>
    public bool Covers(DateOnly date) =>
        date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    /// <summary>Ends this row so a newly notified one takes over from the next day.</summary>
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
