namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// How an item's on-hand quantity is costed (phase-2-inventory.md §2).
/// </summary>
/// <remarks>
/// The global default is FIFO (<c>open-questions.md</c> #19); a per-item override selects the other
/// method for that item alone. Both methods are implemented and each is verified against a
/// hand-computed fixture (checkpoint C2.4). Persisted by name, never by number, so reordering the
/// enum cannot silently change what an existing row means.
/// </remarks>
public enum ValuationMethod
{
    Fifo = 1,

    WeightedAverage = 2,
}
