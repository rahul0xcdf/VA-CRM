using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Infrastructure.Security;

/// <summary>
/// Hashes passwords with PBKDF2-HMAC-SHA256 and a per-password random salt.
/// </summary>
/// <remarks>
/// <b>Why PBKDF2 and not a bare SHA-256.</b> The database is a file on a shop-floor PC (§4a D3), so
/// the realistic threat is somebody who has the file. A single round of SHA-256 over a password is
/// no obstacle to that person: ordinary hardware tests billions of candidates a second against a
/// fast hash. PBKDF2's iteration count is the whole point — it makes each guess cost what one
/// sign-in costs.
/// <para>
/// <b>The verifier is self-describing.</b> It stores the algorithm, iteration count and salt
/// alongside the derived key, so the cost can be raised later without a schema change and without
/// locking out existing accounts: <see cref="Verify"/> reads the parameters out of the stored value
/// instead of assuming today's. That is why <see cref="IPasswordHasher"/> exists as an interface at
/// all — see its remarks.
/// </para>
/// <para>
/// Comparison is fixed-time (<see cref="CryptographicOperations.FixedTimeEquals"/>). A short-circuit
/// byte comparison leaks how much of a candidate was right through its own timing, which over many
/// attempts is enough to recover the hash a byte at a time.
/// </para>
/// </remarks>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>Names both the algorithm and this encoding, so a future scheme is distinguishable.</summary>
    private const string Scheme = "PBKDF2-SHA256";

    /// <summary>
    /// OWASP's 2023 floor for PBKDF2-HMAC-SHA256. Raising this is safe: existing verifiers carry
    /// their own count and keep working.
    /// </summary>
    private const int DefaultIterations = 600_000;

    private const int SaltBytes = 16;

    private const int KeyBytes = 32;

    private static readonly char[] FieldSeparator = ['$'];

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Derive(password, salt, DefaultIterations);

        return string.Join(
            '$',
            Scheme,
            DefaultIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        // A malformed verifier fails the sign-in rather than throwing: whatever corrupted the row,
        // the operator's recourse is the same and a crashed login screen offers none of it.
        var fields = storedHash.Split(FieldSeparator, StringSplitOptions.None);

        if (fields.Length != 4
            || !string.Equals(fields[0], Scheme, StringComparison.Ordinal)
            || !int.TryParse(fields[1], CultureInfo.InvariantCulture, out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(fields[2]);
            expected = Convert.FromBase64String(fields[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = KeyBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            length);
}
