using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.Security;

/// <summary>
/// Names the signed-in application account as the author of audited changes, falling back to the
/// Windows identity when nobody has signed in.
/// </summary>
/// <remarks>
/// The fallback is not a convenience — it is what keeps the audit trail populated in the two states
/// where there is no application account to name: an install whose owner has not created the first
/// user yet, and the provisioning work that runs at first launch before any login prompt could have
/// been shown. <see cref="ICurrentUser"/>'s contract is that it must never resolve to blank, and an
/// unattributable change is not an audit trail.
/// <para>
/// The fallback identity is prefixed so a reader of the edit log can tell the two apart at a glance.
/// Without that, <c>RAHUL-PC\rahul</c> and an application account called <c>rahul</c> would be
/// indistinguishable in the one record that is supposed to settle who did what.
/// </para>
/// </remarks>
public sealed class SessionCurrentUser : ICurrentUser
{
    /// <summary>Marks an identity that came from Windows rather than from a sign-in.</summary>
    public const string UnauthenticatedPrefix = "(no sign-in) ";

    private readonly IUserSession _session;
    private readonly ICurrentUser _fallback;

    public SessionCurrentUser(IUserSession session, ICurrentUser fallback)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public string UserName
    {
        get
        {
            var current = _session.Current;

            return current is null
                ? UnauthenticatedPrefix + _fallback.UserName
                : current.UserName;
        }
    }
}
