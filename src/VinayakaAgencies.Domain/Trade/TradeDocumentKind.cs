namespace VinayakaAgencies.Domain.Trade;

/// <summary>
/// The seven documents of the sales and purchase pipelines
/// (phase-3-sales-purchase-pipeline.md §2).
/// </summary>
/// <remarks>
/// The chain mirrors Tally's own pattern: quotation → sales order → delivery challan → tax invoice,
/// and its purchase mirror purchase order → goods receipt → purchase invoice. Every kind carries
/// its own numbering series (§4a decision 2), and exactly two kinds post to the books and the stock
/// ledger — <see cref="TaxInvoice"/> and <see cref="PurchaseInvoice"/> — which keeps the atomicity
/// checkpoint C3.3's surface minimal and the stock-ledger-to-control-account reconciliation
/// automatic rather than maintained.
/// </remarks>
public enum TradeDocumentKind
{
    /// <summary>An estimate. Touches no ledger and no stock, ever (checkpoint C3.5).</summary>
    Quotation = 1,

    /// <summary>A confirmed order; tracks pending quantity against its conversions.</summary>
    SalesOrder = 2,

    /// <summary>A staged delivery. Data-only: it carries godown and quantity forward.</summary>
    DeliveryChallan = 3,

    /// <summary>The posting sales document: journal lines and stock issues, atomically.</summary>
    TaxInvoice = 4,

    /// <summary>A confirmed purchase order; tracks pending quantity against its receipts.</summary>
    PurchaseOrder = 5,

    /// <summary>A record of what physically arrived. Data-only, like the challan (§4a).</summary>
    GoodsReceipt = 6,

    /// <summary>The posting purchase document: journal lines and stock receipts, atomically.</summary>
    PurchaseInvoice = 7,
}
