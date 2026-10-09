using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Application.Security;

/// <summary>Why a sign-in attempt did not establish a session.</summary>
/// <remarks>
/// <see cref="InvalidCredentials"/> covers both an unknown account and a wrong password, and the
/// screen must not distinguish them. Telling a stranger that a user name exists hands them half the
/// answer, and on a 2–3 person install the set of valid names is small enough for that to matter.
/// </remarks>
public enum SignInFailure
{
    None,

    /// <summary>No such account, or the password did not match. Deliberately indistinguishable.</summary>
    InvalidCredentials,

    /// <summary>The account exists and the password matched, but it has been deactivated.</summary>
    AccountInactive,
}

/// <param name="SignedIn">The established identity, or <c>null</c> when <paramref name="Failure"/> is set.</param>
/// <param name="Failure">Why the attempt failed, or <see cref="SignInFailure.None"/>.</param>
/// <param name="Message">Operator-facing text for the login screen's status line.</param>
public sealed record SignInOutcome(SignedInUser? SignedIn, SignInFailure Failure, string Message)
{
    public bool Succeeded => SignedIn is not null && Failure == SignInFailure.None;

    public static SignInOutcome Success(SignedInUser user) =>
        new(user, SignInFailure.None, $"Signed in as {user.DisplayName}.");

    public static SignInOutcome Failed(SignInFailure failure, string message) =>
        new(null, failure, message);
}

/// <param name="Succeeded">Whether the password was changed.</param>
/// <param name="Errors">What was wrong, for display; empty when <paramref name="Succeeded"/>.</param>
public sealed record ChangePasswordOutcome(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static ChangePasswordOutcome Success() => new(true, []);

    public static ChangePasswordOutcome Failed(params string[] errors) => new(false, errors);
}

/// <summary>A user account as the administration screen lists it.</summary>
/// <remarks>Carries no password material — there is nothing to show and nothing to leak.</remarks>
public sealed record AppUserSummary(
    Guid Id,
    Guid CompanyId,
    string UserName,
    string DisplayName,
    Guid RoleId,
    string RoleName,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset? LastSignInAtUtc);

/// <summary>A role and the permissions it grants.</summary>
public sealed record AppRoleSummary(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsOwnerRole,
    IReadOnlyList<PermissionKind> Grants);

/// <param name="UserName">What the operator will type at the prompt; unique per company.</param>
/// <param name="DisplayName">Shown on screen and stamped on the audit trail.</param>
/// <param name="Password">
/// The initial password. Stored only as a verifier, and the account is flagged
/// <c>MustChangePassword</c> so the owner never knows the operator's working password.
/// </param>
/// <param name="RoleId">The role whose grants the account will carry.</param>
public sealed record CreateUserRequest(
    string UserName,
    string DisplayName,
    string Password,
    Guid RoleId);

/// <param name="Id">The account to change.</param>
/// <param name="UserName">The new sign-in name.</param>
/// <param name="DisplayName">The new display name.</param>
/// <param name="RoleId">The role to assign.</param>
/// <param name="IsActive">Whether the account may sign in.</param>
public sealed record UpdateUserRequest(
    Guid Id,
    string UserName,
    string DisplayName,
    Guid RoleId,
    bool IsActive);

/// <param name="Name">Unique per company.</param>
/// <param name="Description">Free text for the administration screen.</param>
/// <param name="Grants">The permissions the role carries.</param>
public sealed record SaveRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<PermissionKind> Grants);
