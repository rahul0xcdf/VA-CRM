namespace VinayakaAgencies.Application.Tds;

/// <summary>
/// Computes and posts tax deducted at source (phase-7-tds-payroll-banking.md §2, checkpoints C7.1,
/// C7.3, C7.4 and C7.6).
/// </summary>
/// <remarks>
/// Composes the Phase 1 journal engine rather than writing journal lines itself, exactly as
/// <c>IStockVoucherService</c> does: a deduction reaches the books through
/// <c>IVoucherPostingService</c> or it does not reach them at all.
/// </remarks>
public interface ITaxDeductionService
{
    /// <summary>
    /// What would be deducted from this payment, without posting. Safe to call on every keystroke.
    /// </summary>
    Task<TaxDeductionPreview> ComputeAsync(
        ComputeTaxDeductionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a voucher that withholds tax: expense debits, the party credited net, the deduction
    /// credited to the TDS payable ledger, and the snapshot row — all in one transaction.
    /// </summary>
    Task<PostedDeductingVoucherSummary> PostAsync(
        PostDeductingVoucherRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>The deduction register for a period, newest first.</summary>
    Task<IReadOnlyList<TaxDeductionRow>> ListAsync(
        DateOnly fromDate,
        DateOnly toDate,
        Guid? taxSectionId = null,
        Guid? ledgerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>What must be deposited per section for the period — the challan figures.</summary>
    Task<IReadOnlyList<TaxChallanLine>> ChallanSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether tax collected at source applies to a sale to this buyer, and under which section.
    /// </summary>
    /// <remarks>
    /// Returns null whenever collection must not happen — the flag is off, no section is configured,
    /// or the buyer has declared they deduct on the purchase side, which is the mutual exclusion
    /// checkpoint C7.6 pins.
    /// </remarks>
    Task<Guid?> ResolveSaleOfGoodsCollectionSectionAsync(
        Guid customerLedgerId,
        CancellationToken cancellationToken = default);
}
