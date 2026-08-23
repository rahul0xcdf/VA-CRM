using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Trade;

/// <summary>
/// The seed rows the sales/purchase pipeline needs before it can run: one system voucher type with
/// its numbering series per document kind (phase-3 §4a decision 2), the cost-of-goods-sold ledger,
/// and the two default tax-invoice print templates (checkpoint C3.6).
/// </summary>
/// <remarks>
/// Written at runtime through <see cref="ICompanyProvisioningService.EnsurePipelineSeedAsync"/> and
/// deliberately not through EF's <c>HasData</c>, for the same §5 reason as every other seed table in
/// this product: migration seeding bypasses <c>SaveChangesAsync</c> and therefore the audit trail.
/// </remarks>
internal static class PipelineSeedData
{
    /// <summary>The ledger stock issues are expensed against when a tax invoice posts.</summary>
    public const string CostOfGoodsSoldLedgerName = "Cost of Goods Sold";

    /// <summary>Where the COGS ledger hangs. Seeded by Phase 1, so normally present already.</summary>
    public const string CostOfGoodsSoldGroupName = "Direct Expenses";

    internal sealed record KindSeed(
        TradeDocumentKind Kind,
        string VoucherTypeName,
        VoucherKind AccountingKind,
        string Prefix);

    /// <summary>
    /// One numbering series per document kind. Only the two invoice kinds post; their accounting
    /// kinds drive the journal engine's behaviour, and the rest exist purely so every document is
    /// numbered like its siblings from day one.
    /// </summary>
    public static IReadOnlyList<KindSeed> Kinds { get; } =
    [
        new(TradeDocumentKind.Quotation, "Quotation", VoucherKind.Journal, "QTN/"),
        new(TradeDocumentKind.SalesOrder, "Sales Order", VoucherKind.Journal, "SO/"),
        new(TradeDocumentKind.DeliveryChallan, "Delivery Challan", VoucherKind.Journal, "DC/"),
        new(TradeDocumentKind.TaxInvoice, "Tax Invoice", VoucherKind.Sales, "INV/"),
        new(TradeDocumentKind.PurchaseOrder, "Purchase Order", VoucherKind.Journal, "PO/"),
        new(TradeDocumentKind.GoodsReceipt, "Goods Receipt", VoucherKind.Journal, "GRN/"),
        new(TradeDocumentKind.PurchaseInvoice, "Purchase Invoice", VoucherKind.Purchase, "BILL/"),
    ];

    public static string VoucherTypeNameFor(TradeDocumentKind kind) => kind switch
    {
        TradeDocumentKind.Quotation => "Quotation",
        TradeDocumentKind.SalesOrder => "Sales Order",
        TradeDocumentKind.DeliveryChallan => "Delivery Challan",
        TradeDocumentKind.TaxInvoice => "Tax Invoice",
        TradeDocumentKind.PurchaseOrder => "Purchase Order",
        TradeDocumentKind.GoodsReceipt => "Goods Receipt",
        TradeDocumentKind.PurchaseInvoice => "Purchase Invoice",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown trade document kind."),
    };

    /// <summary>The two default tax-invoice templates seeded for checkpoint C3.6.</summary>
    /// <remarks>
    /// The rule that picks between them is <see cref="PrintTemplate.Priority"/>: lowest active wins.
    /// "Detailed" carries the line-by-line layout; "Compact" prints a denser grid for long invoices.
    /// Both are ordinary data — an operator edits either in the template designer without a rebuild
    /// (checkpoint C3.2), and ADR-0004 records why they render through the built-in engine today.
    /// </remarks>
    public static IReadOnlyList<(string Name, int Priority, string Description)> TaxInvoiceTemplates { get; } =
    [
        ("Tax Invoice — Detailed", 10, "Default: full header, item grid and GST Rule 46 copy labels."),
        ("Tax Invoice — Compact", 20, "Denser grid for invoices with many lines."),
    ];
}
