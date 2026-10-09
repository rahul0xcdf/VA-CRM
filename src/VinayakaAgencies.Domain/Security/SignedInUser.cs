namespace VinayakaAgencies.Domain.Security;

/// <summary>
/// The identity established by a successful sign-in.
/// </summary>
/// <remarks>
/// Deliberately carries no password material and no permission set. The password is gone the moment
/// it has been verified, and permissions are resolved from the role on each check rather than
/// snapshotted here — otherwise revoking a right would not bite until the holder next signed out,
/// which on a shop floor could be days. See <see cref="Abstractions.IPermissionAuthority"/>.
/// </remarks>
/// <param name="UserId">The <see cref="AppUser"/> row.</param>
/// <param name="CompanyId">The company the account belongs to.</param>
/// <param name="UserName">What was typed at the prompt, in its stored casing.</param>
/// <param name="DisplayName">The name shown on screen and stamped on the audit trail.</param>
/// <param name="RoleId">The <see cref="AppRole"/> whose grants are checked.</param>
/// <param name="MustChangePassword">
/// Set when the account was created or reset by the owner. The shell routes to the change-password
/// prompt rather than refusing the sign-in, because an operator who cannot get in also cannot
/// change their password.
/// </param>
public sealed record SignedInUser(
    Guid UserId,
    Guid CompanyId,
    string UserName,
    string DisplayName,
    Guid RoleId,
    bool MustChangePassword);
