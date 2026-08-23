using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Customers;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The customer category master: the rows whose price lists price their customers
/// (phase-4-customer-pricing.md §2). Master data, not an enum (§4a).
/// </summary>
public sealed partial class CustomerCategoriesViewModel : ObservableObject
{
    private readonly ICustomerCategoryService _categoryService;

    [ObservableProperty]
    private ObservableCollection<CustomerCategorySummary> _categories = [];

    [ObservableProperty]
    private CustomerCategorySummary? _selectedCategory;

    [ObservableProperty]
    private Guid? _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public CustomerCategoriesViewModel(ICustomerCategoryService categories)
    {
        _categoryService = categories ?? throw new ArgumentNullException(nameof(categories));
    }

    partial void OnIncludeInactiveChanged(bool value) => _ = RefreshAsync(CancellationToken.None);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Categories = new ObservableCollection<CustomerCategorySummary>(
                await _categoryService.ListAsync(IncludeInactive, cancellationToken).ConfigureAwait(true));

            Status = $"{Categories.Count} category/categories.";
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

    public Task LoadSelectedIntoEditorAsync(CancellationToken cancellationToken)
    {
        if (SelectedCategory is null)
        {
            return Task.CompletedTask;
        }

        EditId = SelectedCategory.Id;
        EditName = SelectedCategory.Name;
        EditDescription = SelectedCategory.Description ?? string.Empty;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task SelectionChangedAsync(CancellationToken cancellationToken) =>
        LoadSelectedIntoEditorAsync(cancellationToken);

    [RelayCommand]
    private void New()
    {
        EditId = null;
        EditName = string.Empty;
        EditDescription = string.Empty;
        Status = "New category — fill the form and save.";
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            Status = "Type a name first.";
            return;
        }

        IsBusy = true;

        try
        {
            var saved = await _categoryService.SaveAsync(
                    new SaveCustomerCategoryRequest(EditId, EditName.Trim(), NullIfEmpty(EditDescription)),
                    cancellationToken)
                .ConfigureAwait(true);

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
        if (SelectedCategory is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _categoryService.DeactivateAsync(SelectedCategory.Id, cancellationToken).ConfigureAwait(true);
            Status = $"'{SelectedCategory.Name}' deactivated.";

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
        if (SelectedCategory is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _categoryService.ActivateAsync(SelectedCategory.Id, cancellationToken).ConfigureAwait(true);
            Status = $"'{SelectedCategory.Name}' reactivated.";

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
        if (SelectedCategory is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _categoryService.DeleteAsync(SelectedCategory.Id, cancellationToken).ConfigureAwait(true);
            Status = $"'{SelectedCategory.Name}' deleted.";
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

    private static string? NullIfEmpty(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
