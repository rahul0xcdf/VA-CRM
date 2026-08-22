using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Splits a voucher number back into its numeric part, given the series that issued it.
/// </summary>
/// <remarks>
/// One parser serves both writers and readers: the allocator validates a manual override against it,
/// and the §8.7 numbering-gap report uses it to recover the sequence positions a series has actually
/// reached. Parsing lives here rather than in SQL deliberately — prefix and suffix handling is
/// business logic, and putting it in the dialect layer would mean rewriting it when Phase 9 adds a
/// second provider.
/// </remarks>
public static class VoucherNumberParser
{
    /// <summary>
    /// Extracts the sequence number from <paramref name="supplied"/>, which may be the full
    /// formatted form (<c>PMT/0017</c>) or the bare digits (<c>17</c>).
    /// </summary>
    public static bool TryParse(VoucherNumberSeries series, string supplied, out long numeric)
    {
        numeric = 0;

        var digits = supplied.Trim();

        if (series.Prefix.Length > 0)
        {
            if (!digits.StartsWith(series.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            digits = digits[series.Prefix.Length..];
        }

        if (series.Suffix is { } suffix && suffix.Length > 0)
        {
            if (!digits.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            digits = digits[..^suffix.Length];
        }

        return long.TryParse(
            digits,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out numeric);
    }
}
