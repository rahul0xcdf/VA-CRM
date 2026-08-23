using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Domain.Tests.Trade;

/// <summary>
/// The aggregate rules the pipeline leans on: which statuses a kind may hold, whether lines carry
/// godowns, when a document may still change, and what conversion and posting record
/// (phase-3-sales-purchase-pipeline.md §2).
/// </summary>
public class TradeDocumentTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid PartyId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid GodownId = Guid.NewGuid();
    private static readonly DateOnly DocumentDate = new(2026, 8, 20);

    [Fact]
    public void Constructor_QuotationAndInvoice_StartDraft_OthersStartOpen()
    {
        // Estimates stay drafts until something converts them; invoices until they post. Orders,
        // challans and goods receipts are live from birth.
        NewDocument(TradeDocumentKind.Quotation).Status.ShouldBe(TradeDocumentStatus.Draft);
        NewDocument(TradeDocumentKind.TaxInvoice).Status.ShouldBe(TradeDocumentStatus.Draft);
        NewDocument(TradeDocumentKind.PurchaseInvoice).Status.ShouldBe(TradeDocumentStatus.Draft);
        NewDocument(TradeDocumentKind.SalesOrder).Status.ShouldBe(TradeDocumentStatus.Open);
        NewDocument(TradeDocumentKind.DeliveryChallan).Status.ShouldBe(TradeDocumentStatus.Open);
        NewDocument(TradeDocumentKind.PurchaseOrder).Status.ShouldBe(TradeDocumentStatus.Open);
        NewDocument(TradeDocumentKind.GoodsReceipt).Status.ShouldBe(TradeDocumentStatus.Open);
    }

    [Fact]
    public void Constructor_EmptyCompanyOrParty_Throws()
    {
        Should.Throw<ArgumentException>(() => new TradeDocument(
            Guid.Empty, TradeDocumentKind.Quotation, DocumentDate, PartyId, null));
        Should.Throw<ArgumentException>(() => new TradeDocument(
            CompanyId, TradeDocumentKind.Quotation, DocumentDate, Guid.Empty, null));
    }

    [Fact]
    public void AddLine_InvoiceWithoutGodown_IsRefused()
    {
        var invoice = NewDocument(TradeDocumentKind.TaxInvoice);

        var refusal = Should.Throw<InvalidOperationException>(
            () => AddPump(invoice, godownId: null));

        refusal.Message.ShouldContain("must name the godown");
    }

    [Fact]
    public void AddLine_QuotationWithGodown_IsRefused()
    {
        var quotation = NewDocument(TradeDocumentKind.Quotation);

        var refusal = Should.Throw<InvalidOperationException>(
            () => AddPump(quotation, GodownId));

        refusal.Message.ShouldContain("moves no stock");
    }

    [Fact]
    public void AddLine_SerialTrackedFractionalQuantity_IsRefused()
    {
        var order = NewDocument(TradeDocumentKind.SalesOrder);

        var refusal = Should.Throw<InvalidOperationException>(
            () => order.AddLine(ItemId, null, 1.5m, 100m, 0m, isSerialTracked: true));

        refusal.Message.ShouldContain("whole units only");
    }

    [Fact]
    public void AddLine_LinesNumberFromOne()
    {
        var order = NewDocument(TradeDocumentKind.SalesOrder);

        AddPump(order, null);
        AddPump(order, null);

        order.Lines.Select(line => line.LineNumber).ShouldBe([1, 2]);
    }

    [Fact]
    public void ResetLines_ReplacesSetAndRenumbersFromOne()
    {
        var quotation = NewDocument(TradeDocumentKind.Quotation);
        AddPump(quotation, null);
        AddPump(quotation, null);

        quotation.ResetLines(
        [
            new TradeLineInput(ItemId, null, 4m, 90m, 5m, IsSerialTracked: false),
        ]);

        quotation.Lines.Count.ShouldBe(1);
        quotation.Lines.Single().LineNumber.ShouldBe(1);
        quotation.Lines.Single().NetValue.ShouldBe(MoneyScale.Round(4m * 90m * 0.95m));
    }

    [Fact]
    public void ResetLines_IssuedInvoice_IsRefused()
    {
        var invoice = NewDocument(TradeDocumentKind.TaxInvoice);
        AddPump(invoice, GodownId);
        invoice.AttachNumber("INV/0001");
        invoice.AttachPostedVoucher(Guid.NewGuid(), "INV/0001");

        var refusal = Should.Throw<InvalidOperationException>(
            () => quotationReset(invoice));

        refusal.Message.ShouldContain("cannot be revised");

        static void quotationReset(TradeDocument document) => document.ResetLines(
        [
            new TradeLineInput(ItemId, GodownId, 1m, 100m, 0m, IsSerialTracked: false),
        ]);
    }

    [Fact]
    public void AttachNumber_SecondCall_IsRefused()
    {
        var order = NewDocument(TradeDocumentKind.SalesOrder);

        order.AttachNumber("SO/0001");

        var refusal = Should.Throw<InvalidOperationException>(
            () => order.AttachNumber("SO/0002"));

        refusal.Message.ShouldContain("already been numbered");
        order.Number.ShouldBe("SO/0001");
    }

    [Fact]
    public void AttachPostedVoucher_OnQuotation_IsRefused()
    {
        var quotation = NewDocument(TradeDocumentKind.Quotation);

        var refusal = Should.Throw<InvalidOperationException>(
            () => quotation.AttachPostedVoucher(Guid.NewGuid(), "QTN/0001"));

        refusal.Message.ShouldContain("never posts to the books");
    }

    [Fact]
    public void AttachPostedVoucher_MarksIssued_AndSecondPostIsRefused()
    {
        var invoice = NewDocument(TradeDocumentKind.PurchaseInvoice);
        AddPump(invoice, GodownId);

        invoice.AttachPostedVoucher(Guid.NewGuid(), "BILL/0001");

        invoice.Status.ShouldBe(TradeDocumentStatus.Issued);
        invoice.PostedVoucherId.ShouldNotBeNull();
        invoice.PostedNumber.ShouldBe("BILL/0001");

        Should.Throw<InvalidOperationException>(
            () => invoice.AttachPostedVoucher(Guid.NewGuid(), "BILL/0002"));
    }

    [Fact]
    public void MarkCancelled_PostedInvoiceIsRefused_DraftIsAllowed()
    {
        var issued = NewDocument(TradeDocumentKind.TaxInvoice);
        AddPump(issued, GodownId);
        issued.AttachPostedVoucher(Guid.NewGuid(), "INV/0001");

        var refusal = Should.Throw<InvalidOperationException>(() => issued.MarkCancelled());
        refusal.Message.ShouldContain("reverse it through the journal engine");

        var draft = NewDocument(TradeDocumentKind.SalesOrder);
        draft.MarkCancelled();
        draft.Status.ShouldBe(TradeDocumentStatus.Cancelled);
    }

    [Fact]
    public void MarkFulfilled_OnlyFromOpen()
    {
        var fulfilled = NewDocument(TradeDocumentKind.SalesOrder);
        fulfilled.MarkFulfilled();
        fulfilled.Status.ShouldBe(TradeDocumentStatus.Fulfilled);

        var converted = NewDocument(TradeDocumentKind.Quotation);
        converted.MarkConverted();
        Should.Throw<InvalidOperationException>(() => converted.MarkFulfilled());
    }

    [Fact]
    public void SetConvertedFrom_RecordsLinkOnce()
    {
        var successor = NewDocument(TradeDocumentKind.TaxInvoice);
        var sourceId = Guid.NewGuid();

        successor.SetConvertedFrom(sourceId, "QTN/0001");

        successor.ConvertedFromDocumentId.ShouldBe(sourceId);
        successor.ConvertedFromNumber.ShouldBe("QTN/0001");

        Should.Throw<InvalidOperationException>(
            () => successor.SetConvertedFrom(Guid.NewGuid(), "SO/0002"));
    }

    [Fact]
    public void CanEdit_DraftsAndOpenOnly()
    {
        var draft = NewDocument(TradeDocumentKind.Quotation);
        draft.CanEdit.ShouldBeTrue();

        draft.MarkConverted();
        draft.CanEdit.ShouldBeFalse();

        var open = NewDocument(TradeDocumentKind.GoodsReceipt);
        open.CanEdit.ShouldBeTrue();

        open.MarkCancelled();
        open.CanEdit.ShouldBeFalse();
    }

    [Fact]
    public void DiscountPercent_RoundTripsThroughMinorUnits_Exactly()
    {
        // The ADR-0003 regression guard at domain level: a discount of 12.34% must come back as
        // exactly 12.34% after storage in minor units — never through a TEXT decimal.
        var order = NewDocument(TradeDocumentKind.SalesOrder);
        order.AddLine(ItemId, null, 3m, 199.99m, 12.34m, isSerialTracked: false);

        var line = order.Lines.Single();
        line.DiscountPercent.ShouldBe(12.34m);
        line.DiscountPercentMinor.ShouldBe(123_400L);
    }

    private static TradeDocument NewDocument(TradeDocumentKind kind) =>
        new(CompanyId, kind, DocumentDate, PartyId, narration: null);

    private static TradeDocumentLine AddPump(TradeDocument document, Guid? godownId) =>
        document.AddLine(ItemId, godownId, 2m, 100m, 0m, isSerialTracked: false);
}
