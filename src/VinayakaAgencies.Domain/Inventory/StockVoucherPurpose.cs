namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// What a stock voucher is for (phase-2-inventory.md §1, §2).
/// </summary>
/// <remarks>
/// The purpose decides the document's validation rules and its accounting effect:
/// <see cref="OpeningBalance"/> and <see cref="Receipt"/> increase stock and debit the inventory
/// control account; <see cref="Issue"/> decreases it at valuation cost; <see cref="Transfer"/>
/// moves quantity between godowns through one out-line and one in-line per item and leaves the
 /// total stock value untouched (checkpoint C2.5). Purchases and sales arrive in Phase 3 as
/// their own documents writing to the same ledger — they are deliberately not purposes here.
/// </remarks>
public enum StockVoucherPurpose
{
    OpeningBalance = 1,

    Receipt = 2,

    Issue = 3,

    Transfer = 4,
}
