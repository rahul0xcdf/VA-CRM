using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Inventory;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The stock-group master: classification rows items hang off (phase-2-inventory.md §1).
/// </summary>
public sealed partial class StockGroupsViewModel : ObservableObject
{
    private readonly IInventoryMasterService _masters;

    [ObservableProperty]
    private ObservableCollection<StockGroupSummary> _groups = [];

    [ObservableProperty]
    private StockGroupSummary? _selectedGroup;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public StockGroupsViewModel(IInventoryMasterService masters)
    {
        _masters = masters ?? throw new ArgumentNullException(nameof(masters));
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var groups = await _masters.ListStockGroupsAsync(
                includeInactive: true,
                cancellationToken).ConfigureAwait(true);

            Groups = new ObservableCollection<StockGroupSummary>(groups);
            Status = $"{Groups.Count} stock groups.";
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
    private async Task AddAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            Status = "Type a name first.";
            return;
        }

        IsBusy = true;

        try
        {
            var created = await _masters.CreateStockGroupAsync(
                new CreateStockGroupRequest(EditName, EditDescription),
                cancellationToken).ConfigureAwait(true);

            Status = $"Created '{created.Name}'.";
            EditName = string.Empty;
            EditDescription = string.Empty;

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
    private async Task SaveSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedGroup is null || string.IsNullOrWhiteSpace(EditName))
        {
            Status = "Select a group and edit its name first.";
            return;
        }

        IsBusy = true;

        try
        {
            var updated = await _masters.UpdateStockGroupAsync(
                new UpdateStockGroupRequest(SelectedGroup.Id, EditName, EditDescription),
                cancellationToken).ConfigureAwait(true);

            Status = $"Saved '{updated.Name}'.";

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
        if (SelectedGroup is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _masters.DeactivateStockGroupAsync(SelectedGroup.Id, cancellationToken)
                .ConfigureAwait(true);
            Status = $"'{SelectedGroup.Name}' deactivated.";

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
        if (SelectedGroup is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _masters.DeleteStockGroupAsync(SelectedGroup.Id, cancellationToken)
                .ConfigureAwait(true);
            Status = $"'{SelectedGroup.Name}' deleted.";
            EditName = string.Empty;

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
}
