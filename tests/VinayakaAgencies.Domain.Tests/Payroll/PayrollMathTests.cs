using Shouldly;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Domain.Tests.Payroll;

/// <summary>
/// The payroll arithmetic against hand-computed fixtures
/// (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// Every rate, ceiling and slab in this file is a figure the <b>test</b> chooses. That is the
/// property under examination: §2 extends non-negotiable #6 to PF and ESI rates, their wage
/// ceilings and PT slabs, so <c>PayrollMath</c> must not know any of them — and these tests would
/// pass unchanged after a notification moved every one.
/// </remarks>
public class PayrollMathTests
{
    private const long Rupee = 10_000L;
    private const long TwelvePercent = 120_000L;
    private const long OnePercent = 10_000L;

    private static long Rupees(long value) => value * Rupee;

    // ------------------------------------------------------------------- the wage ceiling caps

    [Fact]
    public void Contribution_BelowTheWageCeiling_ContributesOnTheWholeWage()
    {
        var result = PayrollMath.Contribution(
            Rupees(12_000),
            TwelvePercent,
            TwelvePercent,
            wageCeilingMinor: Rupees(15_000));

        result.ContributoryWageMinor.ShouldBe(Rupees(12_000));
        result.EmployeeShareMinor.ShouldBe(Rupees(1_440));
        result.EmployerShareMinor.ShouldBe(Rupees(1_440));
        result.IsExcluded.ShouldBeFalse();
    }

    /// <summary>
    /// A wage ceiling caps the base; it does not exclude anybody. An employee above it contributes
    /// on the capped wage, not on nothing — getting this backwards under-remits for every senior
    /// employee in the company.
    /// </summary>
    [Fact]
    public void Contribution_AboveTheWageCeiling_ContributesOnTheCappedWageNotOnNothing()
    {
        var result = PayrollMath.Contribution(
            Rupees(40_000),
            TwelvePercent,
            TwelvePercent,
            wageCeilingMinor: Rupees(15_000));

        result.ContributoryWageMinor.ShouldBe(Rupees(15_000));
        result.EmployeeShareMinor.ShouldBe(Rupees(1_800));
        result.IsExcluded.ShouldBeFalse();
        result.HasContribution.ShouldBeTrue();
    }

    [Fact]
    public void Contribution_WithNoCeilingConfigured_ContributesOnEverything()
    {
        var result = PayrollMath.Contribution(Rupees(40_000), TwelvePercent, TwelvePercent);

        result.ContributoryWageMinor.ShouldBe(Rupees(40_000));
        result.EmployeeShareMinor.ShouldBe(Rupees(4_800));
    }

    // -------------------------------------------------------- the eligibility ceiling excludes

    /// <summary>
    /// An eligibility ceiling is the opposite rule: above it the employee is out of the scheme.
    /// This is ESI, and it is why the two ceilings are two columns rather than one reused field.
    /// </summary>
    [Fact]
    public void Contribution_AboveTheEligibilityCeiling_ExcludesTheEmployeeEntirely()
    {
        var result = PayrollMath.Contribution(
            Rupees(30_000),
            OnePercent,
            3_250L,
            eligibilityCeilingMinor: Rupees(21_000));

        result.IsExcluded.ShouldBeTrue();
        result.EmployeeShareMinor.ShouldBe(0L);
        result.EmployerShareMinor.ShouldBe(0L);
        result.HasContribution.ShouldBeFalse();
    }

    [Fact]
    public void Contribution_ExactlyAtTheEligibilityCeiling_IsStillInsideTheScheme()
    {
        var result = PayrollMath.Contribution(
            Rupees(21_000),
            OnePercent,
            3_250L,
            eligibilityCeilingMinor: Rupees(21_000));

        result.IsExcluded.ShouldBeFalse();
        result.EmployeeShareMinor.ShouldBe(Rupees(210));
    }

    /// <summary>
    /// Both ceilings on one row, applied in the order the statutes mean: eligibility excludes
    /// first, and only then does the wage ceiling cap what is left. The other order would let a
    /// high earner be capped down into eligibility, which inverts the rule.
    /// </summary>
    [Fact]
    public void Contribution_WithBothCeilings_ExcludesBeforeItCaps()
    {
        var result = PayrollMath.Contribution(
            Rupees(30_000),
            TwelvePercent,
            TwelvePercent,
            wageCeilingMinor: Rupees(15_000),
            eligibilityCeilingMinor: Rupees(21_000));

        result.IsExcluded.ShouldBeTrue("the wage ceiling must not rescue an ineligible employee");
        result.EmployeeShareMinor.ShouldBe(0L);
    }

    [Fact]
    public void Contribution_RoundsEachShareToWholeRupees()
    {
        // 12% of 12,345 is 1,481.40 — remitted as 1,481.
        var result = PayrollMath.Contribution(Rupees(12_345), TwelvePercent, TwelvePercent);

        result.EmployeeShareMinor.ShouldBe(Rupees(1_481));
        (result.EmployeeShareMinor % Rupee).ShouldBe(0L, "a challan is paid in whole rupees");
    }

    [Fact]
    public void Contribution_OnAVeryLargeWage_DoesNotOverflow()
    {
        var result = PayrollMath.Contribution(Rupees(10_000_000), TwelvePercent, TwelvePercent);

        result.EmployeeShareMinor.ShouldBe(Rupees(1_200_000));
    }

    [Fact]
    public void Contribution_RefusesNegativeInput() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            PayrollMath.Contribution(-1L, TwelvePercent, TwelvePercent));

    // ------------------------------------------------------------------- professional tax slabs

    private static readonly (long From, long? To, long Tax)[] Slabs =
    [
        (Rupees(15_000), Rupees(25_000), Rupees(200)),
        (Rupees(25_000), null, Rupees(300)),
    ];

    [Fact]
    public void ProfessionalTax_InsideABand_ChargesThatBandsFlatAmount() =>
        PayrollMath.ProfessionalTax(Rupees(20_000), Slabs).ShouldBe(Rupees(200));

    /// <summary>
    /// The upper bound is exclusive, so a wage landing exactly on a boundary belongs to the band
    /// above. Inclusive bounds on both sides would let two adjacent bands claim one wage.
    /// </summary>
    [Fact]
    public void ProfessionalTax_ExactlyOnABoundary_BelongsToTheBandAbove()
    {
        PayrollMath.ProfessionalTax(Rupees(25_000), Slabs).ShouldBe(Rupees(300));
        PayrollMath.ProfessionalTax(Rupees(15_000), Slabs).ShouldBe(Rupees(200));
    }

    [Fact]
    public void ProfessionalTax_OnTheOpenTopBand_ChargesItsAmount() =>
        PayrollMath.ProfessionalTax(Rupees(90_000), Slabs).ShouldBe(Rupees(300));

    /// <summary>
    /// Below the lowest band there is simply no tax. A PT table usually has no bottom slab, and
    /// treating the gap as an error would refuse to pay the people it most affects.
    /// </summary>
    [Fact]
    public void ProfessionalTax_BelowTheLowestBand_IsZeroAndNotAnError() =>
        PayrollMath.ProfessionalTax(Rupees(9_000), Slabs).ShouldBe(0L);

    [Fact]
    public void ProfessionalTax_WithNoSlabsConfigured_IsZero() =>
        PayrollMath.ProfessionalTax(Rupees(50_000), []).ShouldBe(0L);

    // ------------------------------------------------------------------------ the head bases

    [Fact]
    public void HeadAmount_OnAFlatAmount_IsTheAmount() =>
        PayrollMath.HeadAmount(
            PayComputationBasis.FlatAmount,
            Rupees(20_000),
            percentMinor: null,
            basicMinor: 0L,
            grossMinor: 0L)
        .ShouldBe(Rupees(20_000));

    [Fact]
    public void HeadAmount_OnPercentOfBasic_ReadsTheBasicAndNotTheGross() =>
        PayrollMath.HeadAmount(
            PayComputationBasis.PercentOfBasic,
            amountMinor: null,
            percentMinor: 400_000L,
            basicMinor: Rupees(20_000),
            grossMinor: Rupees(35_000))
        .ShouldBe(Rupees(8_000));

    [Fact]
    public void HeadAmount_OnPercentOfGross_ReadsTheGrossAndNotTheBasic() =>
        PayrollMath.HeadAmount(
            PayComputationBasis.PercentOfGross,
            amountMinor: null,
            percentMinor: 100_000L,
            basicMinor: Rupees(20_000),
            grossMinor: Rupees(35_000))
        .ShouldBe(Rupees(3_500));

    /// <summary>
    /// A statutory head routed through here would need a rate this method has no business holding.
    /// It throws rather than returning zero, because a statutory head quietly worth nothing shows
    /// up as an under-remitted challan months later.
    /// </summary>
    [Fact]
    public void HeadAmount_OnTheStatutoryBasis_IsRefusedRatherThanReturningZero() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            PayrollMath.HeadAmount(PayComputationBasis.Statutory, null, null, 0L, 0L));

    [Fact]
    public void RoundToRupee_RoundsHalvesAwayFromZero()
    {
        PayrollMath.RoundToRupee(15_000L).ShouldBe(Rupees(2));
        PayrollMath.RoundToRupee(14_999L).ShouldBe(Rupees(1));
    }
}
