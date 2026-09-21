using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// The contribution a wage-based statute produces for one employee in one month.
/// </summary>
/// <param name="ContributoryWageMinor">The wage actually contributed on, after any ceiling.</param>
/// <param name="EmployeeShareMinor">Withheld from the employee.</param>
/// <param name="EmployerShareMinor">Paid by the company on top of gross.</param>
/// <param name="IsExcluded">
/// True where the employee earns above the scheme's eligibility ceiling and is outside it
/// altogether — which is not the same as contributing zero.
/// </param>
public readonly record struct StatutoryContribution(
    long ContributoryWageMinor,
    long EmployeeShareMinor,
    long EmployerShareMinor,
    bool IsExcluded)
{
    /// <summary>True when the scheme produced a figure at all.</summary>
    public bool HasContribution => EmployeeShareMinor > 0L || EmployerShareMinor > 0L;

    /// <summary>The result for an employee the scheme does not cover.</summary>
    public static StatutoryContribution Excluded(long wageMinor) => new(wageMinor, 0L, 0L, true);
}

/// <summary>
/// The payroll arithmetic (phase-7-tds-payroll-banking.md §4a D3, checkpoint C7.7).
/// </summary>
/// <remarks>
/// <para>
/// A pure static class holding no rate, no ceiling and no slab — every figure arrives as an
/// argument read from a dated row. §2 extends non-negotiable #6 to PF and ESI rates, their wage
/// ceilings and PT slabs by name, and this file is what makes that possible: it does not know that
/// provident fund is twelve percent.
/// </para>
/// <para>
/// Separated from the service that loads the rows for the same reason <c>TdsMath</c> and
/// <c>GstTaxMath</c> are: the rule is arithmetic, and arithmetic is worth testing against
/// hand-computed fixtures without a database in the way (ADR-0007).
/// </para>
/// </remarks>
public static class PayrollMath
{
    /// <summary>A percentage in minor units: 100% on the 1/10,000-of-a-percent scale.</summary>
    private const long OneHundredPercentMinor = 100L * 10_000L;

    /// <summary>Minor units in one rupee — the unit a statutory challan is paid in.</summary>
    private const long MinorUnitsPerRupee = MoneyScale.UnitsPerMajor;

    /// <summary>
    /// The employee and employer shares of a wage-based contribution.
    /// </summary>
    /// <remarks>
    /// <b>The two ceilings do opposite things and are applied in order.</b> The eligibility ceiling
    /// is tested first and excludes the employee outright — this is ESI, where earning above the
    /// limit puts you outside the scheme. The wage ceiling is applied second and merely caps the
    /// base — this is PF, where earning above the limit means you contribute on the capped wage
    /// rather than on nothing. Applying them in the other order would let a high earner be capped
    /// into eligibility, which is the inverse of the rule.
    /// </remarks>
    /// <param name="wageMinor">The contributory wage before any ceiling.</param>
    /// <param name="employeeRatePercentMinor">Employee share rate, from the dated row.</param>
    /// <param name="employerRatePercentMinor">Employer share rate, from the dated row.</param>
    /// <param name="wageCeilingMinor">Caps the base; null where the statute caps nothing.</param>
    /// <param name="eligibilityCeilingMinor">Excludes the employee; null where nobody is excluded.</param>
    public static StatutoryContribution Contribution(
        long wageMinor,
        long employeeRatePercentMinor,
        long employerRatePercentMinor,
        long? wageCeilingMinor = null,
        long? eligibilityCeilingMinor = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wageMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(employeeRatePercentMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(employerRatePercentMinor);

        if (eligibilityCeilingMinor is { } eligibility && wageMinor > eligibility)
        {
            return StatutoryContribution.Excluded(wageMinor);
        }

        var contributory = wageCeilingMinor is { } ceiling
            ? Math.Min(wageMinor, ceiling)
            : wageMinor;

        return new StatutoryContribution(
            contributory,
            RoundToRupee(Apply(contributory, employeeRatePercentMinor)),
            RoundToRupee(Apply(contributory, employerRatePercentMinor)),
            IsExcluded: false);
    }

    /// <summary>
    /// The flat Professional Tax for a wage, from the slab table in force.
    /// </summary>
    /// <remarks>
    /// Returns zero where no slab contains the wage, rather than throwing. A PT table usually has
    /// no slab below its first threshold — a low earner simply pays nothing — and treating a
    /// missing bottom band as an error would refuse to pay the people it is most likely to affect.
    /// </remarks>
    public static long ProfessionalTax(long wageMinor, IEnumerable<(long From, long? To, long Tax)> slabs)
    {
        ArgumentNullException.ThrowIfNull(slabs);
        ArgumentOutOfRangeException.ThrowIfNegative(wageMinor);

        foreach (var (from, to, tax) in slabs)
        {
            if (wageMinor >= from && (!to.HasValue || wageMinor < to.Value))
            {
                return tax;
            }
        }

        return 0L;
    }

    /// <summary>
    /// A pay head's amount from its basis and the figures the structure line carries.
    /// </summary>
    /// <remarks>
    /// <see cref="PayComputationBasis.Statutory"/> is absent by design: a statutory head's figure
    /// comes from <see cref="Contribution"/> or <see cref="ProfessionalTax"/>, and routing it
    /// through here would need a rate this method has no business holding. Passing one throws
    /// rather than silently returning zero, because a statutory head quietly worth nothing is the
    /// failure that shows up as an under-remitted challan.
    /// </remarks>
    public static long HeadAmount(
        PayComputationBasis basis,
        long? amountMinor,
        long? percentMinor,
        long basicMinor,
        long grossMinor) => basis switch
        {
            PayComputationBasis.FlatAmount => amountMinor ?? 0L,
            PayComputationBasis.PercentOfBasic => RoundToRupee(Apply(basicMinor, percentMinor ?? 0L)),
            PayComputationBasis.PercentOfGross => RoundToRupee(Apply(grossMinor, percentMinor ?? 0L)),
            PayComputationBasis.Statutory => throw new ArgumentOutOfRangeException(
                nameof(basis),
                basis,
                "A statutory head's figure comes from the statutory engine, not from this method."),
            _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown computation basis."),
        };

    /// <summary>
    /// Rounds to whole rupees.
    /// </summary>
    /// <remarks>
    /// PF, ESI and PT are all remitted in whole rupees, so the figure posted to the books must be
    /// the figure that will be paid — carrying paise into the liability account would leave the
    /// challan and the ledger permanently a few paise apart, exactly as <c>TdsMath</c> notes for
    /// tax deducted at source.
    /// </remarks>
    public static long RoundToRupee(long amountMinor)
    {
        var rupees = decimal.Round(
            (decimal)amountMinor / MinorUnitsPerRupee, 0, MidpointRounding.AwayFromZero);

        return (long)rupees * MinorUnitsPerRupee;
    }

    /// <summary>
    /// A percentage of an amount, before rounding.
    /// </summary>
    /// <remarks>
    /// In <see cref="decimal"/> rather than <see cref="long"/> because both operands are already
    /// scaled by 10,000, so the product is scaled by 10^8 and overflows 64 bits on a large wage
    /// before the divide brings it back.
    /// </remarks>
    private static long Apply(long baseMinor, long percentMinor)
    {
        if (baseMinor <= 0L || percentMinor <= 0L)
        {
            return 0L;
        }

        var scaled = (decimal)baseMinor * percentMinor / OneHundredPercentMinor;

        return (long)decimal.Round(scaled, 0, MidpointRounding.AwayFromZero);
    }
}
