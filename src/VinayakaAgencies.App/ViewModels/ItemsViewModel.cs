using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The item master: name-and-code based as approved (open-questions.md #5), with units, tracking
/// flags, valuation override, reorder level and kit components (phase-2-inventory.md §2).
/// </summary>
public sealed partial class ItemsViewModel : ObservableObject
{
    private readonly IInventoryMasterService _masters;

    [ObservableProperty]
    private ObservableCollection<ItemSummary> _items = [];

    [ObservableProperty]
    private ItemSummary? _selectedItem;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<StockGroupSummary> _groupChoices = [];

    [ObservableProperty]
    private ObservableCollection<UnitOfMeasureSummary> _unitChoices = [];

    [ObservableProperty]
    private ObservableCollection<ItemLookupRow> _componentChoices = [];

    [ObservableProperty]
    private ObservableCollection<KitComponentLine> _kitComponents = [];

    [ObservableProperty]
    private KitComponentLine? _selectedComponent;

    [ObservableProperty]
    private ItemLookupRow? _newComponentItem;

    [ObservableProperty]
    private string _newComponentQuantity = "1";

    // Editor fields.
    [ObservableProperty]
    private Guid? _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editCode = string.Empty;

    [ObservableProperty]
    private string _editBarcode = string.Empty;

    [ObservableProperty]
    private Guid? _editStockGroupId;

    [ObservableProperty]
    private Guid? _editBaseUnitId;

    [ObservableProperty]
    private Guid? _editAlternateUnitId;

    [ObservableProperty]
    private string _editConversionFactor = string.Empty;

    [ObservableProperty]
    private bool _editIsSerialTracked;

    [ObservableProperty]
    private bool _editIsBatchTracked;

    [ObservableProperty]
    private ValuationMethod? _editValuationOverride;

    [ObservableProperty]
    private string _editReorderLevel = "0";

    [ObservableProperty]
    private bool _editIsKit;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public ItemsViewModel(IInventoryMasterService masters)
    {
        _masters = masters ?? throw new ArgumentNullException(nameof(masters));
    }

    /// <summary>Sentinel shown in the valuation picker for "use the company default".</summary>
    public IReadOnlyList<ValuationMethod?> ValuationChoices { get; } =
        [null, ValuationMethod.Fifo, ValuationMethod.WeightedAverage];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            GroupChoices = new ObservableCollection<StockGroupSummary>(
                await _masters.ListStockGroupsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(true));

            UnitChoices = new ObservableCollection<UnitOfMeasureSummary>(
                await _masters.ListUnitsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(true));

            var items = await _masters.ListItemsAsync(
                FilterText,
                includeInactive: true,
                cancellationToken).ConfigureAwait(true);

            Items = new ObservableCollection<ItemSummary>(items);

            ComponentChoices = new ObservableCollection<ItemLookupRow>(
                items.Where(item => item.IsActive && !item.IsKit)
                    .Select(item => new ItemLookupRow(
                        item.Id, item.Name, item.Code, item.Barcode,
                        item.IsSerialTracked, item.IsBatchTracked, item.IsKit,
                        item.BaseUnitCode)));

            Status = $"{Items.Count} items.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Fills the editor from the selected row and its components.</summary>
    public async Task LoadSelectedIntoEditorAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is null)
        {
            return;
        }

        EditId = SelectedItem.Id;
        EditName = SelectedItem.Name;
        EditCode = SelectedItem.Code ?? string.Empty;
        EditBarcode = SelectedItem.Barcode ?? string.Empty;
        EditStockGroupId = SelectedItem.StockGroupId;
        EditBaseUnitId = SelectedItem.BaseUnitId;
        EditAlternateUnitId = SelectedItem.AlternateUnitId;
        EditConversionFactor = SelectedItem.ConversionFactor?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
        EditIsSerialTracked = SelectedItem.IsSerialTracked;
        EditIsBatchTracked = SelectedItem.IsBatchTracked;
        EditValuationOverride = SelectedItem.ValuationOverride;
        EditReorderLevel = SelectedItem.ReorderLevel.ToString("0.####", CultureInfo.InvariantCulture);
        EditIsKit = SelectedItem.IsKit;

        KitComponents = EditIsKit
            ? new ObservableCollection<KitComponentLine>(
                await _masters.ListKitComponentsAsync(SelectedItem.Id, cancellationToken)
                    .ConfigureAwait(true))
            : [];
    }

    [RelayCommand]
    private Task SelectionChangedAsync(CancellationToken cancellationToken) =>
        LoadSelectedIntoEditorAsync(cancellationToken);

    [RelayCommand]
    private void New()
    {
        EditId = null;
        EditName = string.Empty;
        EditCode = string.Empty;
        EditBarcode = string.Empty;
        EditStockGroupId = null;
        EditBaseUnitId = UnitChoices.FirstOrDefault()?.Id;
        EditAlternateUnitId = null;
        EditConversionFactor = string.Empty;
        EditIsSerialTracked = false;
        EditIsBatchTracked = false;
        EditValuationOverride = null;
        EditReorderLevel = "0";
        EditIsKit = false;
        KitComponents = [];
        Status = "New item — fill the form and save.";
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            Status = "Type a name first.";
            return;
        }

        if (!EditBaseUnitId.HasValue)
        {
            Status = "Choose a base unit first (create one on the Units list if none fits).";
            return;
        }

        if (!decimal.TryParse(EditReorderLevel, out var reorderLevel))
        {
            Status = "The reorder level must be a number.";
            return;
        }

        decimal? conversionFactor = null;

        if (EditAlternateUnitId.HasValue)
        {
            if (!decimal.TryParse(EditConversionFactor, out var parsed) || parsed <= 0m)
            {
                Status = "A conversion factor greater than zero is required with an alternate unit.";
                return;
            }

            conversionFactor = parsed;
        }

        IsBusy = true;

        try
        {
            var saved = await _masters.SaveItemAsync(new SaveItemRequest(
                EditId,
                EditName,
                EditBaseUnitId.Value,
                StockGroupId: EditStockGroupId,
                Code: NullIfEmpty(EditCode),
                Barcode: NullIfEmpty(EditBarcode),
                AlternateUnitId: EditAlternateUnitId,
                ConversionFactor: conversionFactor,
                IsSerialTracked: EditIsSerialTracked,
                IsBatchTracked: EditIsBatchTracked,
                ValuationOverride: EditValuationOverride,
                ReorderLevel: reorderLevel,
                IsKit: EditIsKit), cancellationToken).ConfigureAwait(true);

            Status = $"Saved '{saved.Name}'.";
            EditId = saved.Id;

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeactivateSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _masters.DeactivateItemAsync(SelectedItem.Id, cancellationToken).ConfigureAwait(true);
            Status = $"'{SelectedItem.Name}' deactivated.";

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _masters.DeleteItemAsync(SelectedItem.Id, cancellationToken).ConfigureAwait(true);
            Status = $"'{SelectedItem.Name}' deleted.";
            New();

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddComponent()
    {
        if (!EditIsKit || NewComponentItem is null)
        {
            Status = "Pick an item to add as a component first.";
            return;
        }

        if (!decimal.TryParse(NewComponentQuantity, out var quantity) || quantity <= 0m)
        {
            Status = "The component quantity must be a positive number.";
            return;
        }

        if (KitComponents.Any(line => line.ComponentItemId == NewComponentItem.Id))
        {
            Status = "That component is already on this kit.";
            return;
        }

        KitComponents.Add(new KitComponentLine(NewComponentItem.Id, NewComponentItem.Name, quantity));
        NewComponentQuantity = "1";
        Status = $"Component '{NewComponentItem.Name}' added. Save to keep it.";
    }

    [RelayCommand]
    private async Task SaveComponentsAsync(CancellationToken cancellationToken)
    {
        if (!EditId.HasValue || !EditIsKit)
        {
            Status = "Save the item as a kit before editing its components.";
            return;
        }

        IsBusy = true;

        try
        {
            await _masters.SetKitComponentsAsync(
                EditId.Value,
                KitComponents.Select(line => (line.ComponentItemId, line.Quantity)).ToList(),
                cancellationToken).ConfigureAwait(true);

            Status = $"Saved {KitComponents.Count} kit component(s).";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RemoveComponent()
    {
        if (SelectedComponent is not null)
        {
            KitComponents.Remove(SelectedComponent);
        }
    }

    private static string? NullIfEmpty(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}


