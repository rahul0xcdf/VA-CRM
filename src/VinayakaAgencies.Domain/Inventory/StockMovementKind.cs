namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// The movement kinds a stock ledger entry can carry (phase-2-inventory.md §2).
/// </summary>
/// <remarks>
/// One unified stock ledger (<c>open-questions.md</c> #1): every module writes rows carrying one of
/// these kinds, and no module keeps its own stock table or cached quantity. A transfer writes a
/// <see cref="TransferOut"/> row against the source godown and a <see cref="TransferIn"/> row
/// against the destination, so on-hand by godown is always a plain sum over the ledger.
/// </remarks>
public enum StockMovementKind
{
    OpeningBalance = 1,

    Receipt = 2,

    Issue = 3,

    TransferOut = 4,

    TransferIn = 5,

    /// <summary>Stock issued to a customer by a posted tax invoice (phase-3 §2).</summary>
    Sale = 6,

    /// <summary>Stock received from a supplier by a posted purchase invoice (phase-3 §2).</summary>
    Purchase = 7,

    /// <summary>The reversal row appended when a tax invoice is cancelled — the goods come back.</summary>
    SaleCancellation = 8,

    /// <summary>The reversal row appended when a purchase invoice is cancelled — the goods go back out.</summary>
    PurchaseCancellation = 9,
}
