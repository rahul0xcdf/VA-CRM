using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Domain.Tests.Accounting;

/// <summary>
/// Decision D1: the financial year starts 1 April. <see cref="FinancialYear.RangeFor"/> is the
/// boundary logic every posting-time date check relies on, so the day either side of 1 April is the
/// case that matters most.
/// </summary>
public class FinancialYearTests
{
    [Fact]
    public void RangeFor_31MarchWithAprilStart_BelongsToThePriorYear()
    {
        var (start, end) = FinancialYear.RangeFor(new DateOnly(2027, 3, 31), startMonth: 4);

        start.ShouldBe(new DateOnly(2026, 4, 1));
        end.ShouldBe(new DateOnly(2027, 3, 31));
    }

    [Fact]
    public void RangeFor_1AprilWithAprilStart_BelongsToTheNewYear()
    {
        var (start, end) = FinancialYear.RangeFor(new DateOnly(2027, 4, 1), startMonth: 4);

        start.ShouldBe(new DateOnly(2027, 4, 1));
        end.ShouldBe(new DateOnly(2028, 3, 31));
    }

    [Fact]
    public void RangeFor_InvalidStartMonth_ThrowsArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => FinancialYear.RangeFor(new DateOnly(2026, 8, 14), startMonth: 13));
    }

    [Fact]
    public void RangeFor_StartMonthZero_ThrowsArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => FinancialYear.RangeFor(new DateOnly(2026, 8, 14), startMonth: 0));
    }

    [Fact]
    public void Label_SpanningTwoCalendarYears_UsesShortForm()
    {
        var year = new FinancialYear(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));

        year.Label.ShouldBe("2026-27");
    }

    [Fact]
    public void Label_WithinOneCalendarYear_IsJustTheYear()
    {
        var year = new FinancialYear(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        year.Label.ShouldBe("2026");
    }

    [Fact]
    public void Contains_DateInsideRange_ReturnsTrue()
    {
        var year = new FinancialYear(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));

        year.Contains(new DateOnly(2026, 12, 25)).ShouldBeTrue();
    }

    [Fact]
    public void Contains_StartAndEndDatesThemselves_ReturnTrue()
    {
        var year = new FinancialYear(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));

        year.Contains(new DateOnly(2026, 4, 1)).ShouldBeTrue();
        year.Contains(new DateOnly(2027, 3, 31)).ShouldBeTrue();
    }

    [Fact]
    public void Contains_DateOutsideRange_ReturnsFalse()
    {
        var year = new FinancialYear(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));

        year.Contains(new DateOnly(2027, 4, 1)).ShouldBeFalse();
        year.Contains(new DateOnly(2026, 3, 31)).ShouldBeFalse();
    }

    [Fact]
    public void Constructor_EndDateEqualToStartDate_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(
            () => new FinancialYear(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public void Constructor_EndDateBeforeStartDate_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(
            () => new FinancialYear(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Constructor_EmptyCompanyId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(
            () => new FinancialYear(Guid.Empty, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)));
    }
}
