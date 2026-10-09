using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Domain.Abstractions;

/// <summary>
/// Who is signed in to this process.
/// </summary>
/// <remarks>
/// One per running client, not one per request: a WPF desktop client has a single operator at the
/// keyboard, so this is registered as a singleton and <see cref="ICurrentUser"/> reads through it.
/// That is what makes the audit trail name the application account rather than the shared Windows
/// login — the point of §4a D2.
/// </remarks>
public interface IUserSession
{
    /// <summary>The signed-in user, or <c>null</c> before sign-in and after sign-out.</summary>
    SignedInUser? Current { get; }

    /// <summary>Whether anybody is signed in.</summary>
    bool IsSignedIn { get; }

    /// <summary>Raised when <see cref="Current"/> changes, so the shell can re-read its bars.</summary>
    event EventHandler? Changed;

    /// <summary>Establishes <paramref name="user"/> as the signed-in identity.</summary>
    void SignIn(SignedInUser user);

    /// <summary>Clears the session.</summary>
    void SignOut();
}
