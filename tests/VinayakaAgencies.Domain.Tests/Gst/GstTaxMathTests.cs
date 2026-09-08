using VinayakaAgencies.Domain.Gst;
using Shouldly;

namespace VinayakaAgencies.Domain.Tests.Gst;

/// <summary>The GST arithmetic conventions, hand-computed (phase-5-gst-returns.md §2).</summary>
public sealed class GstTaxMathTests
{
    [Fact]
    public void TaxMinor_EighteenPercentOnTenThousand_ComputesExactTax()
    {
        // ₹10,000.0000 taxable at 18% = ₹1,800.0000.
        GstTaxMath.TaxMinor(100_000_000L, 180_000L).ShouldBe(18_000_000L);
    }

    [Fact]
    public void TaxMinor_ZeroRate_ReturnsZeroWithoutArithmetic()
    {
        GstTaxMath.TaxMinor(100_000_000L, 0L).ShouldBe(0L);
    }

    [Fact]
    public void TaxMinor_HalfPaise_RoundsAwayFromZero()
    {
        // ₹0.1250 at 18% = ₹0.0225 → rounds to ₹0.03 (half away from zero), not banker's.
        GstTaxMath.TaxMinor(1_250L, 180_000L).ShouldBe(225L);
    }

    [Fact]
    public void SplitHalf_OddTotal_PutsRemainderOnSgst()
    {
        var (cgst, sgst) = GstTaxMath.SplitHalf(18_000_001L);

        cgst.ShouldBe(9_000_000L);
        sgst.ShouldBe(9_000_001L);
        (cgst + sgst).ShouldBe(18_000_001L);
    }

    [Theory]
    [InlineData(100_500L, -500L)]
    [InlineData(106_000L, 4_000L)]
    [InlineData(120_000_000L, 0L)]
    public void RupeeRoundOffMinor_LandsTotalOnNearestRupee(long totalMinor, long expectedRoundOff)
    {
        GstTaxMath.RupeeRoundOffMinor(totalMinor).ShouldBe(expectedRoundOff);
    }
}
