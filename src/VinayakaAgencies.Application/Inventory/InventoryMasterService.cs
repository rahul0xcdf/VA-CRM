using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Inventory;

/// <summary>
/// Maintains the inventory masters through the unit of work, so every create, change and
/// deactivate lands in the edit log like every accounting master (phase-2-inventory.md §1).
/// </summary>
/// <remarks>
/// Hard delete follows the chart-of-accounts rule (spec §4.6): available only while nothing in the
/// books references the row. An item with ledger history, a group holding items, or a unit an item
/// uses can only be deactivated — the foreign keys backstop what the checks here express.
/// </remarks>
public sealed class InventoryMasterService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : IInventoryMasterService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockGroupSummary>> ListStockGroupsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var groups = await _unitOfWork.Repository<StockGroup>()
            .ListAsync(new CompanyStockGroups(companyId), cancellationToken)
            .ConfigureAwait(false);

        return groups
            .Where(group => includeInactive || group.IsActive)
            .OrderBy(group => group.NameNormalised, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<StockGroupSummary> CreateStockGroupAsync(
        CreateStockGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        await RequireUniqueGroupNameAsync(companyId, request.Name, null, cancellationToken)
            .ConfigureAwait(false);

        var group = new StockGroup(companyId, request.Name, request.Description);
        await _unitOfWork.Repository<StockGroup>().AddAsync(group, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(group);
    }

    /// <inheritdoc />
    public async Task<StockGroupSummary> UpdateStockGroupAsync(
        UpdateStockGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var group = await RequireStockGroupAsync(companyId, request.Id, cancellationToken)
            .ConfigureAwait(false);

        await RequireUniqueGroupNameAsync(companyId, request.Name, group.Id, cancellationToken)
            .ConfigureAwait(false);

        group.Rename(request.Name);
        group.SetDescription(request.Description);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(group);
    }

    /// <inheritdoc />
    public async Task DeactivateStockGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireStockGroupAsync(companyId, id, cancellationToken).ConfigureAwait(false))
            .Deactivate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateStockGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireStockGroupAsync(companyId, id, cancellationToken).ConfigureAwait(false))
            .Activate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteStockGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var group = await RequireStockGroupAsync(companyId, id, cancellationToken).ConfigureAwait(false);

        if (await _unitOfWork.Repository<Item>()
                .AnyAsync(new ItemsInStockGroup(id), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{group.Name}' still has items; move or deactivate them before deleting the group.");
        }

        _unitOfWork.Repository<StockGroup>().Remove(group);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GodownSummary>> ListGodownsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var godowns = await _unitOfWork.Repository<Godown>()
            .ListAsync(new CompanyGodowns(companyId), cancellationToken)
            .ConfigureAwait(false);

        return godowns
            .Where(godown => includeInactive || godown.IsActive)
            .OrderBy(godown => godown.NameNormalised, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<GodownSummary> CreateGodownAsync(
        CreateGodownRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        await RequireUniqueGodownNameAsync(companyId, request.Name, null, cancellationToken)
            .ConfigureAwait(false);

        var godown = new Godown(companyId, request.Name, request.Address);
        await _unitOfWork.Repository<Godown>().AddAsync(godown, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(godown);
    }

    /// <inheritdoc />
    public async Task<GodownSummary> UpdateGodownAsync(
        UpdateGodownRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var godown = await RequireGodownAsync(companyId, request.Id, cancellationToken)
            .ConfigureAwait(false);

        await RequireUniqueGodownNameAsync(companyId, request.Name, godown.Id, cancellationToken)
            .ConfigureAwait(false);

        godown.Rename(request.Name);
        godown.SetAddress(request.Address);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(godown);
    }

    /// <inheritdoc />
    public async Task DeactivateGodownAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireGodownAsync(companyId, id, cancellationToken).ConfigureAwait(false)).Deactivate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateGodownAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireGodownAsync(companyId, id, cancellationToken).ConfigureAwait(false)).Activate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var units = await _unitOfWork.Repository<UnitOfMeasure>()
            .ListAsync(new CompanyUnits(companyId), cancellationToken)
            .ConfigureAwait(false);

        return units
            .Where(unit => includeInactive || unit.IsActive)
            .OrderBy(unit => unit.CodeNormalised, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<UnitOfMeasureSummary> CreateUnitAsync(
        CreateUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var normalised = Domain.Common.NameNormaliser.Normalise(request.Code);

        if (await _unitOfWork.Repository<UnitOfMeasure>()
                .AnyAsync(new UnitCodeExists(companyId, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"A unit coded '{request.Code}' already exists.");
        }

        var unit = new UnitOfMeasure(companyId, request.Code, request.DecimalPlaces, request.Description);
        await _unitOfWork.Repository<UnitOfMeasure>().AddAsync(unit, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(unit);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemSummary>> ListItemsAsync(
        string? filterText = null,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var items = await _unitOfWork.Repository<Item>()
            .ListAsync(new CompanyItems(companyId), cancellationToken)
            .ConfigureAwait(false);

        var groups = (await ListStockGroupsAsync(includeInactive: true, cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(group => group.Id, group => group.Name);

        var units = (await ListUnitsAsync(includeInactive: true, cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(unit => unit.Id, unit => unit.Code);

        IEnumerable<Item> filtered = items;

        if (!string.IsNullOrWhiteSpace(filterText))
        {
            // In-memory contains over the loaded page: master screens hold one company's catalogue
            // and the high-SKU lookup path is SearchItemsAsync, which runs in SQL.
            filtered = filtered.Where(item =>
                item.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                || (item.Code?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return filtered
            .Where(item => includeInactive || item.IsActive)
            .OrderBy(item => item.NameNormalised, StringComparer.Ordinal)
            .Select(item => ToSummary(item, groups, units))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ItemSummary?> GetItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var item = await _unitOfWork.Repository<Item>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (item is null || item.CompanyId != companyId)
        {
            return null;
        }

        var groups = (await ListStockGroupsAsync(includeInactive: true, cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(group => group.Id, group => group.Name);
        var units = (await ListUnitsAsync(includeInactive: true, cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(unit => unit.Id, unit => unit.Code);

        return ToSummary(item, groups, units);
    }

    /// <inheritdoc />
    public async Task<ItemSummary> SaveItemAsync(
        SaveItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var repository = _unitOfWork.Repository<Item>();

        if (request.IsSerialTracked && request.IsBatchTracked)
        {
            throw new InvalidOperationException(
                "An item cannot be both serial-tracked and batch-tracked.");
        }

        Item item;

        if (request.Id.HasValue)
        {
            item = await RequireItemAsync(companyId, request.Id.Value, cancellationToken)
                .ConfigureAwait(false);

            await RequireUniqueItemNameAsync(companyId, request.Name, item.Id, cancellationToken)
                .ConfigureAwait(false);

            item.Rename(request.Name);
            item.MoveToGroup(request.StockGroupId);
            item.SetCode(request.Code);
            item.SetBarcode(request.Barcode);
            item.SetUnits(request.BaseUnitId, request.AlternateUnitId, request.ConversionFactor);
            item.SetTracking(request.IsSerialTracked, request.IsBatchTracked);
            item.SetValuationOverride(request.ValuationOverride);
            item.SetReorderLevel(request.ReorderLevel);
            item.AssignHsnSac(request.HsnSacId);

            if (request.IsKit)
            {
                item.MarkAsKit();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RequireUniqueItemNameAsync(companyId, request.Name, null, cancellationToken)
                .ConfigureAwait(false);

            item = new Item(
                companyId,
                request.Name,
                request.BaseUnitId,
                request.StockGroupId,
                request.Code,
                request.Barcode);
            item.SetUnits(request.BaseUnitId, request.AlternateUnitId, request.ConversionFactor);
            item.SetTracking(request.IsSerialTracked, request.IsBatchTracked);
            item.SetValuationOverride(request.ValuationOverride);
            item.SetReorderLevel(request.ReorderLevel);
            item.AssignHsnSac(request.HsnSacId);

            if (request.IsKit)
            {
                item.MarkAsKit();
            }

            await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetItemAsync(item.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The saved item could not be read back.");
    }

    /// <inheritdoc />
    public async Task DeactivateItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireItemAsync(companyId, id, cancellationToken).ConfigureAwait(false)).Deactivate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        (await RequireItemAsync(companyId, id, cancellationToken).ConfigureAwait(false)).Activate();

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var item = await RequireItemAsync(companyId, id, cancellationToken).ConfigureAwait(false);

        if (await _unitOfWork.Repository<StockLedgerEntry>()
                .AnyAsync(new LedgerEntriesOfItem(id), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{item.Name}' has stock movements and cannot be deleted; deactivate it instead. "
                + "History stays complete.");
        }

        if (await _unitOfWork.Repository<ItemKitComponent>()
                .AnyAsync(new KitComponentsReferencing(id), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{item.Name}' is used as a kit component and cannot be deleted; deactivate it instead.");
        }

        // A kit's component rows point at it, and the model has no cascades (§5): they are removed
        // explicitly, in the same save, so the audit trail shows exactly what went with the kit.
        foreach (var component in await _unitOfWork.Repository<ItemKitComponent>()
                     .ListAsync(new ComponentsOfKit(id), cancellationToken)
                     .ConfigureAwait(false))
        {
            _unitOfWork.Repository<ItemKitComponent>().Remove(component);
        }

        _unitOfWork.Repository<Item>().Remove(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KitComponentLine>> ListKitComponentsAsync(
        Guid kitItemId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        await RequireItemAsync(companyId, kitItemId, cancellationToken).ConfigureAwait(false);

        var components = await _unitOfWork.Repository<ItemKitComponent>()
            .ListAsync(new ComponentsOfKit(kitItemId), cancellationToken)
            .ConfigureAwait(false);

        var names = (await _unitOfWork.Repository<Item>()
                .ListAsync(new CompanyItems(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(item => item.Id, item => item.Name);

        return components
            .OrderBy(component => names.GetValueOrDefault(component.ComponentItemId, "?"), StringComparer.Ordinal)
            .Select(component => new KitComponentLine(
                component.ComponentItemId,
                names.GetValueOrDefault(component.ComponentItemId, "?"),
                component.Quantity))
            .ToList();
    }

    /// <inheritdoc />
    public async Task SetKitComponentsAsync(
        Guid kitItemId,
        IReadOnlyList<(Guid ComponentItemId, decimal Quantity)> components,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(components);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var kit = await RequireItemAsync(companyId, kitItemId, cancellationToken).ConfigureAwait(false);

        if (!kit.IsKit)
        {
            throw new InvalidOperationException($"'{kit.Name}' is not marked as a kit item.");
        }

        var seen = new HashSet<Guid>();
        foreach (var (componentItemId, _) in components)
        {
            if (componentItemId == kitItemId)
            {
                throw new InvalidOperationException("A kit cannot contain itself as a component.");
            }

            if (!seen.Add(componentItemId))
            {
                throw new InvalidOperationException(
                    "The same component appears more than once in the kit's component list.");
            }
        }

        var componentItems = await _unitOfWork.Repository<Item>()
            .ListAsync(new CompanyItems(companyId), cancellationToken)
            .ConfigureAwait(false);
        var byId = componentItems.ToDictionary(item => item.Id);

        foreach (var (componentItemId, _) in components)
        {
            if (!byId.TryGetValue(componentItemId, out var component))
            {
                throw new InvalidOperationException(
                    $"Kit component '{componentItemId}' does not exist in this company's books.");
            }

            if (component.IsKit)
            {
                throw new InvalidOperationException(
                    $"'{component.Name}' is itself a kit. Kits cannot nest in this phase — list its "
                    + "components directly on the outer kit instead.");
            }
        }

        var repository = _unitOfWork.Repository<ItemKitComponent>();

        foreach (var existing in await repository
                     .ListAsync(new ComponentsOfKit(kitItemId), cancellationToken)
                     .ConfigureAwait(false))
        {
            repository.Remove(existing);
        }

        foreach (var (componentItemId, quantity) in components)
        {
            await repository
                .AddAsync(new ItemKitComponent(kitItemId, componentItemId, quantity), cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static StockGroupSummary ToSummary(StockGroup group) =>
        new(group.Id, group.Name, group.Description, group.IsActive);

    private static GodownSummary ToSummary(Godown godown) =>
        new(godown.Id, godown.Name, godown.Address, godown.IsSystem, godown.IsActive);

    private static UnitOfMeasureSummary ToSummary(UnitOfMeasure unit) =>
        new(unit.Id, unit.Code, unit.DecimalPlaces, unit.Description, unit.IsActive);

    private static ItemSummary ToSummary(
        Item item,
        IReadOnlyDictionary<Guid, string> groupsById,
        IReadOnlyDictionary<Guid, string> unitsById) =>
        new(
            item.Id,
            item.Name,
            item.Code,
            item.Barcode,
            item.StockGroupId,
            item.StockGroupId.HasValue ? groupsById.GetValueOrDefault(item.StockGroupId.Value) : null,
            item.BaseUnitId,
            unitsById.GetValueOrDefault(item.BaseUnitId, "?"),
            item.AlternateUnitId,
            item.AlternateUnitId.HasValue ? unitsById.GetValueOrDefault(item.AlternateUnitId.Value) : null,
            item.ConversionFactor,
            item.IsSerialTracked,
            item.IsBatchTracked,
            item.ValuationOverride,
            item.ReorderLevel,
            item.IsKit,
            item.HsnSacId,
            item.IsActive);

    private async Task<StockGroup> RequireStockGroupAsync(
        Guid companyId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.Repository<StockGroup>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (group is null || group.CompanyId != companyId)
        {
            throw new InvalidOperationException("That stock group does not exist in this company's books.");
        }

        return group;
    }

    private async Task<Godown> RequireGodownAsync(
        Guid companyId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var godown = await _unitOfWork.Repository<Godown>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (godown is null || godown.CompanyId != companyId)
        {
            throw new InvalidOperationException("That godown does not exist in this company's books.");
        }

        return godown;
    }

    private async Task<Item> RequireItemAsync(
        Guid companyId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Repository<Item>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (item is null || item.CompanyId != companyId)
        {
            throw new InvalidOperationException("That item does not exist in this company's books.");
        }

        return item;
    }

    private async Task RequireUniqueGroupNameAsync(
        Guid companyId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var normalised = Domain.Common.NameNormaliser.Normalise(name);

        var clash = await _unitOfWork.Repository<StockGroup>()
            .SingleOrDefaultAsync(new StockGroupNameExists(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (clash is not null && clash.Id != exceptId)
        {
            throw new InvalidOperationException($"A stock group named '{name}' already exists.");
        }
    }

    private async Task RequireUniqueGodownNameAsync(
        Guid companyId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var normalised = Domain.Common.NameNormaliser.Normalise(name);

        var clash = await _unitOfWork.Repository<Godown>()
            .SingleOrDefaultAsync(new GodownNameExists(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (clash is not null && clash.Id != exceptId)
        {
            throw new InvalidOperationException($"A godown named '{name}' already exists.");
        }
    }

    private async Task RequireUniqueItemNameAsync(
        Guid companyId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var normalised = Domain.Common.NameNormaliser.Normalise(name);

        var clash = await _unitOfWork.Repository<Item>()
            .SingleOrDefaultAsync(new ItemNameExists(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        if (clash is not null && clash.Id != exceptId)
        {
            throw new InvalidOperationException($"An item named '{name}' already exists.");
        }
    }

    private sealed class CompanyStockGroups : Specification<StockGroup>
    {
        public CompanyStockGroups(Guid companyId)
        {
            Where(group => group.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class StockGroupNameExists : Specification<StockGroup>
    {
        public StockGroupNameExists(Guid companyId, string nameNormalised)
        {
            Where(group => group.CompanyId == companyId && group.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class CompanyGodowns : Specification<Godown>
    {
        public CompanyGodowns(Guid companyId)
        {
            Where(godown => godown.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class GodownNameExists : Specification<Godown>
    {
        public GodownNameExists(Guid companyId, string nameNormalised)
        {
            Where(godown => godown.CompanyId == companyId && godown.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class CompanyUnits : Specification<UnitOfMeasure>
    {
        public CompanyUnits(Guid companyId)
        {
            Where(unit => unit.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class UnitCodeExists : Specification<UnitOfMeasure>
    {
        public UnitCodeExists(Guid companyId, string codeNormalised)
        {
            Where(unit => unit.CompanyId == companyId && unit.CodeNormalised == codeNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class CompanyItems : Specification<Item>
    {
        public CompanyItems(Guid companyId)
        {
            Where(item => item.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class ItemNameExists : Specification<Item>
    {
        public ItemNameExists(Guid companyId, string nameNormalised)
        {
            Where(item => item.CompanyId == companyId && item.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class ItemsInStockGroup : Specification<Item>
    {
        public ItemsInStockGroup(Guid stockGroupId)
        {
            Where(item => item.StockGroupId == stockGroupId);
            ApplyNoTracking();
        }
    }

    private sealed class LedgerEntriesOfItem : Specification<StockLedgerEntry>
    {
        public LedgerEntriesOfItem(Guid itemId)
        {
            Where(entry => entry.ItemId == itemId);
            ApplyNoTracking();
        }
    }

    private sealed class KitComponentsReferencing : Specification<ItemKitComponent>
    {
        public KitComponentsReferencing(Guid componentItemId)
        {
            Where(component => component.ComponentItemId == componentItemId);
            ApplyNoTracking();
        }
    }

    private sealed class ComponentsOfKit : Specification<ItemKitComponent>
    {
        public ComponentsOfKit(Guid kitItemId)
        {
            Where(component => component.KitItemId == kitItemId);
            ApplyNoTracking();
        }
    }
}
