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
}
