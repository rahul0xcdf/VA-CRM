namespace VinayakaAgencies.Domain.Printing;

/// <summary>
/// What a <see cref="PrintTemplate"/> lays out (phase-7-tds-payroll-banking.md §4a D4).
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 typed this as <c>TradeDocumentKind</c>, because at the time every printable document was
/// a trade document. Phase 7 adds one that is not: a cheque is a bank instrument, and §4a D4 rules
/// that its layout reuses the Phase 3 template engine rather than standing up a second template
/// system. Widening <c>TradeDocumentKind</c> to hold it would have put a bank instrument inside an
/// enum whose own documentation says "the seven documents of the sales and purchase pipelines", and
/// every pipeline <c>switch</c> would have grown an arm for a value that can never reach it.
/// </para>
/// <para>
/// <b>No data migration.</b> The column stores enum <em>names</em>, not ordinals
/// (<c>PrintTemplateConfiguration</c>), and the seven trade members keep their names and their
/// numbers here. Existing template rows read back as exactly what they were.
/// </para>
/// </remarks>
public enum PrintDocumentKind
{
    /// <summary>An estimate.</summary>
    Quotation = 1,

    /// <summary>A confirmed sales order.</summary>
    SalesOrder = 2,

    /// <summary>A staged delivery.</summary>
    DeliveryChallan = 3,

    /// <summary>The posting sales document.</summary>
    TaxInvoice = 4,

    /// <summary>A confirmed purchase order.</summary>
    PurchaseOrder = 5,

    /// <summary>A record of what physically arrived.</summary>
    GoodsReceipt = 6,

    /// <summary>The posting purchase document.</summary>
    PurchaseInvoice = 7,

    /// <summary>
    /// A bank cheque. Field positions on a CTS-2010 leaf, adjusted by the owner against a real
    /// cheque and saved as a template version like any other (§4a D4, checkpoint C7.8).
    /// </summary>
    Cheque = 100,
}
