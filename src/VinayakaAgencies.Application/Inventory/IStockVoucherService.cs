using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>
/// Posts manual inventory documents — opening balances, receipts, issues and inter-godown
/// transfers — through one atomic path (phase-2-inventory.md §2).
/// </summary>
/// <remarks>
/// Posting validates first, values the issue side against the ledger as it stood just before the
/// voucher, writes the accounting voucher through the Phase 1 journal engine, appends the unified
/// stock-ledger rows and moves serials through their lifecycle — all inside one unit-of-work
/// transaction, so a failure midway leaves neither books nor stock touched. This is the same path
/// Phase 3's invoices will drive, minus the document chain.
/// </remarks>
public interface IStockVoucherService
{
    /// <summary>
    /// Validates without posting. Errors block posting outright; warnings are the approved
    /// negative-stock policy (<c>open-questions.md</c> #20) shown to the operator for confirmation.
    /// </summary>
    Task<StockValidationResult> ValidateAsync(
        PostStockVoucherRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts the voucher. Refuses when validation errors exist; refuses on warnings unless the
    /// caller confirms them (the UI passes <c>ConfirmNegativeStockWarnings</c> after showing the
    /// dialog). Serial availability is never a warning: an unavailable serial always blocks.
    /// </summary>
    Task<PostedStockVoucherSummary> PostAsync(
        PostStockVoucherRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>The most recent stock vouchers, newest first.</summary>
    Task<IReadOnlyList<PostedStockVoucherSummary>> ListRecentAsync(
        int take = 100,
        CancellationToken cancellationToken = default);
}
