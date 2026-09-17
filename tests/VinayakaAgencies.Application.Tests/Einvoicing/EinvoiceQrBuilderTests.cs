using Shouldly;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Application.Tests.Einvoicing;

/// <summary>
/// The ten signed-QR fields (phase-6-einvoice-eway.md §2, checkpoint C6.1).
/// </summary>
/// <remarks>
/// The field that earns its own tests is <c>MainHsnCode</c>: it is the HSN of the line with the
/// <em>highest taxable value</em>, and taking the first line instead is a one-character mistake with
/// no visible symptom on a single-line invoice.
/// </remarks>
public sealed class EinvoiceQrBuilderTests
{
    private const string SellerGstin = "29AAAAA9999A1Z5";
    private const string BuyerGstin = "27BBBBB8888B1Z3";
    private static readonly DateOnly InvoiceDate = new(2026, 9, 17);

    [Fact]
    public void MainHsnCode_IsTheHighestTaxableValueLine_NotTheFirst()
    {
        var lines = new[]
        {
            Line("84131990", taxableMinor: 10_000_000L),   // ₹1,000
            Line("85011012", taxableMinor: 250_000_000L),  // ₹25,000 — the main one
            Line("73079990", taxableMinor: 50_000_000L),   // ₹5,000
        };

        EinvoiceQrBuilder.MainHsnCode(lines).ShouldBe("85011012");
    }

    [Fact]
    public void MainHsnCode_OnASingleLineInvoice_IsThatLine()
    {
        EinvoiceQrBuilder.MainHsnCode([Line("84131990", 10_000_000L)]).ShouldBe("84131990");
    }

    [Fact]
    public void MainHsnCode_OnATie_TakesTheFirstSuchLine_SoTheResultIsStable()
    {
        var lines = new[]
        {
            Line("84131990", 100_000_000L),
            Line("85011012", 100_000_000L),
        };

        EinvoiceQrBuilder.MainHsnCode(lines).ShouldBe("84131990");
    }

    [Fact]
    public void MainHsnCode_WithNoLines_IsRefused()
    {
        Should.Throw<ArgumentException>(() => EinvoiceQrBuilder.MainHsnCode([]));
    }

    [Fact]
    public void TotalInvoiceValueMinor_SumsLineTotalsAndTheRoundOff()
    {
        // ₹1,000 taxable + ₹90 CGST + ₹90 SGST = ₹1,180, less ₹0.20 of rounding = ₹1,179.80.
        var request = Request(
            [Line("84131990", 10_000_000L, cgstMinor: 900_000L, sgstMinor: 900_000L)],
            roundOffMinor: -2_000L);

        EinvoiceQrBuilder.TotalInvoiceValueMinor(request).ShouldBe(11_798_000L);
    }

    [Fact]
    public void TotalInvoiceValueMinor_IncludesCess()
    {
        var request = Request([Line("84131990", 10_000_000L, cessMinor: 100_000L)]);

        EinvoiceQrBuilder.TotalInvoiceValueMinor(request).ShouldBe(10_100_000L);
    }

    [Fact]
    public void Build_CarriesAllTenFields()
    {
        var request = Request(
        [
            Line("84131990", 10_000_000L, cgstMinor: 900_000L, sgstMinor: 900_000L),
            Line("85011012", 250_000_000L, cgstMinor: 22_500_000L, sgstMinor: 22_500_000L),
        ]);

        var irn = new string('a', 64);
        var qr = EinvoiceQrBuilder.Build(request, irn, "2026-09-17 17:30:00");

        qr.SellerGstin.ShouldBe(SellerGstin);
        qr.BuyerGstin.ShouldBe(BuyerGstin);
        qr.DocumentNumber.ShouldBe("SAL/0007");
        qr.DocumentTypeCode.ShouldBe("INV");
        qr.DocumentDate.ShouldBe(InvoiceDate);
        qr.TotalInvoiceValueMinor.ShouldBe(306_800_000L);
        qr.ItemCount.ShouldBe(2);
        qr.MainHsnCode.ShouldBe("85011012");
        qr.Irn.ShouldBe(irn);
        qr.IrnDateText.ShouldBe("2026-09-17 17:30:00");
    }

    [Fact]
    public void Build_WithNoLines_IsRefused()
    {
        Should.Throw<InvalidOperationException>(
            () => EinvoiceQrBuilder.Build(Request([]), new string('a', 64), "2026-09-17 17:30:00"));
    }

    [Fact]
    public void Build_ForAnUnregisteredBuyer_CarriesThePortalsPlaceholder()
    {
        var request = Request([Line("84131990", 10_000_000L)]) with
        {
            Buyer = new EinvoicePartyDetails(
                SignedQrPayload.UnregisteredParty, "Walk-in customer", "Main Road", "Mysuru", "29", 570001),
        };

        EinvoiceQrBuilder.Build(request, new string('a', 64), "x").BuyerGstin.ShouldBe("URP");
    }

    private static EinvoiceLine Line(
        string hsnCode,
        long taxableMinor,
        long cgstMinor = 0L,
        long sgstMinor = 0L,
        long igstMinor = 0L,
        long cessMinor = 0L) =>
        new(hsnCode,
            "Item",
            "NOS",
            QuantityMinor: 10_000L,
            UnitPriceMinor: taxableMinor,
            TaxableValueMinor: taxableMinor,
            GstRatePercentMinor: 180_000L,
            IgstMinor: igstMinor,
            CgstMinor: cgstMinor,
            SgstMinor: sgstMinor,
            CessMinor: cessMinor);

    private static EinvoiceRequest Request(
        IReadOnlyList<EinvoiceLine> lines,
        long roundOffMinor = 0L) =>
        new(EinvoiceDocumentType.TaxInvoice,
            "SAL/0007",
            InvoiceDate,
            IsReverseCharge: false,
            new EinvoicePartyDetails(SellerGstin, "Vinayaka Agencies", "Market Road", "Mysuru", "29", 570001),
            new EinvoicePartyDetails(BuyerGstin, "Buyer Pumps", "Industrial Estate", "Pune", "27", 411001),
            PlaceOfSupplyStateCode: "29",
            lines,
            roundOffMinor);
}
