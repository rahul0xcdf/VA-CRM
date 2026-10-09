namespace VinayakaAgencies.Domain.Abstractions;

/// <summary>
/// Turns a password into a stored verifier, and checks a password against one.
/// </summary>
/// <remarks>
/// An abstraction rather than a static helper for one reason that matters more than testability:
/// the cost parameter of a password hash has to be raiseable without a schema change, because what
/// is expensive enough today is cheap in five years. The verifier therefore carries its own
/// parameters and <see cref="Verify"/> reads them from the stored value, so raising the cost affects
/// new and changed passwords and leaves existing accounts able to sign in.
/// <para>
/// Deliberately <b>not</b> <c>ICredentialProtector</c> from Phase 6. That protects secrets this
/// application must replay to the IRP and so has to be reversible; a password must never be
/// recoverable, by us or by anyone holding the database file.
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Produces the verifier to persist for <paramref name="password"/>.</summary>
    string Hash(string password);

    /// <summary>
    /// Whether <paramref name="password"/> matches <paramref name="storedHash"/>.
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing on a malformed or unrecognised stored value: a corrupt
    /// verifier must fail the sign-in, not crash the login screen into a state the operator cannot
    /// get out of.
    /// </remarks>
    bool Verify(string password, string storedHash);
}
