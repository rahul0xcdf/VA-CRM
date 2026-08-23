namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// The eight core voucher behaviours (project.md §4.1).
/// </summary>
/// <remarks>
/// The <i>kind</i> is an enum because posting rules are code, not data: a Contra must move value
/// between cash and bank, a Receipt must debit a cash or bank ledger. The voucher <i>type</i> is a
/// data row, so the owner can run "Cash Payment" and "Bank Payment" as separate types with separate
/// numbering while both behave as <see cref="Payment"/>.
/// </remarks>
public enum VoucherKind
{
    Payment = 1,
    Receipt = 2,
    Contra = 3,
    Journal = 4,
    Sales = 5,
    Purchase = 6,
    CreditNote = 7,
    DebitNote = 8,
}
