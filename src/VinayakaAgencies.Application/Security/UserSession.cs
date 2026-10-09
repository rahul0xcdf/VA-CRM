using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Application.Security;

/// <summary>
/// Holds the signed-in identity for the lifetime of the client process.
/// </summary>
/// <remarks>
/// Guarded by a lock even though a desktop client has one operator: sign-out can be raised from the
/// UI thread while a background report is mid-read, and a torn read here would hand a permission
/// check the wrong role rather than merely a stale one.
/// </remarks>
public sealed class UserSession : IUserSession
{
    private readonly object _gate = new();

    private SignedInUser? _current;

    public SignedInUser? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool IsSignedIn => Current is not null;

    public event EventHandler? Changed;

    public void SignIn(SignedInUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (_gate)
        {
            _current = user;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        lock (_gate)
        {
            _current = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
