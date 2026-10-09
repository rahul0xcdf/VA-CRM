using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Security;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The user master: who may sign in, and under which role (phase-9-multiuser.md §4a D2/D4).
/// </summary>
/// <remarks>
/// Every service call behind this screen requires <see cref="PermissionKind.ManageUsers"/>, and the
/// refusal is caught and shown rather than allowed to crash the screen. That is not defensive
/// padding: an Operator can reach this screen through the Go To palette, so "the Gateway hides it"
/// was never the protection — the service refusing is (C9.2).
/// <para>
/// Accounts are deactivated rather than deleted. The audit trail refers to them by name for as long
/// as the books exist, and a dangling author is worse than a disabled row — which is why there is no
/// delete command here at all.
/// </para>
/// </remarks>
public sealed partial class UsersViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IUserAdminService _admin;

    [ObservableProperty]
    private ObservableCollection<AppUserSummary> _users = [];

    [ObservableProperty]
    private ObservableCollection<AppRoleSummary> _roleChoices = [];

    [ObservableProperty]
    private AppUserSummary? _selectedUser;

    [ObservableProperty]
    private Guid? _editId;

    [ObservableProperty]
    private string _editUserName = string.Empty;

    [ObservableProperty]
    private string _editDisplayName = string.Empty;

    [ObservableProperty]
    private AppRoleSummary? _editRole;

    [ObservableProperty]
    private bool _editIsActive = true;

    /// <summary>
    /// The initial or reset password the owner types.
    /// </summary>
    /// <remarks>
    /// Used for a new account and for a reset, and in both cases the account is flagged to change
    /// it at next sign-in — the owner knowing somebody's working password is the thing that makes
    /// the audit trail's attribution worthless.
    /// </remarks>
    [ObservableProperty]
    private string _editPassword = string.Empty;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public UsersViewModel(IUserAdminService admin) =>
        _admin = admin ?? throw new ArgumentNullException(nameof(admin));

    /// <summary>Ctrl+A — commit the editor (ADR-0007).</summary>
    ICommand? IScreenShortcuts.AcceptCommand => SaveCommand;

    /// <summary>Alt+X — abandon the editor and reload whichever account is selected.</summary>
    ICommand? IScreenShortcuts.CancelCommand => SelectionChangedCommand;

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New account", NewCommand),
        new ShortcutButton(AppShortcuts.Delete, "Deactivate", DeactivateSelectedCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Alt+C New · Ctrl+A Save · Alt+D Deactivate · Alt+X Reload the selected account";

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            RoleChoices = new ObservableCollection<AppRoleSummary>(
                await _admin.ListRolesAsync(cancellationToken).ConfigureAwait(true));

            Users = new ObservableCollection<AppUserSummary>(
                await _admin.ListUsersAsync(cancellationToken).ConfigureAwait(true));

            Status = Users.Count == 0
                ? "No accounts yet. Until one exists, everybody on this install can do everything."
                : $"{Users.Count} account(s).";
        }
        catch (PermissionDeniedException ex)
        {
            Status = ex.Message;
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
    private Task SelectionChangedAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is null)
        {
            return Task.CompletedTask;
        }

        EditId = SelectedUser.Id;
        EditUserName = SelectedUser.UserName;
        EditDisplayName = SelectedUser.DisplayName;
        EditIsActive = SelectedUser.IsActive;
        EditRole = RoleChoices.FirstOrDefault(role => role.Id == SelectedUser.RoleId);

        // Never carried over from whatever was last typed. A stray value here would reset somebody's
        // password as a side effect of clicking their row.
        EditPassword = string.Empty;

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void New()
    {
        EditId = null;
        EditUserName = string.Empty;
        EditDisplayName = string.Empty;
        EditPassword = string.Empty;
        EditIsActive = true;
        EditRole = RoleChoices.FirstOrDefault(role => !role.IsOwnerRole) ?? RoleChoices.FirstOrDefault();
        Status = "New account — fill the form and save.";
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EditUserName))
        {
            Status = "Type a user name first.";
            return;
        }

        if (EditRole is null)
        {
            Status = "Pick a role first.";
            return;
        }

        IsBusy = true;

        try
        {
            if (EditId is null)
            {
                var created = await _admin.CreateUserAsync(
                        new CreateUserRequest(
                            EditUserName.Trim(),
                            string.IsNullOrWhiteSpace(EditDisplayName)
                                ? EditUserName.Trim()
                                : EditDisplayName.Trim(),
                            EditPassword,
                            EditRole.Id),
                        cancellationToken)
                    .ConfigureAwait(true);

                EditId = created.Id;
                Status = $"Created '{created.UserName}'. They must change this password at first sign-in.";
            }
            else
            {
                var saved = await _admin.UpdateUserAsync(
                        new UpdateUserRequest(
                            EditId.Value,
                            EditUserName.Trim(),
                            string.IsNullOrWhiteSpace(EditDisplayName)
                                ? EditUserName.Trim()
                                : EditDisplayName.Trim(),
                            EditRole.Id,
                            EditIsActive),
                        cancellationToken)
                    .ConfigureAwait(true);

                Status = $"Saved '{saved.UserName}'.";
            }

            EditPassword = string.Empty;

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (PermissionDeniedException ex)
        {
            Status = ex.Message;
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
    private async Task ResetPasswordAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is null)
        {
            Status = "Pick the account to reset first.";
            return;
        }

        IsBusy = true;

        try
        {
            await _admin.ResetPasswordAsync(SelectedUser.Id, EditPassword, cancellationToken)
                .ConfigureAwait(true);

            Status = $"'{SelectedUser.UserName}' must change this password at next sign-in.";
            EditPassword = string.Empty;

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (PermissionDeniedException ex)
        {
            Status = ex.Message;
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
    private Task DeactivateSelectedAsync(CancellationToken cancellationToken) =>
        SetSelectedActiveAsync(false, cancellationToken);

    [RelayCommand]
    private Task ActivateSelectedAsync(CancellationToken cancellationToken) =>
        SetSelectedActiveAsync(true, cancellationToken);

    private async Task SetSelectedActiveAsync(bool isActive, CancellationToken cancellationToken)
    {
        if (SelectedUser is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _admin.UpdateUserAsync(
                    new UpdateUserRequest(
                        SelectedUser.Id,
                        SelectedUser.UserName,
                        SelectedUser.DisplayName,
                        SelectedUser.RoleId,
                        isActive),
                    cancellationToken)
                .ConfigureAwait(true);

            Status = isActive
                ? $"'{SelectedUser.UserName}' can sign in again."
                : $"'{SelectedUser.UserName}' can no longer sign in.";

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (PermissionDeniedException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // The lockout guard lands here: taking the last administrator's access away is refused
            // with a message naming what to do instead.
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
