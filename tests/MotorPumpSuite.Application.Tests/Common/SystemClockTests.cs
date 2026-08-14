using MotorPumpSuite.Application.Common;
using Shouldly;

namespace MotorPumpSuite.Application.Tests.Common;

/// <summary>
/// <see cref="IClock"/>'s contract distinguishes UTC (system/audit timestamps) from the local
/// calendar date (business dates — Indian financial-year logic follows the local date). These tests
/// prove <see cref="SystemClock"/> honours both halves of that contract.
/// </summary>
public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_Always_HasZeroUtcOffset()
    {
        var sut = new SystemClock();

        sut.UtcNow.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void UtcNow_Always_TracksTheRealInstant()
    {
        // Bracket the read with two independent DateTimeOffset.UtcNow calls rather than comparing
        // to the SUT's own output, so this cannot pass by construction.
        var before = DateTimeOffset.UtcNow;
        var sut = new SystemClock();

        var actual = sut.UtcNow;

        var after = DateTimeOffset.UtcNow;
        actual.ShouldBeInRange(before, after);
    }

    [Fact]
    public void Today_Always_TracksTheLocalCalendarDate()
    {
        // DateOnly.FromDateTime(DateTime.Now) is the local-date reference the interface promises
        // ("Today's date in the company's local calendar" — not UTC). Bracket the read between two
        // independent local-date snapshots, rather than asserting exact equality against a single
        // snapshot, so the test cannot flip to red purely because the clock crossed local midnight
        // between the "before" read and the SUT read.
        var before = DateOnly.FromDateTime(DateTime.Now);
        var sut = new SystemClock();

        var actual = sut.Today;

        var after = DateOnly.FromDateTime(DateTime.Now);
        actual.ShouldBeInRange(before, after);
    }
}
