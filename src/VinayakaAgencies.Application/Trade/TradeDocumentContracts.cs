using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Trade;

/// <summary>
/// One editable line of a trade document being created or revised.
/// </summary>
/// <remarks>
/// The pricing-evidence trio travels with the rate (phase-4 §4a): what the pricing engine applied
/// and from which tier, plus whether the operator moved the final rate off it before saving.
/// Defaults describe a hand-keyed line — manual, not overridden, applied what was keyed — so
/// purchase-side and unpriced callers are unaffected.
/// </remarks>
public sealed record TradeDocumentLineInput(
    Guid ItemId,
    Guid? GodownId,
    decimal Quantity,
    decimal Rate,
    decimal DiscountPercent = 0m,
    string? BatchNumber = null,
    IReadOnlyList<string>? SerialNumbers = null,
    long? AppliedRateMinor = null,
    bool IsPriceOverridden = false,
    SalePriceSource PriceSource = SalePriceSource.Manual);

/// <param name="Kind">Which pipeline document this is. Immutable after creation.</param>
/// <param name="DocumentDate">Business date; Indian FY logic keys off it.</param>
/// <param name="PartyLedgerId">Customer ledger (sales side) or supplier ledger (purchase side).</param>
/// <param name="ConfirmNegativeStockWarnings">
/// Posting-side flag: a tax invoice that would take stock below zero refuses to post until the
/// operator confirms, exactly like a manual stock voucher.
/// </param>
public sealed record SaveTradeDocumentRequest(
    TradeDocumentKind Kind,
    DateOnly DocumentDate,
    Guid PartyLedgerId,
    IReadOnlyList<TradeDocumentLineInput> Lines,
    string? Narration = null,
    string? PartyReferenceNumber = null,
    DateOnly? PartyReferenceDate = null,
    bool ConfirmNegativeStockWarnings = false,
    string? PartyGstin = null,
    bool IsReverseCharge = false);

/// <summary>Read model for one posted or staged line of a trade document.</summary>
/// <param name="AppliedRateMinor">
/// What the pricing engine applied when the line was priced, in minor units — null on a
/// hand-keyed line. The override evidence travels with drafts so an edit-and-save cycle never
/// silently launders an override into a manual rate (phase-4-customer-pricing.md §4a).
/// </param>
/// <param name="IsPriceOverridden">True when the final rate was moved off the applied one.</param>
/// <param name="PriceSource">Which pricing tier produced the applied rate.</param>
public sealed record TradeDocumentLineSummary(
    int LineNumber,
    Guid ItemId,
    string ItemName,
    Guid? GodownId,
    string? GodownName,
    decimal Quantity,
    decimal Rate,
    decimal DiscountPercent,
    decimal NetValue,
    string? BatchNumber,
    IReadOnlyList<string> SerialNumbers,
    long? AppliedRateMinor = null,
    bool IsPriceOverridden = false,
    SalePriceSource PriceSource = SalePriceSource.Manual);

/// <summary>Read model for a trade document, header and lines together.</summary>
public sealed record TradeDocumentSummary(
    Guid Id,
    TradeDocumentKind Kind,
    TradeDocumentStatus Status,
    string Number,
    DateOnly DocumentDate,
    Guid PartyLedgerId,
    string PartyLedgerName,
    string? Narration,
    string? PartyReferenceNumber,
    DateOnly? PartyReferenceDate,
    Guid? ConvertedFromDocumentId,
    string? ConvertedFromNumber,
    Guid? PostedVoucherId,
    string? PostedNumber,
    decimal TotalNetValue,
    IReadOnlyList<TradeDocumentLineSummary> Lines)
{
    /// <summary>The caption operators read: the number where one exists, the kind where not.</summary>
    public string DisplayNumber => string.IsNullOrWhiteSpace(Number) ? "(not yet numbered)" : Number;
}

/// <summary>Narrows a list to recent documents of the given kinds.</summary>
public sealed record TradeDocumentFilter
{
    public static readonly TradeDocumentFilter All = new();

    public IReadOnlyList<TradeDocumentKind>? Kinds { get; init; }

    public int Take { get; init; } = 100;
}

/// <summary>
/// Converts an existing document into its successor: quotation → order → challan → invoice on the
/// sales side, purchase order → goods receipt → purchase invoice on the purchase side.
/// </summary>
/// <param name="DefaultGodownId">
/// Applied to carried lines when the target needs godowns and the source had none — converting an
/// order straight into an invoice, for instance.
/// </param>
public sealed record ConvertTradeDocumentRequest(
    Guid SourceDocumentId,
    TradeDocumentKind TargetKind,
    DateOnly DocumentDate,
    Guid? DefaultGodownId = null,
    string? Narration = null,
    bool ConfirmNegativeStockWarnings = false);

/// <summary>The result of <c>ValidateInvoiceAsync</c>: blocking errors and confirmable warnings.</summary>
public sealed record InvoiceValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool HasErrors => Errors.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>The outcome of posting an invoice.</summary>
/// <param name="CostOfGoodsSoldValueMinor">What the stock issues were expensed at, minor units.</param>
/// <param name="TotalTaxMinor">GST computed and posted with this invoice, minor units
/// (phase-5-gst-returns.md §2); zero while items carry no HSN/rate.</param>
/// <param name="RoundOffMinor">The per-invoice rupee round-off routed to the round-off ledger,
/// minor units; positive adds to the bill, negative subtracts.</param>
/// <param name="TaxWarnings">
/// Non-blocking GST findings — an item without an HSN/SAC code or a covering rate schedule computes
/// at zero so the owner may still raise non-GST invoices (spec §4a).
/// </param>
public sealed record PostedInvoiceResult(
    Guid DocumentId,
    string DocumentNumber,
    string PostedVoucherNumber,
    DateOnly DocumentDate,
    decimal TotalNetValue,
    long CostOfGoodsSoldValueMinor,
    long TotalTaxMinor = 0L,
    long RoundOffMinor = 0L,
    IReadOnlyList<string>? TaxWarnings = null);

/// <summary>One pending row of the order-outstanding report (checkpoint C3.4).</summary>
public sealed record OrderOutstandingRow(
    Guid OrderId,
    string OrderNumber,
    DateOnly OrderDate,
    Guid PartyLedgerId,
    string PartyName,
    Guid ItemId,
    string ItemName,
    decimal OrderedQuantity,
    decimal PendingQuantity,
    decimal Rate,
    decimal PendingValue);

/// <summary>
/// The sales and purchase pipeline: create, edit and convert documents along the chains, post
/// invoices atomically into the journal and the stock ledger, and report what remains pending
/// (phase-3-sales-purchase-pipeline.md §1–§3).
/// </summary>
public interface ITradeDocumentService
{
    /// <summary>Creates a document, claiming its number from its kind's series inside the save.</summary>
    /// <remarks>
    /// Invoices are the exception by design: their number is claimed at posting time through the
    /// journal engine, so a draft invoice carries none until it posts.
    /// </remarks>
    Task<TradeDocumentSummary> CreateAsync(
        SaveTradeDocumentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Rewrites a still-editable document's header and replaces its whole line set.</summary>
    Task<TradeDocumentSummary> UpdateDraftAsync(
        Guid documentId,
        SaveTradeDocumentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a document that has had no effect. Anything converted-from, issued or already
    /// cancelled is refused; deletion is captured by the audit trail like every other change.
    /// </summary>
    Task DeleteAsync(Guid documentId, string reason, CancellationToken cancellationToken = default);

    Task<TradeDocumentSummary?> GetAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradeDocumentSummary>> ListAsync(
        TradeDocumentFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Carries a source document forward into its successor without re-keying (checkpoint C3.1):
    /// items, quantities, rates, discounts, godowns and terms all travel; both sides keep links so
    /// drill-down runs either way.
    /// </summary>
    /// <remarks>
    /// A conversion that consumes only some of the source's quantity leaves it open (checkpoint
    /// C3.4); the source closes automatically once every line has been carried forward.
    /// </remarks>
    Task<TradeDocumentSummary> ConvertAsync(
        ConvertTradeDocumentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Everything checkable about an invoice before it posts.</summary>
    Task<InvoiceValidationResult> ValidateInvoiceAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts an invoice: journal voucher and stock movements inside one unit-of-work transaction,
    /// or neither at all (checkpoint C3.3). Negative-stock warnings must be confirmed first.
    /// </summary>
    Task<PostedInvoiceResult> PostInvoiceAsync(
        Guid documentId,
        bool confirmNegativeStockWarnings = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses a posted invoice through the journal engine: a cancelling revision, reversing stock
    /// rows, serials returned to stock — all inside one transaction. The document keeps its number.
    /// </summary>
    Task<TradeDocumentSummary> CancelPostedInvoiceAsync(
        Guid documentId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>The sales-order outstanding report: ordered minus delivered/invoiced (C3.4).</summary>
    Task<IReadOnlyList<OrderOutstandingRow>> SalesOrderOutstandingAsync(
        CancellationToken cancellationToken = default);

    /// <summary>The purchase-order outstanding mirror.</summary>
    Task<IReadOnlyList<OrderOutstandingRow>> PurchaseOrderOutstandingAsync(
        CancellationToken cancellationToken = default);
}
