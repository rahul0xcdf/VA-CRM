using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The category-wise price list / margin table (phase-4-customer-pricing.md §2): per item-category
/// line, either a fixed selling price or a margin/discount percent over cost or over MRP — the
/// basis flag picks which, exactly as approved in §4a.
/// </summary>
public sealed partial class CategoryPriceListViewModel : ObservableObject
{
    private readonly ICustomerCategoryService _categories;
    private readonly IPriceListService _prices;
    private readonly IInventoryMasterService _masters;

    [ObservableProperty]
    private ObservableCollection<CustomerCategorySummary> _categoryChoices = [];

    [ObservableProperty]
    private CustomerCategorySummary? _selectedCategory;

    [ObservableProperty]
    private ObservableCollection<PriceLineEditor> _priceLines = [];

    [ObservableProperty]
    private PriceLineEditor? _selectedLine;

    [ObservableProperty]
    private ObservableCollection<ItemSummary> _itemChoices = [];

    // Staging fields for a new line.
    [ObservableProperty]
    private ItemSummary? _newItem;

    [ObservableProperty]
    private PriceBasis _newBasis = PriceBasis.FixedPrice;

    [ObservableProperty]
    private string _newValue = string.Empty;

    [ObservableProperty]
    private string _status = "Pick a category to see its prices.";

    [ObservableProperty]
    private bool _isBusy;

    public CategoryPriceListViewModel(
        ICustomerCategoryService categories,
        IPriceListService prices,
        IInventoryMasterService masters)
    {
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
        _prices = prices ?? throw new ArgumentNullException(nameof(prices));
        _masters = masters ?? throw new ArgumentNullException(nameof(masters));
    }

    /// <summary>The pricing bases one line may carry (§4a).</summary>
    public IReadOnlyList<PriceBasis> BasisChoices { get; } =
        [PriceBasis.FixedPrice, PriceBasis.MarginOnCost, PriceBasis.MarginOnSalePrice];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            CategoryChoices = new ObservableCollection<CustomerCategorySummary>(
                await _categories.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(true));

            ItemChoices = new ObservableCollection<ItemSummary>(
                await _masters.ListItemsAsync(string.Empty, includeInactive: false, cancellationToken)
                    .ConfigureAwait(true));

            SelectedCategory ??= CategoryChoices.FirstOrDefault();

            await LoadLinesAsync(cancellationToken).ConfigureAwait(true);
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

    partial void OnSelectedCategoryChanged(CustomerCategorySummary? value) =>
        _ = LoadLinesAsyncCoreAsync();

    private async Task LoadLinesAsyncCoreAsync()
    {
        try
        {
            await LoadLinesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
    }

    private async Task LoadLinesAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedCategory is null)
        {
            PriceLines = [];
            Status = "Create a category first (Masters → Customer categories).";
            return;
        }

        PriceLines = new ObservableCollection<PriceLineEditor>(
            (await _prices.ListCategoryPricesAsync(SelectedCategory.Id, cancellationToken).ConfigureAwait(true))
            .Select(price => new PriceLineEditor(
                price.Id,
                price.ItemId,
                price.ItemName,
                price.Basis,
                price.FixedPrice,
                price.MarginPercent)));

        Status = $"{PriceLines.Count} priced item(s) in '{SelectedCategory.Name}'.";
    }

    [RelayCommand]
    private void AddLine()
    {
        if (SelectedCategory is null)
        {
            Status = "Pick a category first.";
            return;
        }

        if (NewItem is null)
        {
            Status = "Pick an item to price first.";
            return;
        }

        if (PriceLines.Any(line => line.ItemId == NewItem.Id))
        {
            Status = "That item is already priced in this category — edit its row instead.";
            return;
        }

        var staged = new PriceLineEditor(
            null, NewItem.Id, NewItem.Name, NewBasis, null, null, isDirty: true)
        {
            ValueText = string.IsNullOrWhiteSpace(NewValue) ? "0" : NewValue.Trim(),
        };

        if (staged.ParsedFixedPrice is null && staged.ParsedMarginPercent is null)
        {
            Status = "The price must be a number.";
            return;
        }

        if (staged.Basis == PriceBasis.FixedPrice && staged.ParsedFixedPrice <= 0m)
        {
            Status = "A fixed price must be greater than zero.";
            return;
        }

        if (staged.Basis != PriceBasis.FixedPrice
            && (staged.ParsedMarginPercent <= 0m || staged.ParsedMarginPercent >= 100m))
        {
            Status = "A margin/discount percent must be between 0 and 100.";
            return;
        }

        PriceLines.Add(staged);
        NewItem = null;
        NewValue = string.Empty;
        Status = "Line staged — press Save prices to keep it.";
    }

    [RelayCommand]
    private async Task SavePricesAsync(CancellationToken cancellationToken)
    {
        if (SelectedCategory is null)
        {
            Status = "Pick a category first.";
            return;
        }

        IsBusy = true;

        try
        {
            foreach (var line in PriceLines.Where(line => line.IsDirty))
            {
                await _prices.SetCategoryPriceAsync(new SavePriceLineRequest(
                        line.Id,
                        SelectedCategory.Id,
                        line.ItemId,
                        line.Basis,
                        FixedPrice: line.ParsedFixedPrice,
                        MarginPercent: line.ParsedMarginPercent),
                        cancellationToken)
                    .ConfigureAwait(true);

                line.MarkClean();
            }

            Status = $"Saved {PriceLines.Count} price line(s) for '{SelectedCategory.Name}'.";
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
    private async Task RemoveSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedLine is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            if (SelectedLine.Id.HasValue)
            {
                await _prices.RemoveCategoryPriceAsync(SelectedLine.Id.Value, cancellationToken)
                    .ConfigureAwait(true);
            }

            PriceLines.Remove(SelectedLine);
            Status = $"Removed. {PriceLines.Count} priced item(s) remain.";
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
}
