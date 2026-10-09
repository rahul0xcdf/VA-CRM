using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Application.Security;

/// <summary>
/// Resolves permissions from the signed-in user's role, each time they are asked for.
/// </summary>
/// <remarks>
/// <b>Read fresh, not snapshotted.</b> Both the user row and the role are re-read on every check.
/// That costs two indexed single-row reads, which at the two or three counters §4a D1 sizes is
/// noise, and it buys two properties worth more than the saving: revoking a permission or
/// deactivating an account takes effect at once rather than at the holder's next sign-in, and a
/// role reassignment cannot be defeated by staying signed in.
/// <para>
/// <b>An install with no accounts grants everything.</b> That is the bootstrap state — a fresh
/// company before the owner has created the first user, and every Phase 1–8 test database — and it
/// is the one state in which this class protects nothing. It is deliberate (§4b) rather than an
/// oversight: enforcement has to begin somewhere, and refusing everything before the first account
/// exists would mean the first account could never be created. The moment one user row exists, an
/// unauthenticated caller is refused.
/// </para>
/// </remarks>
public sealed class PermissionAuthority : IPermissionAuthority
{
    private readonly IUserSession _session;
    private readonly IUnitOfWork _unitOfWork;

    public PermissionAuthority(IUserSession session, IUnitOfWork unitOfWork)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<bool> HasPermissionAsync(
        PermissionKind permission,
        CancellationToken cancellationToken = default)
    {
        var current = _session.Current;

        if (current is null)
        {
            // Nobody is signed in. Allowed only while the install has no accounts at all.
            var anyAccount = await _unitOfWork.Repository<AppUser>()
                .AnyAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return !anyAccount;
        }

        var user = await _unitOfWork.Repository<AppUser>()
            .GetByIdAsync(current.UserId, cancellationToken)
            .ConfigureAwait(false);

        // Deleted or deactivated mid-session: the session is stale and grants nothing.
        if (user is null || !user.IsActive)
        {
            return false;
        }

        // The user's own row, not the session's copy — a role reassignment must bite immediately.
        var role = await _unitOfWork.Repository<AppRole>()
            .SingleOrDefaultAsync(new RoleWithGrants(user.RoleId), cancellationToken)
            .ConfigureAwait(false);

        return role is not null && role.Allows(permission);
    }

    public async Task EnsureAsync(
        PermissionKind permission,
        CancellationToken cancellationToken = default)
    {
        if (await HasPermissionAsync(permission, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var current = _session.Current;

        throw current is null
            ? new PermissionDeniedException(
                $"'{permission}' requires a signed-in user, and nobody is signed in.")
            : new PermissionDeniedException(permission, current.UserName);
    }

    private sealed class RoleWithGrants : Specification<AppRole>
    {
        public RoleWithGrants(Guid roleId)
        {
            Where(role => role.Id == roleId);
            AddInclude(role => role.Grants);
        }
    }
}
