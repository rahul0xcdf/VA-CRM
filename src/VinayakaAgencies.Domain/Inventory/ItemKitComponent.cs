using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// One component of a kit item: issuing the kit depletes the component by
/// <see cref="Quantity"/> base units per kit (phase-2-inventory.md §2, basic BOM only).
/// </summary>
/// <remarks>
/// No multi-level manufacturing, no work orders, no routing — a kit is a sale-time convenience,
/// so its components are plain references to other items and cycles are refused at the service
/// boundary where both sides of the edge are visible.
/// </remarks>
public sealed class ItemKitComponent : Entity, ICreationStamped, IBelongsToAggregate
{
    private ItemKitComponent()
    {
        // EF Core materialisation.
    }

    public ItemKitComponent(Guid kitItemId, Guid componentItemId, decimal quantity)
    {
        if (kitItemId == Guid.Empty)
        {
            throw new ArgumentException("KitItemId must not be empty.", nameof(kitItemId));
        }

        if (componentItemId == Guid.Empty)
        {
            throw new ArgumentException("ComponentItemId must not be empty.", nameof(componentItemId));
        }

        if (kitItemId == componentItemId)
        {
            throw new ArgumentException(
                "A kit cannot contain itself as a component.", nameof(componentItemId));
        }

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "A kit component quantity must be positive.");
        }

        KitItemId = kitItemId;
        ComponentItemId = componentItemId;

        // Minor units (ADR-0003): persisted scaled so the column is INTEGER, never TEXT.
        var roundedQuantity = MoneyScale.Round(quantity);

        if (roundedQuantity != quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"A kit component quantity carries more than {MoneyScale.Places} decimal places.");
        }

        QuantityMinor = MoneyScale.ToMinor(roundedQuantity);
    }

    public Guid KitItemId { get; private set; }

    public Guid ComponentItemId { get; private set; }

    /// <summary>
    /// Base units of the component consumed per one kit issued — as a domain decimal, computed
    /// from <see cref="QuantityMinor"/> and never a column (ADR-0003).
    /// </summary>
    public decimal Quantity => MoneyScale.FromMinor(QuantityMinor);

    /// <summary>Base units per kit, in minor units of 1/10,000 (ADR-0003).</summary>
    public long QuantityMinor { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AggregateType => nameof(Item);

    public string AggregateId => KitItemId.ToString();
}
