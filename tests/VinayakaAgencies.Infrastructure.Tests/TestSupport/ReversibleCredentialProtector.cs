using System.Text;
using VinayakaAgencies.Application.Einvoicing;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// A credential protector the tests can round-trip, standing in for DPAPI
/// (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// DPAPI encrypts for one Windows user, which makes it unusable in a test that must pass on any
/// machine and in CI. This substitutes a reversible transform so the storage path can be exercised
/// end to end.
/// </para>
/// <para>
/// It is deliberately <b>not</b> encryption, and the ciphertext it produces is deliberately not the
/// plaintext either: the redaction tests need stored bytes that do not literally contain the secret,
/// or they would pass for the wrong reason. Reversing the bytes achieves exactly that and nothing
/// more. Nothing outside the test project may use this type.
/// </para>
/// </remarks>
internal sealed class ReversibleCredentialProtector : ICredentialProtector
{
    public byte[] Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var bytes = Encoding.UTF8.GetBytes(plaintext);

        Array.Reverse(bytes);

        return bytes;
    }

    public string Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        var bytes = ciphertext.ToArray();

        Array.Reverse(bytes);

        return Encoding.UTF8.GetString(bytes);
    }
}
