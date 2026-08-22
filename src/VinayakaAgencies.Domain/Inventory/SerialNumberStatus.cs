namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// Where a serial-numbered unit sits in its lifecycle (phase-2-inventory.md §2).
/// </summary>
/// <remarks>
/// received → in stock → sold → (optionally) returned. A return puts the unit back into
/// <see cref="InStock"/>; the serial's row is never deleted or recreated, because the unique index
/// over its value is what makes "in stock exactly once" enforceable by the database rather than by
/// convention (checkpoints C2.2, C2.3).
/// </remarks>
public enum SerialNumberStatus
{
    InStock = 1,

    Sold = 2,
}
