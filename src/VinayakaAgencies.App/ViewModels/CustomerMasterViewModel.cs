using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The customer master: contact and GST details, addresses, place of supply, credit terms, the one
/// category that prices this customer, and any per-customer special prices that win over the
/// category list (phase-4-customer-pricing.md §2, §4a).
/// </summary>
public sealed partial class CustomerMasterViewModel : ObservableObject
{
    private readonly ICustomerService _customerService;
    private readonly ICustomerCategoryService _categories;
    private readonly IPriceListService _prices;
    private readonly IInventoryMasterService _masters;
    private readonly List<Guid> _removedSpecialPriceIds = [];

    [ObservableProperty]
    private ObservableCollection<CustomerSummary> _customers = [];

    [ObservableProperty]
    private CustomerSummary? _selectedCustomer;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CustomerCategorySummary> _categoryChoices = [];

    [ObservableProperty]
    private ObservableCollection<ItemSummary> _itemChoices = [];

    [ObservableProperty]
    private ObservableCollection<PriceLineEditor> _specialPrices = [];

    // Editor fields.
    [ObservableProperty]
    private Guid? _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private Guid? _editCategoryId;

    [ObservableProperty]
    private string _editContactPerson = string.Empty;

    [ObservableProperty]
    private string _editPhoneNumber = string.Empty;

    [ObservableProperty]
    private string _editEmail = string.Empty;

    [ObservableProperty]
    private string _editGstin = string.Empty;

    [ObservableProperty]
    private string _editPlaceOfSupply = string.Empty;

    [ObservableProperty]
    private string _editBillingAddress = string.Empty;

    [ObservableProperty]
    private string _editShippingAddress = string.Empty;

    [ObservableProperty]
    private string _editCreditLimit = "0";

    [ObservableProperty]
    private string _editCreditPeriodDays = string.Empty;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public CustomerMasterViewModel(
        ICustomerService customers,
        ICustomerCategoryService categories,
        IPriceListService prices,
        IInventoryMasterService masters)
    {
        _customerService = customers ?? throw new ArgumentNullException(nameof(customers));
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
        _prices = prices ?? throw new ArgumentNullException(nameof(prices));
        _masters = masters ?? throw new ArgumentNullException(nameof(masters));
    }

    /// <summary>The pricing bases a special-price line may carry.</summary>
    public IReadOnlyList<PriceBasis> BasisChoices { get; } =
        [PriceBasis.FixedPrice, PriceBasis.MarginOnCost, PriceBasis.MarginOnSalePrice];

    [ObservableProperty]
    private ItemSummary? _newSpecialItem;

    [ObservableProperty]
    private PriceBasis _newSpecialBasis = PriceBasis.FixedPrice;

    [ObservableProperty]
    private string _newSpecialValue = string.Empty;

    partial void OnIncludeInactiveChanged(bool value) => _ = RefreshAsync(CancellationToken.None);

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

            var rows = string.IsNullOrWhiteSpace(FilterText)
                ? await _customerService.ListAsync(IncludeInactive, cancellationToken).ConfigureAwait(true)
                : await _customerService.SearchAsync(FilterText, IncludeInactive, cancellationToken).ConfigureAwait(true);

            Customers = new ObservableCollection<CustomerSummary>(rows);

            Status = $"{Customers.Count} customer(s).";
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

    public async Task LoadSelectedIntoEditorAsync(CancellationToken cancellationToken)
    {
        if (SelectedCustomer is null)
        {
            return;
        }

        var detail = SelectedCustomer;

        EditId = detail.Id;
        EditName = detail.Name;
        EditCategoryId = detail.CustomerCategoryId;
        EditContactPerson = detail.ContactPerson ?? string.Empty;
        EditPhoneNumber = detail.PhoneNumber ?? string.Empty;
        EditEmail = detail.Email ?? string.Empty;
        EditGstin = detail.Gstin ?? string.Empty;
        EditPlaceOfSupply = detail.PlaceOfSupplyStateCode ?? string.Empty;
        EditBillingAddress = detail.BillingAddressLines ?? string.Empty;
        EditShippingAddress = detail.ShippingAddressLines ?? string.Empty;
        EditCreditLimit = detail.CreditLimit.ToString("0.####", CultureInfo.InvariantCulture);
        EditCreditPeriodDays = detail.CreditPeriodDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        SpecialPrices = new ObservableCollection<PriceLineEditor>(
            (await _prices.ListCustomerSpecialPricesAsync(detail.Id, cancellationToken).ConfigureAwait(true))
            .Select(price => new PriceLineEditor(
                price.Id,
                price.ItemId,
                price.ItemName,
                price.Basis,
                price.FixedPrice,
                price.MarginPercent)));

        _removedSpecialPriceIds.Clear();
        NewSpecialItem = null;
        NewSpecialValue = string.Empty;
    }

    [RelayCommand]
    private Task SelectionChangedAsync(CancellationToken cancellationToken) =>
        LoadSelectedIntoEditorAsync(cancellationToken);

    [RelayCommand]
    private void New()
    {
        EditId = null;
        EditName = string.Empty;
        EditCategoryId = CategoryChoices.FirstOrDefault()?.Id;
        EditContactPerson = string.Empty;
        EditPhoneNumber = string.Empty;
        EditEmail = string.Empty;
        EditGstin = string.Empty;
        EditPlaceOfSupply = string.Empty;
        EditBillingAddress = string.Empty;
        EditShippingAddress = string.Empty;
        EditCreditLimit = "0";
        EditCreditPeriodDays = string.Empty;
        SpecialPrices = [];
        Status = "New customer — fill the form and save.";
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            Status = "Type a name first.";
            return;
        }

        if (!EditCategoryId.HasValue)
        {
            Status = "Choose the category this customer prices from first.";
            return;
        }

        if (!decimal.TryParse(EditCreditLimit, out var creditLimit))
        {
            Status = "The credit limit must be a number (0 means no limit).";
            return;
        }

        int? creditPeriodDays = null;

        if (!string.IsNullOrWhiteSpace(EditCreditPeriodDays))
        {
            if (!int.TryParse(EditCreditPeriodDays, out var parsedDays) || parsedDays < 0)
            {
                Status = "The credit period must be zero or a whole number of days.";
                return;
            }

            creditPeriodDays = parsedDays;
        }

        IsBusy = true;

        try
        {
            var saved = await _customerService.SaveAsync(new SaveCustomerRequest(
                EditId,
                EditName.Trim(),
                EditCategoryId.Value,
                ContactPerson: NullIfEmpty(EditContactPerson),
                PhoneNumber: NullIfEmpty(EditPhoneNumber),
                Email: NullIfEmpty(EditEmail),
                Gstin: NullIfEmpty(EditGstin)?.ToUpperInvariant(),
                PlaceOfSupplyStateCode: NullIfEmpty(EditPlaceOfSupply),
                BillingAddressLines: NullIfEmpty(EditBillingAddress),
                ShippingAddressLines: NullIfEmpty(EditShippingAddress),
                CreditLimit: creditLimit,
                CreditPeriodDays: creditPeriodDays), cancellationToken).ConfigureAwait(true);

            Status = saved.CreatedLedger
                ? $"Saved '{saved.Name}' with its own Sundry Debtors ledger."
                : $"Saved '{saved.Name}'.";
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
        if (SelectedCustomer is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _customerService.DeactivateAsync(SelectedCustomer.Id, cancellationToken).ConfigureAwait(true);
            Status = $"'{SelectedCustomer.Name}' deactivated; their ledger stays open for receipts.";

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
    private async Task ActivateSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedCustomer is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _customerService.ActivateAsync(SelectedCustomer.Id, cancellationToken).ConfigureAwait(true);
            Status = $"'{SelectedCustomer.Name}' reactivated.";

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
    private void AddSpecialPrice()
    {
        if (!EditId.HasValue)
        {
            Status = "Save the customer first, then add their special prices.";
            return;
        }

        if (NewSpecialItem is null)
        {
            Status = "Pick an item for the special price first.";
            return;
        }

        if (SpecialPrices.Any(line => line.ItemId == NewSpecialItem.Id))
        {
            Status = "That item already has a special price here.";
            return;
        }

        var staged = new PriceLineEditor(
            null, NewSpecialItem.Id, NewSpecialItem.Name, NewSpecialBasis, null, null, isDirty: true)
        {
            ValueText = string.IsNullOrWhiteSpace(NewSpecialValue) ? "0" : NewSpecialValue.Trim(),
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

        SpecialPrices.Add(staged);
        NewSpecialItem = null;
        NewSpecialValue = string.Empty;
        Status = "Special price staged — press Save special prices to keep it.";
    }

    [RelayCommand]
    private async Task SaveSpecialPricesAsync(CancellationToken cancellationToken)
    {
        if (!EditId.HasValue)
        {
            Status = "Save the customer first, then add their special prices.";
            return;
        }

        IsBusy = true;

        try
        {
            foreach (var line in SpecialPrices.Where(line => line.IsDirty))
            {
                await _prices.SetCustomerSpecialPriceAsync(new SavePriceLineRequest(
                        line.Id,
                        EditId.Value,
                        line.ItemId,
                        line.Basis,
                        FixedPrice: line.ParsedFixedPrice,
                        MarginPercent: line.ParsedMarginPercent),
                        cancellationToken)
                    .ConfigureAwait(true);

                line.MarkClean();
            }

            foreach (var id in _removedSpecialPriceIds)
            {
                await _prices.RemoveCustomerSpecialPriceAsync(id, cancellationToken).ConfigureAwait(true);
            }

            _removedSpecialPriceIds.Clear();
            Status = $"Saved {SpecialPrices.Count} special price(s).";
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
    private void RemoveSpecialPrice()
    {
        if (SelectedSpecialPrice is null)
        {
            return;
        }

        if (SelectedSpecialPrice.Id.HasValue)
        {
            // Already saved — the removal reaches the database on Save special prices.
            _removedSpecialPriceIds.Add(SelectedSpecialPrice.Id.Value);
        }

        SpecialPrices.Remove(SelectedSpecialPrice);
    }

    [ObservableProperty]
    private PriceLineEditor? _selectedSpecialPrice;

    private static string? NullIfEmpty(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
