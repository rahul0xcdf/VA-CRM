using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Application.Tests.TestDoubles;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;
using Shouldly;

namespace VinayakaAgencies.Application.Tests.Gst;

/// <summary>
/// The tax computation against hand-computed fixtures: the intra/inter-state split (C5.2), the
/// per-invoice rupee round-off routing, reverse charge, and the unconfigured-item warning
/// (phase-5-gst-returns.md §2).
/// </summary>
public sealed class GstComputationServiceTests : IDisposable
{
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly GstComputationService _service;

    public GstComputationServiceTests()
    {
        _service = new GstComputationService(_unitOfWork, new StubCompanyContext());
    }

    public void Dispose() => _unitOfWork.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>Stages the company (Karnataka, "29") plus one coded item at the given rate.</summary>
    private async Task<TradeDocument> ArrangeInvoiceAsync(
        long ratePercentMinor = 180_000L,
        bool isReverseCharge = false,
        string? partyGstin = null,
        string itemName = "Openwell Pump",
        decimal rate = 10_000m)
    {
        var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29", "29ABCDE1234F1Z5");
        var companyId = company.Id;
        await _unitOfWork.Repository<Company>().AddAsync(company);

        var code = new HsnSac(companyId, "84137010", "Centrifugal pumps");
        await _unitOfWork.Repository<HsnSac>().AddAsync(code);
        await _unitOfWork.Repository<GstRateSchedule>().AddAsync(
            new GstRateSchedule(companyId, code.Id, ratePercentMinor, new DateOnly(2020, 1, 1)));

        var unit = new UnitOfMeasure(companyId, "NOS", 0, "Numbers");
        var item = new Item(companyId, itemName, unit.Id);
        item.AssignHsnSac(code.Id);
        await _unitOfWork.Repository<Item>().AddAsync(item);

        var document = new TradeDocument(
            companyId,
            TradeDocumentKind.TaxInvoice,
            new DateOnly(2026, 6, 15),
            Guid.NewGuid(),
            narration: null);

        document.SetPartyGstin(partyGstin);
        document.SetReverseCharge(isReverseCharge);
        document.AddLine(item.Id, Guid.NewGuid(), 1m, rate, 0m, isSerialTracked: false);

        return document;
    }

    // ---------------------------------------------------------------- C5.2 — intra vs inter state

    [Fact]
    public async Task ComputeAndApply_IntraStateSupply_SplitsHalfCgstHalfSgst()
    {
        // ₹10,000 at 18% = ₹1,800 → CGST ₹900 + SGST ₹900, IGST nil.
        var invoice = await ArrangeInvoiceAsync();

        var result = await _service.ComputeAndApplyAsync(invoice);

        result.IsIntraState.ShouldBeTrue();
        result.PlaceOfSupplyStateCode.ShouldBe("29");
        result.TotalTaxableValueMinor.ShouldBe(100_000_000L);
        result.TotalIgstMinor.ShouldBe(0L);
        result.TotalCgstMinor.ShouldBe(9_000_000L);
        result.TotalSgstMinor.ShouldBe(9_000_000L);
        result.TotalCessMinor.ShouldBe(0L);
        result.TotalTaxMinor.ShouldBe(18_000_000L);
    }

    [Fact]
    public async Task ComputeAndApply_InterStateSupply_ChargesFullIgst()
    {
        // A Maharashtra place of supply makes the supply inter-state:
        // ₹10,000 at 18% = ₹1,800 IGST, CGST/SGST nil.
        var invoice = await ArrangeInvoiceAsync(partyGstin: "27ABCDE1234F1Z5");

        var result = await _service.ComputeAndApplyAsync(invoice);

        result.IsIntraState.ShouldBeFalse();
        result.PlaceOfSupplyStateCode.ShouldBe("27");
        result.TotalIgstMinor.ShouldBe(18_000_000L);
        result.TotalCgstMinor.ShouldBe(0L);
        result.TotalSgstMinor.ShouldBe(0L);
        result.TotalTaxMinor.ShouldBe(18_000_000L);
    }

    [Fact]
    public async Task ComputeAndApply_SnapshotIsWrittenOntoTheAggregate()
    {
        var invoice = await ArrangeInvoiceAsync();
        var line = invoice.Lines.Single();

        await _service.ComputeAndApplyAsync(invoice);

        invoice.PlaceOfSupplyStateCode.ShouldBe("29");
        invoice.IsReverseCharge.ShouldBeFalse();
        line.TaxableValueMinor.ShouldBe(100_000_000L);
        line.GstRatePercentMinor.ShouldBe(180_000L);
        line.IgstMinor.ShouldBe(0L);
        line.CgstMinor.ShouldBe(9_000_000L);
        line.SgstMinor.ShouldBe(9_000_000L);
        line.CessMinor.ShouldBe(0L);
    }

    [Fact]
    public async Task ComputeAndApply_Recomputation_IsRefusedNotOverwritten()
    {
        var invoice = await ArrangeInvoiceAsync();
        await _service.ComputeAndApplyAsync(invoice);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await _service.ComputeAndApplyAsync(invoice));
    }

    // ---------------------------------------------------------------- round-off routing

    [Fact]
    public async Task ComputeAndApply_PaiseTotal_RoutesRoundOffToDesignatedLedger()
    {
        // ₹117.00 taxable at 18% = ₹21.06 tax → exact total ₹138.06 → rounds down to ₹138,
        // difference −₹0.06 routed to the round-off ledger, never dropped.
        var invoice = await ArrangeInvoiceAsync(itemName: "Capacitor", rate: 117m);

        var settings = new GstSettings(invoice.CompanyId, "Round Off");
        await _unitOfWork.Repository<GstSettings>().AddAsync(settings);

        var result = await _service.ComputeAndApplyAsync(invoice);

        result.InvoiceValueMinor.ShouldBe(1_380_600L);
        result.RoundedInvoiceValueMinor.ShouldBe(1_380_000L);
        result.RoundOffMinor.ShouldBe(-600L);
        result.RoundOffLedgerName.ShouldBe("Round Off");
    }

    // ---------------------------------------------------------------- reverse charge

    [Fact]
    public async Task ComputeAndApply_ReverseChargeSupply_DoesNotBillParty()
    {
        // Self-assessed: nothing charged to the party; the posting layer books the heads itself.
        var invoice = await ArrangeInvoiceAsync(isReverseCharge: true);

        var result = await _service.ComputeAndApplyAsync(invoice);

        result.IsReverseCharge.ShouldBeTrue();
        result.TotalTaxMinor.ShouldBe(0L);
        result.InvoiceValueMinor.ShouldBe(result.TotalTaxableValueMinor);
    }

    // ---------------------------------------------------------------- unconfigured items

    [Fact]
    public async Task ComputeAndApply_UnconfiguredItem_ComputesZeroWithWarning()
    {
        // No HSN on the item: the invoice still computes — at zero — with a visible warning.
        var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29", "29ABCDE1234F1Z5");
        await _unitOfWork.Repository<Company>().AddAsync(company);

        var unit = new UnitOfMeasure(company.Id, "NOS", 0, "Numbers");
        var item = new Item(company.Id, "Un-coded Fitting", unit.Id);
        await _unitOfWork.Repository<Item>().AddAsync(item);

        var document = new TradeDocument(
            company.Id,
            TradeDocumentKind.TaxInvoice,
            new DateOnly(2026, 6, 15),
            Guid.NewGuid(),
            narration: null);
        document.AddLine(item.Id, Guid.NewGuid(), 1m, 500m, 0m, isSerialTracked: false);

        var result = await _service.ComputeAndApplyAsync(document);

        result.TotalTaxMinor.ShouldBe(0L);
        result.Warnings.ShouldContain(warning => warning.Contains("no HSN/SAC code"));
    }
}
