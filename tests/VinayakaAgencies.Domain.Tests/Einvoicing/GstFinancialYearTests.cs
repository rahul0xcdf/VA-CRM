using Shouldly;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Domain.Tests.Einvoicing;

/// <summary>
/// The <c>YYYY-YY</c> financial-year label that forms the second component of the IRN
/// (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// The boundary cases matter more than the ordinary ones here: 31 March and 1 April belong to
/// different financial years, and the label for a date in January carries the <em>previous</em>
/// calendar year. Both are the kind of off-by-one that produces an IRN the portal rejects without
/// explaining why.
/// </remarks>
public sealed class GstFinancialYearTests
{
    [Fact]
    public void For_TheNicExampleDate_IsTheLabelNicPublished()
    {
        // 12 December 2019 → 2019-20, per NIC's own worked IRN example.
        GstFinancialYear.For(new DateOnly(2019, 12, 12)).ShouldBe("2019-20");
    }

    [Fact]
    public void For_FirstDayOfTheYear_StartsTheNewLabel()
    {
        GstFinancialYear.For(new DateOnly(2026, 4, 1)).ShouldBe("2026-27");
    }

    [Fact]
    public void For_LastDayOfTheYear_KeepsTheOldLabel()
    {
        GstFinancialYear.For(new DateOnly(2026, 3, 31)).ShouldBe("2025-26");
    }

    [Fact]
    public void For_ADateInJanuary_UsesThePreviousCalendarYearAsTheFirstHalf()
    {
        // January falls in the year that began the previous April — the label is not simply the
        // calendar year.
        GstFinancialYear.For(new DateOnly(2026, 1, 15)).ShouldBe("2025-26");
    }

    [Fact]
    public void For_ACenturyBoundary_RendersTheSecondHalfAsTwoDigits()
    {
        // 2099-00 rather than 2099-100: the second half is the year modulo 100, zero-padded.
        GstFinancialYear.For(new DateOnly(2099, 6, 1)).ShouldBe("2099-00");
    }

    [Fact]
    public void For_ADateInTheNineties_PadsTheSecondHalfWithoutTruncatingTheFirst()
    {
        GstFinancialYear.For(new DateOnly(2009, 5, 1)).ShouldBe("2009-10");
    }

    [Fact]
    public void StartMonth_IsApril_BecauseTheCgstActSaysSoRatherThanBecauseTheCompanyDoes()
    {
        // Deliberately not driven by Company.FinancialYearStartMonth: feeding a company-specific
        // cycle into an IRN would produce a hash the portal computes differently. See the type's
        // remarks.
        GstFinancialYear.StartMonth.ShouldBe(4);
    }
}
