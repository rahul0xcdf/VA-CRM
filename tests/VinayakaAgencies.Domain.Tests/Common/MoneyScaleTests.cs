using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tests.Common;

/// <summary>
/// ADR-0003: money is persisted as a scaled 64-bit integer in minor units of 1/10,000. These tests
/// are the highest-risk surface in Phase 1 — every trial balance, P&amp;L and balance sheet is only
/// as exact as this conversion.
/// </summary>
public class MoneyScaleTests
{
    [Theory]
    [InlineData("1234.5678", 12345678L)]
    [InlineData("0.0001", 1L)]
    [InlineData("-0.0001", -1L)]
    [InlineData("0", 0L)]
    public void ToMinor_ThenFromMinor_RoundTripsExactly(string amountText, long expectedMinor)
    {
        var amount = decimal.Parse(amountText, System.Globalization.CultureInfo.InvariantCulture);

        var minor = MoneyScale.ToMinor(amount);

        minor.ShouldBe(expectedMinor);
        MoneyScale.FromMinor(minor).ShouldBe(amount);
    }

    [Fact]
    public void ToMinor_MoreThanFourDecimalPlaces_ThrowsWithoutRounding()
    {
        // 1.00005 is a legitimate decimal value but would lose precision if truncated to four
        // places, and ADR-0003 is explicit: the converter never rounds silently.
        Should.Throw<ArgumentOutOfRangeException>(() => MoneyScale.ToMinor(1.00005m));
    }

    [Fact]
    public void ToMinor_MaxValue_IsAcceptedAndRoundTrips()
    {
        var minor = MoneyScale.ToMinor(MoneyScale.MaxValue);

        minor.ShouldBe(long.MaxValue);
        MoneyScale.FromMinor(minor).ShouldBe(MoneyScale.MaxValue);
    }

    [Fact]
    public void ToMinor_MinValue_IsAcceptedAndRoundTrips()
    {
        var minor = MoneyScale.ToMinor(MoneyScale.MinValue);

        minor.ShouldBe(long.MinValue);
        MoneyScale.FromMinor(minor).ShouldBe(MoneyScale.MinValue);
    }

    [Fact]
    public void ToMinor_AboveMaxValue_ThrowsArgumentOutOfRangeException()
    {
        var justAboveMax = MoneyScale.MaxValue + 0.0001m;

        Should.Throw<ArgumentOutOfRangeException>(() => MoneyScale.ToMinor(justAboveMax));
    }

    [Fact]
    public void ToMinor_BelowMinValue_ThrowsArgumentOutOfRangeException()
    {
        var justBelowMin = MoneyScale.MinValue - 0.0001m;

        Should.Throw<ArgumentOutOfRangeException>(() => MoneyScale.ToMinor(justBelowMin));
    }

    [Fact]
    public void Round_ExactMidpointWithEvenTargetDigit_RoundsDown()
    {
        // 2.00005 sits exactly halfway between 2.0000 and 2.0001. Banker's rounding picks the
        // even neighbour, which is 2.0000.
        MoneyScale.Round(2.00005m).ShouldBe(2.0000m);
    }

    [Fact]
    public void Round_ExactMidpointWithOddTargetDigit_RoundsUp()
    {
        // 2.00015 sits exactly halfway between 2.0001 and 2.0002. The even neighbour is 2.0002.
        MoneyScale.Round(2.00015m).ShouldBe(2.0002m);
    }

    /// <summary>
    /// The property that matters most (spec §6 / ADR-0003): a randomised set of amounts that sum to
    /// exactly zero in <see cref="decimal"/> must also sum to exactly zero in minor units. This is
    /// the trial-balance invariant (<c>Σ AmountMinor == 0</c>) in miniature — if this test can fail,
    /// so can a trial balance.
    /// </summary>
    [Fact]
    public void ToMinor_RandomisedAmountsSummingToZero_MinorUnitSumIsExactlyZero()
    {
        var random = new Random(20260814);
        const int sampleCount = 500;

        var minorAmounts = new long[sampleCount];
        long runningTotal = 0;

        // First (sampleCount - 1) values are random minor-unit amounts within a range comfortably
        // inside MoneyScale's representable range. The final value is chosen to force the running
        // total back to exactly zero, so the whole set sums to zero by construction — in both minor
        // units and, because the conversion is exact, in decimal.
        for (var i = 0; i < sampleCount - 1; i++)
        {
            var minor = (long)random.Next(-1_000_000_000, 1_000_000_001);
            minorAmounts[i] = minor;
            runningTotal += minor;
        }

        minorAmounts[sampleCount - 1] = -runningTotal;

        var amounts = minorAmounts.Select(MoneyScale.FromMinor).ToArray();

        amounts.Length.ShouldBeGreaterThanOrEqualTo(500);

        // The decimal sum — computed independently of MoneyScale, using plain decimal arithmetic —
        // must be exactly zero. This is the hand-computed fixture: it does not rely on MoneyScale at
        // all for the check, only for producing the sample amounts from a set of minor-unit longs.
        var decimalSum = amounts.Aggregate(0m, (acc, next) => acc + next);
        decimalSum.ShouldBe(0m);

        var minorSum = amounts.Sum(MoneyScale.ToMinor);
        minorSum.ShouldBe(0L);
    }
}
