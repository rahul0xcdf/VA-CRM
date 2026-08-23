using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The account-group master: the chart's classification tree, shown flattened in path order.
/// </summary>
/// <remarks>
/// System groups are shown but locked (spec §2): they can be renamed where the domain allows it,
/// never re-parented and never deleted — those rules live in the domain, so this screen only has to
/// surface the refusal rather than pre-empt it.
/// </remarks>
public sealed partial class AccountGroupsViewModel : ObservableObject
{
    private readonly IChartOfAccountsService _chartOfAccounts;

    [ObservableProperty]
    private ObservableCollection<AccountGroupSummary> _groups = [];

    [ObservableProperty]
    private AccountGroupSummary? _selectedGroup;

    [ObservableProperty]
    private ObservableCollection<AccountGroupSummary> _parentChoices = [];

    [ObservableProperty]
    private AccountGroupSummary? _newParentGroup;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    private int _newSortOrder;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public AccountGroupsViewModel(IChartOfAccountsService chartOfAccounts)
    {
        _chartOfAccounts = chartOfAccounts ?? throw new ArgumentNullException(nameof(chartOfAccounts));
    }

    partial void OnSelectedGroupChanged(AccountGroupSummary? value) =>
        NewParentGroup = value ?? NewParentGroup;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var groups = await _chartOfAccounts.ListGroupsAsync(
                includeInactive: true,
                cancellationToken).ConfigureAwait(true);

            Groups = new ObservableCollection<AccountGroupSummary>(groups);

            ParentChoices = new ObservableCollection<AccountGroupSummary>(
                groups.Where(group => group.IsActive));

            Status = $"{Groups.Count} groups.";
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
    private async Task AddGroupAsync(CancellationToken cancellationToken)
    {
        if (NewParentGroup is null)
        {
            Status = "Choose the parent group first.";
            return;
        }

        IsBusy = true;

        try
        {
            var created = await _chartOfAccounts.CreateGroupAsync(
                new CreateAccountGroupRequest(NewGroupName, NewParentGroup.Id, NewSortOrder),
                cancellationToken)
                .ConfigureAwait(true);

            Status = $"Created '{created.Name}'.";
            NewGroupName = string.Empty;
            NewSortOrder = 0;

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
    private async Task RenameSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedGroup is null || string.IsNullOrWhiteSpace(NewGroupName))
        {
            Status = "Select a group and type its new name first.";
            return;
        }

        IsBusy = true;

        try
        {
            var renamed = await _chartOfAccounts.RenameGroupAsync(
                SelectedGroup.Id,
                NewGroupName,
                cancellationToken)
                .ConfigureAwait(true);

            Status = $"Renamed to '{renamed.Name}'.";
            NewGroupName = string.Empty;

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

    /// <summary>Indentation for the flattened tree, two spaces per level below the root.</summary>
    public static string Indent(AccountGroupSummary group) =>
        new string(' ', Math.Max(0, group.Depth - 1) * 2) + group.Name;
}
