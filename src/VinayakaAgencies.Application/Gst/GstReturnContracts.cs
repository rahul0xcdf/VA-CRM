namespace VinayakaAgencies.Application.Gst;

/// <summary>Tax figures for one rate bucket, in minor units (ADR-0003).</summary>
public sealed record GstRateLine(
    long RatePercentMinor,
    long TaxableValueMinor,
    long IgstMinor,
    long CgstMinor,
    long SgstMinor,
    long CessMinor);

/// <summary>
/// One B2B invoice of the GSTR-1 outward list: a posted tax invoice billed to a GSTIN-carrying
/// counterparty, with its per-rate lines (phase-5-gst-returns.md §2).
/// </summary>
public sealed record Gstr1B2BInvoice(
    Guid DocumentId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string CounterpartyGstin,
    string PlaceOfSupplyStateCode,
    bool IsReverseCharge,
    long InvoiceValueMinor,
    IReadOnlyList<GstRateLine> RateLines);

/// <summary>One GSTR-1 B2C aggregate: the portal expects unregistered supplies netted by place of
/// supply and rate rather than listed invoice by invoice.</summary>
public sealed record Gstr1B2CAggregate(
    string PlaceOfSupplyStateCode,
    long RatePercentMinor,
    int InvoiceCount,
    long TaxableValueMinor,
    long IgstMinor,
    long CgstMinor,
    long SgstMinor,
    long CessMinor,
    long InvoiceValueMinor);

/// <summary>One Table 12 HSN summary row, split by supply type as the portal expects.</summary>
public sealed record Gstr1HsnSummaryRow(
    string Code,
    string? Description,
    bool IsB2B,
    long QuantityMinor,
    long TaxableValueMinor,
    long IgstMinor,
    long CgstMinor,
    long SgstMinor,
    long CessMinor);

/// <summary>The documents-issued summary: invoices raised and cancelled in the period.</summary>
public sealed record Gstr1DocumentIssueSummary(
    int InvoicesIssued,
    int InvoicesCancelled,
    string FirstInvoiceNumber,
    string LastInvoiceNumber);

/// <summary>Grand totals shared by every return shape, minor units throughout.</summary>
public sealed record GstTotals(
    long TaxableValueMinor,
    long IgstMinor,
    long CgstMinor,
    long SgstMinor,
    long CessMinor)
{
    public long TotalTaxMinor => IgstMinor + CgstMinor + SgstMinor + CessMinor;

    public static GstTotals operator +(GstTotals left, GstTotals right) => new(
        left.TaxableValueMinor + right.TaxableValueMinor,
        left.IgstMinor + right.IgstMinor,
        left.CgstMinor + right.CgstMinor,
        left.SgstMinor + right.SgstMinor,
        left.CessMinor + right.CessMinor);
}

/// <summary>A built GSTR-1 period return. Exports/SEZ/e-commerce tables are deliberately absent —
/// domestic B2B + B2C only (spec §4a); sections are omitted entirely rather than emitted empty.</summary>
public sealed record Gstr1Return(
    DateOnly FromDate,
    DateOnly ToDate,
    string Gstin,
    IReadOnlyList<Gstr1B2BInvoice> B2B,
    IReadOnlyList<Gstr1B2CAggregate> B2C,
    IReadOnlyList<Gstr1HsnSummaryRow> HsnSummary,
    Gstr1DocumentIssueSummary Documents,
    GstTotals Totals);

/// <summary>
/// A built GSTR-3B period summary. Every figure derives from the same posted snapshots as
/// GSTR-1, so the two reconcile by construction (checkpoint C5.3).
/// </summary>
/// <param name="OutwardTaxableSupplies">Table 3.1(a): outward taxable supplies.</param>
/// <param name="InwardReverseChargeSupplies">
/// Table 3.1(d): inward supplies on which tax is payable on reverse charge — self-assessed from
/// the purchase snapshots.
/// </param>
/// <param name="ItcClaimed">Table 4(A): input tax credit claimed from non-reverse-charge purchases.</param>
public sealed record Gstr3BSummary(
    DateOnly FromDate,
    DateOnly ToDate,
    string Gstin,
    GstTotals OutwardTaxableSupplies,
    GstTotals InwardReverseChargeSupplies,
    GstTotals ItcClaimed,
    GstTotals NetTaxPayable)
{
    /// <summary>Whether the 3B outward block agrees with the same period's GSTR-1 totals exactly.</summary>
    public bool ReconcilesTo(Gstr1Return gstr1) =>
        OutwardTaxableSupplies.TaxableValueMinor == gstr1.Totals.TaxableValueMinor
        && OutwardTaxableSupplies.IgstMinor == gstr1.Totals.IgstMinor
        && OutwardTaxableSupplies.CgstMinor == gstr1.Totals.CgstMinor
        && OutwardTaxableSupplies.SgstMinor == gstr1.Totals.SgstMinor
        && OutwardTaxableSupplies.CessMinor == gstr1.Totals.CessMinor;
}

/// <summary>One month's figures inside a GSTR-9 annual summary.</summary>
public sealed record Gstr9MonthRow(
    int Year,
    int Month,
    GstTotals OutwardTaxableSupplies,
    GstTotals ItcClaimed,
    GstTotals NetTaxPayable);

/// <summary>The annual summary aggregating the monthly figures over a financial year (spec §2).</summary>
public sealed record Gstr9AnnualSummary(
    DateOnly FromDate,
    DateOnly ToDate,
    string Gstin,
    IReadOnlyList<Gstr9MonthRow> Months,
    GstTotals OutwardTaxableSupplies,
    GstTotals ItcClaimed,
    GstTotals NetTaxPayable);

/// <summary>How an imported portal line classified against the books' purchases (checkpoint C5.4).</summary>
public enum Gstr2bClassification
{
    /// <summary>In both, keyed by supplier GSTIN + invoice number.</summary>
    Matched,

    /// <summary>On the portal file but not among the books' posted purchase invoices.</summary>
    MissingInBooks,

    /// <summary>A books purchase invoice with no counterpart on the portal file.</summary>
    MissingInPortal,
}

/// <summary>One reconciliation outcome row.</summary>
/// <param name="MismatchDetails">
/// Human-readable differences beyond tolerance on a key match — value, taxable or tax heads that
/// disagree between portal and books. Empty means the match is clean within tolerance.
/// </param>
public sealed record Gstr2BMatchResult(
    Gstr2bClassification Classification,
    string SupplierGstin,
    string InvoiceNumber,
    DateOnly? PortalInvoiceDate,
    long? PortalInvoiceValueMinor,
    Guid? BooksDocumentId,
    string? BooksInvoiceNumber,
    DateOnly? BooksInvoiceDate,
    long? BooksInvoiceValueMinor,
    IReadOnlyList<string> MismatchDetails);

/// <summary>The full GSTR-2B reconciliation result for one comparison run.</summary>
public sealed record Gstr2BReconciliationResult(
    DateOnly BooksFromDate,
    DateOnly BooksToDate,
    IReadOnlyList<Gstr2BMatchResult> Results)
{
    public int MatchedCount { get; } = Results.Count(row => row.Classification == Gstr2bClassification.Matched);

    public int MissingInBooksCount { get; } =
        Results.Count(row => row.Classification == Gstr2bClassification.MissingInBooks);

    public int MissingInPortalCount { get; } =
        Results.Count(row => row.Classification == Gstr2bClassification.MissingInPortal);
}
