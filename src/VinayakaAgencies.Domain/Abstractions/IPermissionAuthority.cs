using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Domain.Abstractions;

/// <summary>
/// Answers whether the current user may perform an action, and refuses it if not.
/// </summary>
/// <remarks>
/// This is the enforcement point `phase-9-multiuser.md` §2 requires: "permissions are enforced in
/// the domain/service layer, not by hiding UI. A hidden button is not a permission." C9.5 is the
/// test of that — a permission-denied action invoked straight at the service layer, with no window
/// in the process, must still be refused.
/// <para>
/// <b>Bootstrap behaviour.</b> When the company has no user accounts at all, every permission is
/// granted. That is what makes a fresh install usable before the owner has created the first
/// account, and it is why Phases 1–8 and their test suites are unaffected by this phase: a database
/// with no users enforces nothing, exactly as before. Enforcement begins with the first account.
/// Recorded as a deliberate decision in §4b, because it is also the one state in which this
/// interface protects nothing.
/// </para>
/// </remarks>
public interface IPermissionAuthority
{
    /// <summary>Whether the current user holds <paramref name="permission"/>.</summary>
    Task<bool> HasPermissionAsync(
        PermissionKind permission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns if the current user holds <paramref name="permission"/>, and throws otherwise.
    /// </summary>
    /// <exception cref="PermissionDeniedException">The permission is not held.</exception>
    Task EnsureAsync(PermissionKind permission, CancellationToken cancellationToken = default);
}
