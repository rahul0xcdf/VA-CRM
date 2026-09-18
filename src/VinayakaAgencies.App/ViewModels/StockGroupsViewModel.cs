using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Inventory;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The stock-group master: classification rows items hang off (phase-2-inventory.md §1).
/// </summary>
public sealed partial class StockGroupsViewModel : ObservableObject, IScreenShortcuts
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

    /// <summary>
    /// Ctrl+A. Accept is a create when nothing is selected and an update when something is, so the
    /// operator never has to pick between two commands that mean "commit what I typed" (ADR-0007).
    /// </summary>
    ICommand? IScreenShortcuts.AcceptCommand =>
        SelectedGroup is null ? AddCommand : SaveSelectedCommand;

    /// <summary>Alt+X — abandon the editor.</summary>
    ICommand? IScreenShortcuts.CancelCommand => NewEntryCommand;

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New group", NewEntryCommand),
        new ShortcutButton(AppShortcuts.Delete, "Delete", DeleteSelectedCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Alt+C New · Ctrl+A Accept · Alt+D Delete · Alt+X Abandon";

    /// <summary>Alt+C — empty the editor so the next Ctrl+A creates rather than updates.</summary>
    [RelayCommand]
    private void NewEntry()
    {
        SelectedGroup = null;
        EditName = string.Empty;
        EditDescription = string.Empty;
        Status = "New stock group — name it, then Ctrl+A.";
    }

    /// <summary>
    /// Selecting a row loads it into the editor.
    /// </summary>
    /// <remarks>
    /// It did not before: the operator selected a group, typed a new name into an editor still
    /// holding the last one, and pressed Save. Every other master screen loads on selection, and the
    /// Alt+X meaning this batch settled on — "put the editor back to the selected record" — is only
    /// coherent if selection puts something there in the first place.
    /// </remarks>
    partial void OnSelectedGroupChanged(StockGroupSummary? value)
    {
        OnPropertyChanged(nameof(IScreenShortcuts.ActionBar));

        if (value is null)
        {
            return;
        }

        EditName = value.Name;
        EditDescription = value.Description ?? string.Empty;
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
