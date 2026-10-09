using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Security;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Backs the sign-in window, in all three states a launch can be in.
/// </summary>
/// <remarks>
/// `phase-9-multiuser.md` §4a D2 chose application accounts over Windows authentication, and the
/// reason was the audit trail: non-negotiable #3 requires every change to name a user, and a
/// workgroup shop shares one Windows login per machine. This window is where that name comes from.
/// <para>
/// Three states, decided by the data rather than by the operator:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>No accounts exist</b> — the first launch after this phase. It offers to create the owner
/// account, because no account is seeded: a shipped user name with a default password is a hole,
/// not a convenience.
/// </item>
/// <item><b>Accounts exist</b> — sign in.</item>
/// <item>
/// <b>Signed in with a password the owner set</b> — change it. The owner typed the initial
/// password, so the owner knows it; this is what makes that temporary, and it is what makes the
/// audit trail's attribution to this account mean anything afterwards.
/// </item>
/// </list>
/// <para>
/// Nothing here is permission-gated, and it must not be: before sign-in there is no permission to
/// hold. <see cref="ISignInService"/> is a separate service from <see cref="IUserAdminService"/>
/// for exactly that reason.
/// </para>
/// </remarks>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly ISignInService _signIn;
    private readonly Guid _companyId;

    public LoginViewModel(ISignInService signIn, Guid companyId)
    {
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _companyId = companyId;
    }

    /// <summary>Raised when the session is established and the shell may open.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Whether a session was established; the host opens the shell only if so.</summary>
    [ObservableProperty]
    private bool _isSignedIn;

    /// <summary>
    /// True when this install has no accounts at all, so the window offers to create the first.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSignIn))]
    private bool _isFirstAccount;

    /// <summary>True once the signed-in account has to replace a password somebody else chose.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSignIn))]
    private bool _mustChangePassword;

    /// <summary>Whether the plain sign-in fields are the ones on show.</summary>
    public bool ShowSignIn => !IsFirstAccount && !MustChangePassword;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateFirstAccountCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private bool _isBusy;

    private bool CanInteract => !IsBusy;

    /// <summary>
    /// Works out which of the three states this launch is in.
    /// </summary>
    /// <remarks>
    /// Called by the host before the window is shown. It asks the same question
    /// <c>IPermissionAuthority</c> asks — whether any account exists — so the window and the
    /// enforcement cannot disagree about whether this install is secured.
    /// </remarks>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            IsFirstAccount = !await _signIn
                .AnyAccountsExistAsync(cancellationToken)
                .ConfigureAwait(true);

            Status = IsFirstAccount
                ? "No accounts exist yet. Create the owner account to secure these books."
                : "Sign in to open the books.";
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

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var outcome = await _signIn
                .SignInAsync(UserName, Password, cancellationToken)
                .ConfigureAwait(true);

            // Cleared whatever happened. A failed attempt leaving the password on screen is how the
            // next person at the counter reads it.
            Password = string.Empty;

            if (!outcome.Succeeded)
            {
                Status = outcome.Message;
                return;
            }

            Status = outcome.Message;

            if (outcome.SignedIn!.MustChangePassword)
            {
                // Signed in, but not finished: routed to the change rather than refused, because an
                // operator who cannot get in also cannot change their password.
                MustChangePassword = true;
                Status = "This password was set for you. Choose your own to continue.";
                return;
            }

            Finish();
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

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task CreateFirstAccountAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            Status = "The two passwords do not match.";
            return;
        }

        IsBusy = true;

        try
        {
            var outcome = await _signIn
                .CreateFirstAccountAsync(
                    _companyId,
                    UserName,
                    string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName,
                    NewPassword,
                    cancellationToken)
                .ConfigureAwait(true);

            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;

            if (!outcome.Succeeded)
            {
                Status = outcome.Message;
                return;
            }

            Status = outcome.Message;
            Finish();
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

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ChangePasswordAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            Status = "The two passwords do not match.";
            return;
        }

        IsBusy = true;

        try
        {
            var outcome = await _signIn
                .ChangePasswordAsync(Password, NewPassword, cancellationToken)
                .ConfigureAwait(true);

            if (!outcome.Succeeded)
            {
                Status = string.Join(" ", outcome.Errors);
                return;
            }

            Password = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            Status = "Password changed.";

            Finish();
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
    private void Quit()
    {
        IsSignedIn = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Finish()
    {
        IsSignedIn = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
