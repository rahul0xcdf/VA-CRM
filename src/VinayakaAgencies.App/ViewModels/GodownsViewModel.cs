using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Inventory;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The godown master: the warehouses stock is held in (phase-2-inventory.md §2).
/// </summary>
public sealed partial class GodownsViewModel : ObservableObject, IScreenShortcuts
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

    /// <summary>
    /// Ctrl+A. A create when nothing is selected, an update when something is (ADR-0007).
    /// </summary>
    ICommand? IScreenShortcuts.AcceptCommand =>
        SelectedGodown is null ? AddCommand : SaveSelectedCommand;

    /// <summary>Alt+X — abandon the editor.</summary>
    ICommand? IScreenShortcuts.CancelCommand => NewEntryCommand;

    /// <summary>
    /// No Alt+D: the service offers no godown delete, and a key that cannot run is exactly what
    /// ADR-0006 forbids a bar from showing.
    /// </summary>
    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New godown", NewEntryCommand),
    ];

    string? IScreenShortcuts.StatusBarHints => "Alt+C New · Ctrl+A Accept · Alt+X Abandon";

    /// <summary>Alt+C — empty the editor so the next Ctrl+A creates rather than updates.</summary>
    [RelayCommand]
    private void NewEntry()
    {
        SelectedGodown = null;
        EditName = string.Empty;
        EditAddress = string.Empty;
        Status = "New godown — name it, then Ctrl+A.";
    }

    /// <summary>
    /// Selecting a row loads it into the editor.
    /// </summary>
    /// <remarks>
    /// <see cref="LoadSelectedIntoEditor"/> already existed but nothing ever called it, so selecting a
    /// godown left the editor holding the previous one and Save renamed the wrong row. Hooking it to
    /// the property change fixes that and makes Alt+X — "put the editor back to the selected record" —
    /// mean the same thing here as on every other master screen.
    /// </remarks>
    partial void OnSelectedGodownChanged(GodownSummary? value)
    {
        OnPropertyChanged(nameof(IScreenShortcuts.ActionBar));
        LoadSelectedIntoEditor();
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
