namespace VinayakaAgencies.Application.Security;

/// <summary>
/// Establishes and clears the signed-in session, and changes the signed-in user's own password.
/// </summary>
/// <remarks>
/// Separate from <see cref="IUserAdminService"/> on purpose. Everything here is something a user
/// does to their own session and needs no permission; everything there is administration of other
/// people's accounts and requires <see cref="Domain.Security.PermissionKind.ManageUsers"/>. Putting
/// them in one service would mean the sign-in path sat behind a permission check that cannot
/// succeed before sign-in.
/// </remarks>
public interface ISignInService
{
    /// <summary>
    /// Whether the install has any accounts at all.
    /// </summary>
    /// <remarks>
    /// The shell asks this to decide between showing the login prompt and the create-first-account
    /// prompt. It is also the condition under which <see cref="Domain.Abstractions.IPermissionAuthority"/>
    /// grants everything, so the two cannot disagree.
    /// </remarks>
    Task<bool> AnyAccountsExistAsync(CancellationToken cancellationToken = default);

    /// <summary>Verifies credentials and, on success, establishes the session.</summary>
    Task<SignInOutcome> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the session.</summary>
    void SignOut();

    /// <summary>Changes the signed-in user's own password.</summary>
    Task<ChangePasswordOutcome> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the first account, which is assigned the owner role.
    /// </summary>
    /// <remarks>
    /// Needs no permission, and must not: before it runs there is no account to hold one. It refuses
    /// once any account exists, which is what stops it being a path to a second owner account for
    /// anyone who can reach the service.
    /// </remarks>
    Task<SignInOutcome> CreateFirstAccountAsync(
        Guid companyId,
        string userName,
        string displayName,
        string password,
        CancellationToken cancellationToken = default);
}
