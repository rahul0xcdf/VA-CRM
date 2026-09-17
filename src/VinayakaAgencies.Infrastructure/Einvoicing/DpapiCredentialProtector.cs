using System.Security.Cryptography;
using System.Text;
using VinayakaAgencies.Application.Einvoicing;

namespace VinayakaAgencies.Infrastructure.Einvoicing;

/// <summary>
/// Encrypts the NIC credentials at rest with Windows DPAPI (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DataProtectionScope.CurrentUser"/>, not <c>LocalMachine</c>. Machine scope would let any
/// account on the box decrypt the credentials, including a service account nobody is watching; user
/// scope ties them to the Windows login that entered them. The cost is that the credentials must be
/// re-entered if the app is run as a different user, which for a single-user desktop install is the
/// correct trade and is written down in the feature notes.
/// </para>
/// <para>
/// An extra entropy string is mixed in so that ciphertext lifted out of the SQLite file cannot be
/// decrypted by a different application running as the same user — DPAPI alone would happily do that.
/// </para>
/// <para>
/// DPAPI is Windows-only. The guard in each method is written as
/// <see cref="OperatingSystem.IsWindows"/> rather than a <c>RuntimeInformation</c> check because the
/// platform-compatibility analyzer understands the former as narrowing the platform and the latter as
/// an ordinary boolean — so this form states the constraint to the compiler as well as to the reader.
/// Tests that must run anywhere use their own in-memory protector through
/// <see cref="ICredentialProtector"/>, which is one of the reasons that port exists.
/// </para>
/// </remarks>
internal sealed class DpapiCredentialProtector : ICredentialProtector
{
    /// <summary>
    /// Application-specific entropy. Not a secret — it is in the source — but it scopes the
    /// ciphertext to this application, which is what it is for.
    /// </summary>
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("VinayakaAgencies.Einvoicing.NicCredentials.v1");

    /// <inheritdoc />
    public byte[] Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "DPAPI credential protection is available on Windows only.");
        }

        return ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
    }

    /// <inheritdoc />
    public string Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.Length is 0)
        {
            throw new ArgumentException("There is no ciphertext to decrypt.", nameof(ciphertext));
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "DPAPI credential protection is available on Windows only.");
        }

        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException exception)
        {
            // The usual causes are a different Windows user, or a database file restored from another
            // machine. Saying that beats surfacing "the data is invalid".
            throw new InvalidOperationException(
                "The stored NIC credentials cannot be decrypted by this Windows user. They were "
                + "encrypted for the account that entered them — re-enter them on the E-invoice "
                + "settings screen.",
                exception);
        }
    }
}
