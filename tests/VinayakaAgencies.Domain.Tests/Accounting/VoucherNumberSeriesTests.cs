using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Domain.Tests.Accounting;

/// <summary>Decision D2: prefix + zero-padded width + optional suffix, e.g. <c>PMT/0007</c>.</summary>
public class VoucherNumberSeriesTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid VoucherTypeId = Guid.NewGuid();

    [Fact]
    public void Format_ZeroPadsToWidth()
    {
        var series = new VoucherNumberSeries(CompanyId, VoucherTypeId, "Payment", "PMT/", width: 4);

        series.Format(7).ShouldBe("PMT/0007");
    }

    [Fact]
    public void Format_WithSuffix_AppendsSuffixAfterTheDigits()
    {
        var series = new VoucherNumberSeries(
            CompanyId, VoucherTypeId, "Credit Note", "CN/", width: 4, suffix: "/26-27");

        series.Format(12).ShouldBe("CN/0012/26-27");
    }

    [Fact]
    public void Format_NumberWiderThanPadding_IsNotTruncated()
    {
        var series = new VoucherNumberSeries(CompanyId, VoucherTypeId, "Payment", "PMT/", width: 2);

        series.Format(12345).ShouldBe("PMT/12345");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Format_SequenceNumberLessThanOne_ThrowsArgumentOutOfRangeException(long invalidSequence)
    {
        var series = new VoucherNumberSeries(CompanyId, VoucherTypeId, "Payment", "PMT/");

        Should.Throw<ArgumentOutOfRangeException>(() => series.Format(invalidSequence));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(13)]
    public void Constructor_WidthOutsideAllowedRange_ThrowsArgumentOutOfRangeException(int invalidWidth)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new VoucherNumberSeries(CompanyId, VoucherTypeId, "Payment", "PMT/", width: invalidWidth));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Constructor_WidthAtBoundary_IsAccepted(int boundaryWidth)
    {
        var series = new VoucherNumberSeries(CompanyId, VoucherTypeId, "Payment", "PMT/", width: boundaryWidth);

        series.Width.ShouldBe(boundaryWidth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_StartNumberLessThanOne_ThrowsArgumentOutOfRangeException(long invalidStart)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new VoucherNumberSeries(
                CompanyId, VoucherTypeId, "Payment", "PMT/", startNumber: invalidStart));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Reconfigure_WidthOutsideAllowedRange_ThrowsArgumentOutOfRangeException(int invalidWidth)
    {
        var series = new VoucherNumberSeries(CompanyId, VoucherTypeId, "Payment", "PMT/");

        Should.Throw<ArgumentOutOfRangeException>(
            () => series.Reconfigure("PMT/", invalidWidth, null, allowManualOverride: true));
    }
}
