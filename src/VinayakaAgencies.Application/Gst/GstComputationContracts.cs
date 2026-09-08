using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Gst;

/// <summary>The computed tax for one invoice line, all figures in minor units (ADR-0003).</summary>
public sealed record GstLineTaxBreakdown(
    Guid LineId,
    Guid ItemId,
    Guid? HsnSacId,
    string? HsnCode,
    long TaxableValueMinor,
    long RatePercentMinor,
    long IgstMinor,
    long CgstMinor,
    long SgstMinor,
    long CessMinor);

/// <summary>
/// The full tax computation of one invoice: place-of-supply resolution, per-line breakdowns,
/// invoice totals and the rupee round-off routed to its designated ledger
/// (phase-5-gst-returns.md §2).
/// </summary>
/// <param name="RoundedInvoiceValueMinor">
/// The grand total rounded to the nearest rupee — what the party is actually billed.
/// </param>
/// <param name="RoundOffMinor">
/// Difference between the exact and the rounded total; positive adds, negative subtracts. Posted
/// to <see cref="RoundOffLedgerName"/>, never silently dropped.
/// </param>
/// <param name="Warnings">
/// Non-blocking findings — an item with no HSN/rate computes tax at zero so the owner may raise
/// non-GST invoices while unconfigured, but the gap is surfaced, not swallowed.
/// </param>
public sealed record InvoiceTaxComputationResult(
    string PlaceOfSupplyStateCode,
    bool IsReverseCharge,
    bool IsIntraState,
    IReadOnlyList<GstLineTaxBreakdown> Lines,
    long TotalTaxableValueMinor,
    long TotalIgstMinor,
    long TotalCgstMinor,
    long TotalSgstMinor,
    long TotalCessMinor,
    long InvoiceValueMinor,
    long RoundedInvoiceValueMinor,
    long RoundOffMinor,
    string RoundOffLedgerName,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Total tax charged on the invoice (zero throughout on a reverse-charge supply).</summary>
    public long TotalTaxMinor => TotalIgstMinor + TotalCgstMinor + TotalSgstMinor + TotalCessMinor;
}

/// <summary>
/// Computes the GST snapshot for a trade document: intra-state supplies split CGST/SGST equally,
/// inter-state supplies carry IGST, compensation cess where scheduled, and reverse-charge supplies
/// self-assess instead of being billed (phase-5-gst-returns.md §2, checkpoint C5.2).
/// </summary>
/// <remarks>
/// Called once, by the invoice posting flow, which attaches the returned figures to the document
/// aggregate; returns and exports read those snapshots and never recompute.
/// </remarks>
public interface IGstComputationService
{
    /// <summary>
    /// Resolves rates effective on the document date, computes every line, stamps the snapshots
    /// onto the aggregate, and reports the round-off routing. Idempotent per aggregate: a document
    /// already carrying a snapshot is refused by the domain, not silently recomputed.
    /// </summary>
    Task<InvoiceTaxComputationResult> ComputeAndApplyAsync(
        TradeDocument document,
        CancellationToken cancellationToken = default);
}
