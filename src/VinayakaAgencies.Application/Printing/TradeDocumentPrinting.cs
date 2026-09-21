using VinayakaAgencies.Domain.Printing;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Printing;

/// <summary>
/// Maps a pipeline document to the kind of layout it prints with
/// (phase-7-tds-payroll-banking.md §4a D4).
/// </summary>
/// <remarks>
/// <para>
/// The seam between two enums that were one until Phase 7 needed a printable document that is not a
/// trade document (see <see cref="PrintDocumentKind"/>). It is an explicit switch rather than a cast
/// or a name lookup, because the two enums are now free to diverge — a trade kind that should not be
/// printable, or a printable kind with no trade document behind it, is exactly the case a cast would
/// wave through and this will not compile without.
/// </para>
/// <para>
/// There is deliberately no reverse mapping. <see cref="PrintDocumentKind.Cheque"/> has no trade
/// document, so the inverse would be partial, and a partial function here would push a null check
/// into every caller to serve one value.
/// </para>
/// </remarks>
public static class TradeDocumentPrinting
{
    /// <summary>The layout kind that prints <paramref name="kind"/>.</summary>
    public static PrintDocumentKind ToPrintKind(this TradeDocumentKind kind) => kind switch
    {
        TradeDocumentKind.Quotation => PrintDocumentKind.Quotation,
        TradeDocumentKind.SalesOrder => PrintDocumentKind.SalesOrder,
        TradeDocumentKind.DeliveryChallan => PrintDocumentKind.DeliveryChallan,
        TradeDocumentKind.TaxInvoice => PrintDocumentKind.TaxInvoice,
        TradeDocumentKind.PurchaseOrder => PrintDocumentKind.PurchaseOrder,
        TradeDocumentKind.GoodsReceipt => PrintDocumentKind.GoodsReceipt,
        TradeDocumentKind.PurchaseInvoice => PrintDocumentKind.PurchaseInvoice,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "This trade document has no print layout kind."),
    };
}
