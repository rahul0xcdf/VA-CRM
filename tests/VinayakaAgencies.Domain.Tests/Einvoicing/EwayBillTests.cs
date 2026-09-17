using Shouldly;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Domain.Tests.Einvoicing;

/// <summary>
/// The e-way bill rules: validity slabs, the threshold test, Part-B's per-mode requirements, and the
/// 24-hour cancellation window (phase-6-einvoice-eway.md §2, §4a).
/// </summary>
public sealed class EwayBillTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Document = Guid.NewGuid();
    private static readonly DateTimeOffset Noon = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(200, 1)]
    [InlineData(201, 2)]
    [InlineData(400, 2)]
    [InlineData(401, 3)]
    public void DaysFor_RegularCarriage_IsOneDayPerTwoHundredKilometresRoundedUp(
        int distance, int expectedDays)
    {
        EwayBillValidity.DaysFor(distance, VehicleType.Regular).ShouldBe(expectedDays);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(200, 10)]
    public void DaysFor_OverDimensionalCargo_IsOneDayPerTwentyKilometres(int distance, int expectedDays)
    {
        // A tenth of the validity per kilometre. Applying the regular slab to an ODC consignment would
        // overstate its validity tenfold, which is why vehicle type is part of this calculation.
        EwayBillValidity.DaysFor(distance, VehicleType.OverDimensionalCargo).ShouldBe(expectedDays);
    }

    [Fact]
    public void DaysFor_ZeroDistance_IsStillOneDay_NotZero()
    {
        EwayBillValidity.DaysFor(0, VehicleType.Regular).ShouldBe(1);
    }

    [Fact]
    public void DaysFor_NegativeDistance_IsRefused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => EwayBillValidity.DaysFor(-1, VehicleType.Regular));
    }

    [Fact]
    public void IsRequired_AtExactlyTheThreshold_IsFalse_BecauseTheTestIsExceeds()
    {
        // ₹50,000 exactly does not need a bill; ₹50,000.0001 does.
        EwayBillThresholdRule.IsRequired(
            EinvoiceSettings.DefaultEwayBillThresholdMinor,
            EinvoiceSettings.DefaultEwayBillThresholdMinor).ShouldBeFalse();

        EwayBillThresholdRule.IsRequired(
            EinvoiceSettings.DefaultEwayBillThresholdMinor + 1,
            EinvoiceSettings.DefaultEwayBillThresholdMinor).ShouldBeTrue();
    }

    [Fact]
    public void DefaultThreshold_IsFiftyThousandRupeesInMinorUnits()
    {
        EinvoiceSettings.DefaultEwayBillThresholdMinor.ShouldBe(500_000_000L);
    }

    [Fact]
    public void SetPartB_ByRoadWithoutAVehicleNumber_IsRefused()
    {
        var bill = new EwayBill(Company, Document, 600_000_000L, 120);

        Should.Throw<ArgumentException>(
            () => bill.SetPartB(TransportMode.Road, VehicleType.Regular));
    }

    [Fact]
    public void SetPartB_ByRailWithoutATransportDocument_IsRefused()
    {
        var bill = new EwayBill(Company, Document, 600_000_000L, 120);

        Should.Throw<ArgumentException>(
            () => bill.SetPartB(TransportMode.Rail, VehicleType.Regular, vehicleNumber: "KA01AB1234"));
    }

    [Fact]
    public void SetPartB_ByRoadWithAVehicleNumber_CompletesPartB()
    {
        var bill = new EwayBill(Company, Document, 600_000_000L, 120);

        bill.SetPartB(TransportMode.Road, VehicleType.Regular, vehicleNumber: "ka01ab1234");

        bill.IsPartBComplete.ShouldBeTrue();
        bill.VehicleNumber.ShouldBe("KA01AB1234");
    }

    [Fact]
    public void SetPartB_ByShipWithADocument_CompletesPartB()
    {
        var bill = new EwayBill(Company, Document, 600_000_000L, 900);

        bill.SetPartB(
            TransportMode.Ship,
            VehicleType.Regular,
            transportDocumentNumber: "BL-4471",
            transportDocumentDate: new DateOnly(2026, 9, 17));

        bill.IsPartBComplete.ShouldBeTrue();
    }

    [Fact]
    public void MarkGenerated_SetsValidityFromTheDistanceAndVehicleType()
    {
        var bill = new EwayBill(Company, Document, 600_000_000L, 450);

        bill.SetPartB(TransportMode.Road, VehicleType.Regular, vehicleNumber: "KA01AB1234");
        bill.MarkGenerated(391_000_123_456L, Noon);

        // 450 km regular = three days.
        bill.ValidUntilUtc.ShouldBe(Noon.AddDays(3));
        bill.Status.ShouldBe(EwayBillStatus.Active);
        bill.IsValidAt(Noon.AddDays(2)).ShouldBeTrue();
        bill.IsValidAt(Noon.AddDays(4)).ShouldBeFalse();
    }

    [Fact]
    public void CanCancelAt_WithinTwentyFourHours_IsTrue_AndAfterwardsFalse()
    {
        var bill = Generated();

        bill.CanCancelAt(Noon.AddHours(23)).ShouldBeTrue();
        bill.CanCancelAt(Noon.AddHours(25)).ShouldBeFalse();
    }

    [Fact]
    public void MarkCancelled_AfterTheWindow_IsRefused()
    {
        var bill = Generated();

        Should.Throw<InvalidOperationException>(
            () => bill.MarkCancelled(Noon.AddHours(25), "1"));
    }

    [Fact]
    public void MarkCancelled_WithinTheWindow_RecordsTheReason()
    {
        var bill = Generated();

        bill.MarkCancelled(Noon.AddHours(2), "2", "Order withdrawn");

        bill.Status.ShouldBe(EwayBillStatus.Cancelled);
        bill.CancellationReasonCode.ShouldBe("2");
        bill.CancellationRemarks.ShouldBe("Order withdrawn");
    }

    [Fact]
    public void MarkGenerated_AfterCancellation_IsRefused()
    {
        var bill = Generated();

        bill.MarkCancelled(Noon.AddHours(1), "1");

        Should.Throw<InvalidOperationException>(() => bill.MarkGenerated(2L, Noon.AddHours(2)));
    }

    [Fact]
    public void MarkExtended_CannotShortenValidity()
    {
        var bill = Generated();

        Should.Throw<ArgumentException>(
            () => bill.MarkExtended(Noon.AddHours(1), remainingDistanceKilometres: 10));
    }

    [Fact]
    public void MarkExtended_MovesTheExpiryOutAndRecordsTheRemainingDistance()
    {
        var bill = Generated();

        bill.MarkExtended(Noon.AddDays(5), remainingDistanceKilometres: 80);

        bill.ValidUntilUtc.ShouldBe(Noon.AddDays(5));
        bill.DistanceKilometres.ShouldBe(80);
    }

    [Fact]
    public void Constructor_WithAMalformedIrn_IsRefused()
    {
        Should.Throw<ArgumentException>(
            () => new EwayBill(Company, Document, 1L, 10, irn: "not-an-irn"));
    }

    [Fact]
    public void Constructor_WithANegativeConsignmentValue_IsRefused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new EwayBill(Company, Document, -1L, 10));
    }

    private static EwayBill Generated()
    {
        var bill = new EwayBill(Company, Document, 600_000_000L, 120);

        bill.SetPartB(TransportMode.Road, VehicleType.Regular, vehicleNumber: "KA01AB1234");
        bill.MarkGenerated(391_000_123_456L, Noon);

        return bill;
    }
}
