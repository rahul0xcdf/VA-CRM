using System.Text.RegularExpressions;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// Removes credentials from text before it is logged or persisted
/// (phase-6-einvoice-eway.md §2, checkpoint C6.6).
/// </summary>
/// <remarks>
/// <para>
/// Two passes, because either alone leaks. The key pass catches secrets by the name of the field
/// carrying them, which handles anything NIC sends back in a shape we did not anticipate. The value
/// pass replaces the exact secret strings we know we are holding, which catches a secret that turns
/// up under a field name nobody thought of — an error message quoting the password back, say. Doing
/// only the first would miss that; doing only the second would miss a secret we never held in a
/// variable.
/// </para>
/// <para>
/// This runs at the client boundary, on the way in, so no raw body is ever handed to a logger or
/// written to the database. Redacting on the way out instead would mean the plaintext had already
/// been at rest, which is the thing the checkpoint is testing for.
/// </para>
/// </remarks>
public static partial class SensitiveText
{
    /// <summary>What a removed secret is replaced with.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>
    /// Shortest secret worth substring-matching. Below this, a "secret" is as likely to be a
    /// coincidental fragment of ordinary text, and blanking it would corrupt the record without
    /// protecting anything.
    /// </summary>
    private const int MinimumMatchableSecretLength = 6;

    /// <summary>
    /// Scrubs <paramref name="text"/>: any value under a credential-ish JSON key, plus any exact
    /// occurrence of <paramref name="secretValues"/>.
    /// </summary>
    public static string Redact(string? text, IEnumerable<string>? secretValues = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var scrubbed = CredentialKeyPattern().Replace(text, $"$1{Placeholder}$3");

        if (secretValues is null)
        {
            return scrubbed;
        }

        // Longest first, so a secret that contains another is not half-replaced into something that
        // still reveals the remainder.
        foreach (var secret in secretValues
                     .Where(value => !string.IsNullOrWhiteSpace(value)
                         && value.Length >= MinimumMatchableSecretLength)
                     .Distinct(StringComparer.Ordinal)
                     .OrderByDescending(value => value.Length))
        {
            scrubbed = scrubbed.Replace(secret, Placeholder, StringComparison.Ordinal);
        }

        return scrubbed;
    }

    /// <summary>
    /// True when <paramref name="text"/> still contains any of <paramref name="secretValues"/>. The
    /// test for checkpoint C6.6 asserts on this over the logs, the persisted call rows and the
    /// exports.
    /// </summary>
    public static bool ContainsAny(string? text, IEnumerable<string> secretValues)
    {
        ArgumentNullException.ThrowIfNull(secretValues);

        return !string.IsNullOrEmpty(text)
            && secretValues.Any(secret => !string.IsNullOrWhiteSpace(secret)
                && text.Contains(secret, StringComparison.Ordinal));
    }

    /// <summary>
    /// Matches a JSON string value sitting under a credential-ish key, capturing the key and the
    /// closing quote so the value alone can be swapped out.
    /// </summary>
    [GeneratedRegex(
        """("(?:client[_-]?id|client[_-]?secret|clientsecret|password|pwd|app[_-]?key|appkey|sek|auth[_-]?token|authtoken|token|user[_-]?name|username|usr)"\s*:\s*")([^"]*)(")""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialKeyPattern();
}
