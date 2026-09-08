using VinayakaAgencies.Domain.Gst;
using Shouldly;

namespace VinayakaAgencies.Domain.Tests.Gst;

/// <summary>
/// Guard and effective-date behaviour of rate schedules (phase-5-gst-returns.md §2, C5.5).
/// </summary>
public sealed class GstRateScheduleTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid HsnSacId = Guid.NewGuid();

    [Fact]
    public void Constructor_NegativeRate_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new GstRateSchedule(
            CompanyId, HsnSacId, ratePercentMinor: -1L, new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public void Constructor_NegativeCess_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new GstRateSchedule(
            CompanyId, HsnSacId, 180_000L, new DateOnly(2026, 4, 1), compensationCessPercentMinor: -1L));
    }

    [Fact]
    public void Constructor_EndBeforeStart_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new GstRateSchedule(
            CompanyId,
            HsnSacId,
            180_000L,
            new DateOnly(2026, 7, 1),
            effectiveTo: new DateOnly(2026, 6, 30)));
    }

    [Fact]
    public void Covers_OpenEndedRow_CoversEveryLaterDateAndNotEarlier()
    {
        var schedule = new GstRateSchedule(CompanyId, HsnSacId, 120_000L, new DateOnly(2026, 4, 1));

        schedule.Covers(new DateOnly(2026, 3, 31)).ShouldBeFalse();
        schedule.Covers(new DateOnly(2026, 4, 1)).ShouldBeTrue();
        schedule.Covers(new DateOnly(2030, 12, 31)).ShouldBeTrue();
    }

    [Fact]
    public void Covers_DatedRow_InclusiveOfBothEnds()
    {
        var schedule = new GstRateSchedule(
            CompanyId,
            HsnSacId,
            180_000L,
            new DateOnly(2026, 7, 1),
            effectiveTo: new DateOnly(2027, 3, 31));

        schedule.Covers(new DateOnly(2026, 6, 30)).ShouldBeFalse();
        schedule.Covers(new DateOnly(2026, 7, 1)).ShouldBeTrue();
        schedule.Covers(new DateOnly(2027, 3, 31)).ShouldBeTrue();
        schedule.Covers(new DateOnly(2027, 4, 1)).ShouldBeFalse();
    }

    [Fact]
    public void CloseAsOf_BeforeEffectiveFrom_Throws()
    {
        var schedule = new GstRateSchedule(CompanyId, HsnSacId, 120_000L, new DateOnly(2026, 4, 1));

        Should.Throw<ArgumentOutOfRangeException>(
            () => schedule.CloseAsOf(new DateOnly(2026, 3, 31)));
    }

    [Fact]
    public void CloseAsOf_ValidDate_SetsInclusiveEnd()
    {
        var schedule = new GstRateSchedule(CompanyId, HsnSacId, 120_000L, new DateOnly(2026, 4, 1));

        schedule.CloseAsOf(new DateOnly(2026, 6, 30));

        schedule.EffectiveTo.ShouldBe(new DateOnly(2026, 6, 30));
        schedule.Covers(new DateOnly(2026, 6, 30)).ShouldBeTrue();
        schedule.Covers(new DateOnly(2026, 7, 1)).ShouldBeFalse();
    }
}
