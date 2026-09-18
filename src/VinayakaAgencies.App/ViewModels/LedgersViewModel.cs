using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The ledger master: create, rename, re-file, deactivate and (where nothing was ever posted)
/// delete postable accounts.
/// </summary>
/// <remarks>
/// The list can be narrowed by group and searched by name or alias (spec §2). Refusals — a system
/// row that cannot be deleted, a duplicate name, a ledger with postings — are surfaced as status
/// text from the domain's own messages rather than second-guessed here.
/// </remarks>
public sealed partial class LedgersViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IChartOfAccountsService _chartOfAccounts;

    [ObservableProperty]
    private ObservableCollection<LedgerSummary> _ledgers = [];

    [ObservableProperty]
    private ObservableCollection<AccountGroupSummary> _groupChoices = [];

    [ObservableProperty]
    private ObservableCollection<LedgerSummary> _visibleLedgers = [];

    [ObservableProperty]
    private LedgerSummary? _selectedLedger;

    [ObservableProperty]
    private AccountGroupSummary? _groupFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private Guid _editGroupId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string? _editAlias;

    [ObservableProperty]
    private string? _editDescription;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public LedgersViewModel(IChartOfAccountsService chartOfAccounts)
    {
        _chartOfAccounts = chartOfAccounts ?? throw new ArgumentNullException(nameof(chartOfAccounts));
    }

    partial void OnSelectedLedgerChanged(LedgerSummary? value)
    {
        // The action bar carries Accept, and Accept means create or update depending on this; the
        // shell re-reads the bar when it changes, so the key follows the selection.
        OnPropertyChanged(nameof(IScreenShortcuts.ActionBar));

        if (value is null)
        {
            return;
        }

        EditGroupId = value.AccountGroupId;
        EditName = value.Name;
        EditAlias = value.Alias;
        EditDescription = value.Description;
    }

    partial void OnGroupFilterChanged(AccountGroupSummary? value) => ApplyFilters();

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnIncludeInactiveChanged(bool value) => ApplyFilters();

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var groups = await _chartOfAccounts.ListGroupsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            GroupChoices = new ObservableCollection<AccountGroupSummary>(groups);

            await ReloadLedgersAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task NewLedgerAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var created = await _chartOfAccounts.CreateLedgerAsync(
                new CreateLedgerRequest(EditName, EditGroupId, EditAlias, EditDescription),
                cancellationToken)
                .ConfigureAwait(true);

            Status = $"Created '{created.Name}'.";
            ClearEditor();

            await ReloadLedgersAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task SaveLedgerAsync(CancellationToken cancellationToken)
    {
        if (SelectedLedger is null)
        {
            Status = "Select the ledger to change first.";
            return;
        }

        IsBusy = true;

        try
        {
            var updated = await _chartOfAccounts.UpdateLedgerAsync(
                new UpdateLedgerRequest(
                    SelectedLedger.Id,
                    EditName,
                    EditGroupId,
                    EditAlias,
                    EditDescription),
                cancellationToken)
                .ConfigureAwait(true);

            Status = $"Saved '{updated.Name}'.";

            await ReloadLedgersAsync(cancellationToken).ConfigureAwait(true);
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
        if (SelectedLedger is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _chartOfAccounts.DeactivateLedgerAsync(SelectedLedger.Id, cancellationToken)
                .ConfigureAwait(true);

            Status = $"Deactivated '{SelectedLedger.Name}'. It keeps its history.";
            ClearEditor();

            await ReloadLedgersAsync(cancellationToken).ConfigureAwait(true);
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
        if (SelectedLedger is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _chartOfAccounts.ActivateLedgerAsync(SelectedLedger.Id, cancellationToken)
                .ConfigureAwait(true);

            Status = $"Reactivated '{SelectedLedger.Name}'.";
            ClearEditor();

            await ReloadLedgersAsync(cancellationToken).ConfigureAwait(true);
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
        if (SelectedLedger is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var name = SelectedLedger.Name;

            await _chartOfAccounts.DeleteLedgerAsync(SelectedLedger.Id, cancellationToken)
                .ConfigureAwait(true);

            Status = $"Deleted '{name}'. Its full before-image stays in the edit log.";
            ClearEditor();

            await ReloadLedgersAsync(cancellationToken).ConfigureAwait(true);
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

    private async Task ReloadLedgersAsync(CancellationToken cancellationToken)
    {
        var ledgers = await _chartOfAccounts.ListLedgersAsync(includeInactive: true, cancellationToken)
            .ConfigureAwait(true);

        Ledgers = new ObservableCollection<LedgerSummary>(ledgers);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<LedgerSummary> query = Ledgers;

        if (!IncludeInactive)
        {
            query = query.Where(ledger => ledger.IsActive);
        }

        if (GroupFilter is { } group)
        {
            query = query.Where(ledger => ledger.AccountGroupId == group.Id);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            query = query.Where(ledger =>
                ledger.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (ledger.Alias?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        VisibleLedgers = new ObservableCollection<LedgerSummary>(
            query.OrderBy(ledger => ledger.AccountGroupName, StringComparer.Ordinal)
                .ThenBy(ledger => ledger.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ctrl+A. Accept means "commit what I typed", which is a create when no ledger is selected and
    /// an update when one is — the operator should not have to know which command they are in.
    /// </summary>
    ICommand? IScreenShortcuts.AcceptCommand =>
        SelectedLedger is null ? NewLedgerCommand : SaveLedgerCommand;

    /// <summary>Alt+X — abandon the editor.</summary>
    ICommand? IScreenShortcuts.CancelCommand => NewEntryCommand;

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New ledger", NewEntryCommand),
        new ShortcutButton(AppShortcuts.Delete, "Delete", DeleteSelectedCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Alt+C New · Ctrl+A Accept · Alt+D Delete · Alt+X Abandon";

    /// <summary>Alt+C — empty the editor so the next Ctrl+A creates rather than updates.</summary>
    [RelayCommand]
    private void NewEntry() => ClearEditor();

    private void ClearEditor()
    {
        SelectedLedger = null;
        EditName = string.Empty;
        EditAlias = null;
        EditDescription = null;

        if (GroupChoices.Count > 0)
        {
            EditGroupId = GroupChoices[0].Id;
        }
    }
}
