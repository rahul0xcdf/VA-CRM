using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Application.Security;

/// <summary>
/// Administration of other people's accounts and of roles.
/// </summary>
/// <remarks>
/// Every member requires <see cref="PermissionKind.ManageUsers"/>, checked in the implementation rather
/// than by the screens that call it. That is the whole of C9.2 and C9.5: the Users screen is only
/// reachable from a Gateway entry the shell can hide, and a hidden entry is not a permission —
/// invoking these methods directly, with no window in the process, has to be refused too.
/// </remarks>
public interface IUserAdminService
{
    /// <summary>Lists the company's accounts with their role names.</summary>
    /// <remarks>
    /// The company comes from <c>ICompanyContext</c> rather than a parameter, like every other
    /// service in this layer: no caller should have to carry a company id it cannot get wrong.
    /// </remarks>
    Task<IReadOnlyList<AppUserSummary>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the company's roles with their grants.</summary>
    Task<IReadOnlyList<AppRoleSummary>> ListRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an account, flagged to change its password at first sign-in.
    /// </summary>
    /// <remarks>
    /// The flag is not optional. The owner types the initial password, so the owner knows it; the
    /// flag is what makes that temporary rather than permanent, and it is the reason the audit
    /// trail's attribution to this account means anything afterwards.
    /// </remarks>
    Task<AppUserSummary> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renames an account, reassigns its role, or activates/deactivates it.</summary>
    Task<AppUserSummary> UpdateUserAsync(
        UpdateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a new temporary password the holder must replace at next sign-in.</summary>
    Task ResetPasswordAsync(
        Guid userId,
        string temporaryPassword,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a role, or replaces an existing one's name, description and grants.</summary>
    Task<AppRoleSummary> SaveRoleAsync(
        Guid? roleId,
        SaveRoleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a role.
    /// </summary>
    /// <remarks>
    /// Refused for a system role and for any role with users assigned — a user whose role has gone
    /// holds no permissions and cannot be told why.
    /// </remarks>
    Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
}
