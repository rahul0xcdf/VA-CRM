namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// Builds GST returns from posted documents and reconciles them against the books and the portal
/// (phase-5-gst-returns.md §2).
/// </summary>
public interface IGstReturnService
{
    /// <summary>
    /// GSTR-1 for a period: B2B invoice list, B2C aggregates by place of supply + rate, Table 12
    /// HSN summary split B2B/B2C, documents-issued summary, grand totals. Built only from posted
    /// invoices' snapshots (checkpoint C5.1, C5.3).
    /// </summary>
    Task<Gstr1Return> BuildGstr1Async(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>GSTR-3B outward/ITC/net-tax summary; reconciles to
    /// <see cref="BuildGstr1Async"/> over the same period exactly (checkpoint C5.3).</summary>
    Task<Gstr3BSummary> BuildGstr3BAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>GSTR-9 annual summary: the monthly figures aggregated over the given range.</summary>
    Task<Gstr9AnnualSummary> BuildGstr9Async(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies an imported GSTR-2B portal JSON against this company's posted purchase invoices
    /// in a book-date range: Matched / MissingInBooks / MissingInPortal, keyed by supplier GSTIN +
    /// invoice number, with value-difference details beyond tolerance (checkpoint C5.4).
    /// </summary>
    /// <param name="importedJson">The portal-format JSON file contents.</param>
    /// <param name="valueToleranceMinor">
    /// Value difference treated as still-matched, minor units. Default ₹0.01.
    /// </param>
    Task<Gstr2BReconciliationResult> ReconcileGstr2BAsync(
        string importedJson,
        DateOnly booksFromDate,
        DateOnly booksToDate,
        long valueToleranceMinor = 100L,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Serialises the period's GSTR-1 into the offline-utility JSON shape, stamped with the pinned
    /// schema version and the export timestamp (spec §2, checkpoint C5.1). Requires the company to
    /// carry its GSTIN.
    /// </summary>
    Task<string> ExportGstr1JsonAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}
