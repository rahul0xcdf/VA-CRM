using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// The named reporting queries Phase 5 adds (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Same contract as every other reporting area: <c>CompanyId</c> first parameter, business dates as
/// canonical calendar text, integer minor units out (ADR-0003), and no SQL string outside the
/// dialect layer. Returns are built exclusively from <em>posted</em> documents — the snapshot the
/// posting flow wrote — so every figure reconciles to the books by construction (checkpoint C5.3).
/// </remarks>
public static class GstQueryKeys
{
    /// <summary>
    /// Posted sales-invoice lines in a period, with document header, party GSTIN, place of supply
    /// and item HSN code resolved.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey SalesInvoiceLinesForPeriod =
        new("Gst.SalesInvoiceLines.ForPeriod");

    /// <summary>
    /// Posted purchase-invoice lines in a period, same resolution as the sales mirror.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey PurchaseInvoiceLinesForPeriod =
        new("Gst.PurchaseInvoiceLines.ForPeriod");

    /// <summary>
    /// Counts of tax invoices raised in a period by document status, for the GSTR-1 documents-issued
    /// summary (cancelled invoices included in their own bucket).
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey TaxInvoiceStatusCountsForPeriod =
        new("Gst.TaxInvoiceStatusCounts.ForPeriod");

    /// <summary>
    /// The posted tax-invoice numbers in a period, for the documents-issued from/to range.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey SalesInvoiceNumbersForPeriod =
        new("Gst.SalesInvoiceNumbers.ForPeriod");

    /// <summary>Every key this phase declares.</summary>
    public static IReadOnlyList<SqlQueryKey> All { get; } =
    [
        SalesInvoiceLinesForPeriod,
        PurchaseInvoiceLinesForPeriod,
        TaxInvoiceStatusCountsForPeriod,
        SalesInvoiceNumbersForPeriod,
    ];
}
