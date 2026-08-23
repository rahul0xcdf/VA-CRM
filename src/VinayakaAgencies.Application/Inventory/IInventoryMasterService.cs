namespace VinayakaAgencies.Application.Inventory;

/// <summary>
/// Maintains the inventory masters: stock groups, godowns, units of measure and items
/// (phase-2-inventory.md §1).
/// </summary>
public interface IInventoryMasterService
{
    Task<IReadOnlyList<StockGroupSummary>> ListStockGroupsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<StockGroupSummary> CreateStockGroupAsync(
        CreateStockGroupRequest request,
        CancellationToken cancellationToken = default);

    Task<StockGroupSummary> UpdateStockGroupAsync(
        UpdateStockGroupRequest request,
        CancellationToken cancellationToken = default);

    Task DeactivateStockGroupAsync(Guid id, CancellationToken cancellationToken = default);

    Task ActivateStockGroupAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteStockGroupAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GodownSummary>> ListGodownsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<GodownSummary> CreateGodownAsync(
        CreateGodownRequest request,
        CancellationToken cancellationToken = default);

    Task<GodownSummary> UpdateGodownAsync(
        UpdateGodownRequest request,
        CancellationToken cancellationToken = default);

    Task DeactivateGodownAsync(Guid id, CancellationToken cancellationToken = default);

    Task ActivateGodownAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<UnitOfMeasureSummary> CreateUnitAsync(
        CreateUnitRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists items with their resolved display names; the optional text filters by name or code.
    /// </summary>
    Task<IReadOnlyList<ItemSummary>> ListItemsAsync(
        string? filterText = null,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<ItemSummary?> GetItemAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ItemSummary> SaveItemAsync(
        SaveItemRequest request,
        CancellationToken cancellationToken = default);

    Task DeactivateItemAsync(Guid id, CancellationToken cancellationToken = default);

    Task ActivateItemAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteItemAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The components currently configured on a kit item.</summary>
    Task<IReadOnlyList<KitComponentLine>> ListKitComponentsAsync(
        Guid kitItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a kit's component list wholesale. A component may not be a kit itself (no
    /// multi-level BOM) and no cycle may reach back to the kit.
    /// </summary>
    Task SetKitComponentsAsync(
        Guid kitItemId,
        IReadOnlyList<(Guid ComponentItemId, decimal Quantity)> components,
        CancellationToken cancellationToken = default);
}
