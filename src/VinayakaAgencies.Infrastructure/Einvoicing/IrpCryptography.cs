using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VinayakaAgencies.Infrastructure.Einvoicing;

/// <summary>
/// The encryption NIC's e-invoice API requires: RSA for the auth payload, AES for everything after
/// (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The handshake NIC documents is: generate a random 32-byte app key; put it in the auth payload with
/// the username and password; RSA-encrypt that payload with NIC's published public certificate. The
/// portal answers with a blob encrypted under the app key, containing an auth token and a session
/// key (the "SEK"). Every business call afterwards is AES-encrypted under the SEK, and its response
/// decrypted the same way.
/// </para>
/// <para>
/// <b>ECB mode is NIC's choice, not ours.</b> AES-ECB with PKCS7 padding is what the portal expects,
/// and a payload encrypted any other way is rejected. It is called out here so nobody later "fixes"
/// it to CBC and spends a day on an opaque authentication failure — and so it is on the record that
/// the weakness is in the protocol we are required to speak, not in a choice made here. It is
/// mitigated by what it protects: a payload that travels over TLS anyway, whose blocks are unique per
/// call.
/// </para>
/// <para>
/// This class is deliberately pure and static. Nothing in it reaches the network, so the round trip
/// can be tested without credentials — which, while <c>open-questions.md</c> #10 is open, is the only
/// verification available for it.
/// </para>
/// </remarks>
internal static class IrpCryptography
{
    /// <summary>Length of the app key NIC expects: 256 bits.</summary>
    public const int AppKeyLengthBytes = 32;

    /// <summary>A fresh random app key for one authentication.</summary>
    public static byte[] CreateAppKey() => RandomNumberGenerator.GetBytes(AppKeyLengthBytes);

    /// <summary>
    /// RSA-encrypts the auth payload with NIC's public certificate, returning base64 as the portal
    /// wants it.
    /// </summary>
    public static string EncryptWithPublicCertificate(string payloadJson, string certificateBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateBase64);

        using var certificate = new X509Certificate2(Convert.FromBase64String(certificateBase64));
        using var rsa = certificate.GetRSAPublicKey()
            ?? throw new InvalidOperationException(
                "The configured NIC certificate carries no RSA public key.");

        // PKCS#1 v1.5 is what the portal's own utilities use. OAEP would be the better padding and is
        // not what the other end implements.
        var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(payloadJson), RSAEncryptionPadding.Pkcs1);

        return Convert.ToBase64String(encrypted);
    }

    /// <summary>AES-encrypts <paramref name="plaintext"/> under <paramref name="key"/>, base64-encoded.</summary>
    public static string EncryptToBase64(string plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);

        using var aes = CreateAes(key);
        using var transform = aes.CreateEncryptor();

        var input = Encoding.UTF8.GetBytes(plaintext);

        return Convert.ToBase64String(transform.TransformFinalBlock(input, 0, input.Length));
    }

    /// <summary>Decrypts a base64 AES blob under <paramref name="key"/>.</summary>
    public static string DecryptFromBase64(string base64Ciphertext, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Ciphertext);
        ArgumentNullException.ThrowIfNull(key);

        using var aes = CreateAes(key);
        using var transform = aes.CreateDecryptor();

        var input = Convert.FromBase64String(base64Ciphertext);

        return Encoding.UTF8.GetString(transform.TransformFinalBlock(input, 0, input.Length));
    }

    /// <summary>
    /// Decrypts the session key the portal returns. It arrives encrypted under the app key we sent,
    /// which is the only thing the app key is ever used for.
    /// </summary>
    public static byte[] DecryptSessionKey(string base64EncryptedSek, byte[] appKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64EncryptedSek);
        ArgumentNullException.ThrowIfNull(appKey);

        using var aes = CreateAes(appKey);
        using var transform = aes.CreateDecryptor();

        var input = Convert.FromBase64String(base64EncryptedSek);
        var decrypted = transform.TransformFinalBlock(input, 0, input.Length);

        // The portal sends the SEK as base64 text inside the encrypted envelope, so there are two
        // layers to peel and only the inner one is the key material.
        return Convert.FromBase64String(Encoding.UTF8.GetString(decrypted));
    }

    private static Aes CreateAes(byte[] key)
    {
        var aes = Aes.Create();

        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        return aes;
    }
}
