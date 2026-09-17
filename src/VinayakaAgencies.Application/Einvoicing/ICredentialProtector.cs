namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// Encrypts and decrypts the NIC credentials at rest (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// A port, because the spec names DPAPI and DPAPI is a Windows platform service: the Application
/// layer must not know that, and Phase 9's move to a multi-user server is exactly the point at which
/// a machine-scoped Windows API stops being the right answer. Keeping it behind this interface means
/// that swap replaces one Infrastructure class.
/// <para>
/// Plaintext crosses this boundary and nowhere else. Callers decrypt immediately before a call and
/// let the value go out of scope after it, so nothing long-lived holds a readable secret
/// (checkpoint C6.6).
/// </para>
/// </remarks>
public interface ICredentialProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> for storage.</summary>
    byte[] Protect(string plaintext);

    /// <summary>Decrypts a value produced by <see cref="Protect"/>.</summary>
    string Unprotect(byte[] ciphertext);
}
