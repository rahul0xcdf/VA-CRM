namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// Whether a stock ledger entry adds quantity to, or removes it from, a godown.
/// </summary>
public enum StockMovementDirection
{
    Increase = 1,

    Decrease = 2,
}
