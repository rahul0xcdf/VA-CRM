using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Security;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The role master: what each role may do (phase-9-multiuser.md §4a D4).
/// </summary>
/// <remarks>
/// Roles are data rows, not an enum, so adding an Accountant role later is master data rather than
/// a release. The <i>permissions</i> are a closed set in code, because each one is a branch some
/// service actually takes — a grant naming something nothing checks would read as protection and
/// provide none.
/// <para>
/// Two things this screen refuses, both for the same reason. The owner role cannot have grants
/// edited — it holds everything by definition, now and after a later release adds more, so that an
/// owner is never locked out of a feature they paid for. And the last role able to manage users
/// cannot have that right taken away, because there would then be nobody who could give it back.
/// </para>
/// </remarks>
public sealed partial class RolesViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IUserAdminService _admin;

    [ObservableProperty]
    private ObservableCollection<AppRoleSummary> _roles = [];

    [ObservableProperty]
    private ObservableCollection<PermissionChoice> _permissions = [];

    [ObservableProperty]
    private AppRoleSummary? _selectedRole;

    [ObservableProperty]
    private Guid? _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    /// <summary>True when the selected role holds everything by definition and refuses grant edits.</summary>
    [ObservableProperty]
    private bool _isOwnerRoleSelected;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public RolesViewModel(IUserAdminService admin)
    {
        _admin = admin ?? throw new ArgumentNullException(nameof(admin));
        Permissions = BuildChoices([]);
    }

    /// <summary>Ctrl+A — commit the editor (ADR-0007).</summary>
    ICommand? IScreenShortcuts.AcceptCommand => SaveCommand;

    /// <summary>Alt+X — abandon the editor and reload whichever role is selected.</summary>
    ICommand? IScreenShortcuts.CancelCommand => SelectionChangedCommand;

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New role", NewCommand),
        new ShortcutButton(AppShortcuts.Delete, "Delete", DeleteSelectedCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Alt+C New · Ctrl+A Save · Alt+D Delete · Alt+X Reload the selected role";

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Roles = new ObservableCollection<AppRoleSummary>(
                await _admin.ListRolesAsync(cancellationToken).ConfigureAwait(true));

            Status = $"{Roles.Count} role(s).";
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
        if (SelectedRole is null)
        {
            return Task.CompletedTask;
        }

        EditId = SelectedRole.Id;
        EditName = SelectedRole.Name;
        EditDescription = SelectedRole.Description ?? string.Empty;
        IsOwnerRoleSelected = SelectedRole.IsOwnerRole;
        Permissions = BuildChoices(SelectedRole.Grants);

        Status = SelectedRole.IsOwnerRole
            ? $"'{SelectedRole.Name}' holds every permission by definition, including ones a later release adds. Its grants cannot be edited."
            : $"'{SelectedRole.Name}' — {SelectedRole.Grants.Count} permission(s).";

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void New()
    {
        EditId = null;
        EditName = string.Empty;
        EditDescription = string.Empty;
        IsOwnerRoleSelected = false;
        Permissions = BuildChoices([]);
        Status = "New role — tick what it may do and save.";
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
            var granted = Permissions
                .Where(choice => choice.IsGranted)
                .Select(choice => choice.Permission)
                .ToList();

            var saved = await _admin.SaveRoleAsync(
                    EditId,
                    new SaveRoleRequest(
                        EditName.Trim(),
                        string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim(),
                        granted),
                    cancellationToken)
                .ConfigureAwait(true);

            EditId = saved.Id;
            Status = $"Saved '{saved.Name}'.";

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (PermissionDeniedException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Where the two refusals land: editing the owner role's grants, and taking the last
            // user-management right away from the only role that has it.
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
        if (SelectedRole is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _admin.DeleteRoleAsync(SelectedRole.Id, cancellationToken).ConfigureAwait(true);
            Status = $"'{SelectedRole.Name}' deleted.";
            New();

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

    /// <summary>
    /// Every permission the product has, ticked where <paramref name="granted"/> holds it.
    /// </summary>
    /// <remarks>
    /// Built from <c>Enum.GetValues</c> rather than a curated list, so a permission added by a
    /// later release appears here without anybody remembering to add it — the alternative is a
    /// right that exists, is enforced, and cannot be granted from the one screen for granting it.
    /// </remarks>
    private static ObservableCollection<PermissionChoice> BuildChoices(
        IReadOnlyList<PermissionKind> granted) =>
        new(Enum.GetValues<PermissionKind>()
            .Select(permission => new PermissionChoice(permission, granted.Contains(permission))));
}
