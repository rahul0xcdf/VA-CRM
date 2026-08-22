using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Inventory;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The godown master: the warehouses stock is held in (phase-2-inventory.md §2).
/// </summary>
public sealed partial class GodownsViewModel : ObservableObject
{
    private readonly IInventoryMasterService _masters;

    [ObservableProperty]
    private ObservableCollection<GodownSummary> _godowns = [];

    [ObservableProperty]
    private GodownSummary? _selectedGodown;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editAddress = string.Empty;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public GodownsViewModel(IInventoryMasterService masters)
    {
        _masters = masters ?? throw new ArgumentNullException(nameof(masters));
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var godowns = await _masters.ListGodownsAsync(
                includeInactive: true,
                cancellationToken).ConfigureAwait(true);

            Godowns = new ObservableCollection<GodownSummary>(godowns);
            Status = $"{Godowns.Count} godowns.";
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
            var created = await _masters.CreateGodownAsync(
                new CreateGodownRequest(EditName, EditAddress),
                cancellationToken).ConfigureAwait(true);

            Status = $"Created '{created.Name}'.";
            EditName = string.Empty;
            EditAddress = string.Empty;

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
        if (SelectedGodown is null || string.IsNullOrWhiteSpace(EditName))
        {
            Status = "Select a godown and edit its name first.";
            return;
        }

        IsBusy = true;

        try
        {
            var updated = await _masters.UpdateGodownAsync(
                new UpdateGodownRequest(SelectedGodown.Id, EditName, EditAddress),
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

    /// <summary>Fills the editor from the selected row.</summary>
    public void LoadSelectedIntoEditor()
    {
        if (SelectedGodown is null)
        {
            return;
        }

        EditName = SelectedGodown.Name;
        EditAddress = SelectedGodown.Address ?? string.Empty;
    }
}
