using System.Text;

namespace VinayakaAgencies.Domain.Common;

/// <summary>
/// Produces the comparison form of a master's name, which the unique <c>(CompanyId, Name…)</c>
/// indexes are built over.
/// </summary>
/// <remarks>
/// Collation differs between the two providers this suite must run on: SQLite's default
/// <c>BINARY</c> collation is case-<i>sensitive</i>, SQL Server's default is case-<i>insensitive</i>.
/// A unique index over the display name would therefore accept both "Cash" and "cash" under SQLite
/// for Phases 1–8, and then fail to create during the Phase 9 migration — with duplicates already in
/// the data. Indexing a normalised column instead makes uniqueness mean the same thing on both
/// providers, and keeps the decision in the domain rather than in a collation setting.
/// <para>
/// Whitespace is <i>collapsed</i>, not stripped: "Sundry Debtors" and "SundryDebtors" are different
/// names and must stay so. That is the deliberate difference from
/// <see cref="Accounting.VoucherRevision.Normalise"/>, which strips whitespace entirely because a
/// document number has no words to keep apart.
/// </para>
/// </remarks>
public static class NameNormaliser
{
    /// <summary>
    /// Trims, collapses each run of internal whitespace to a single space and upper-cases
    /// invariantly.
    /// </summary>
    public static string Normalise(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder(name.Length);
        var separatorPending = false;

        foreach (var c in name)
        {
            if (char.IsWhiteSpace(c))
            {
                separatorPending = builder.Length > 0;
                continue;
            }

            if (separatorPending)
            {
                builder.Append(' ');
                separatorPending = false;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }
}
