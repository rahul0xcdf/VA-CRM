using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// A hash of the two statements checkpoint C10.6 names: the trial balance and the stock summary.
/// </summary>
/// <remarks>
/// <para>
/// C10.6 asks that a restore reproduce a <i>byte-identical</i> trial balance and stock summary. The
/// honest way to make that claim testable is to render both statements to a canonical byte sequence
/// and hash it: if the hash taken before the backup equals the hash recomputed after the restore,
/// the two renderings were byte-for-byte the same, and no weaker comparison was substituted for the
/// one the checkpoint asked for.
/// </para>
/// <para>
/// Why a hash rather than comparing the reports directly: the fingerprint is computed at backup
/// time and stored in the archive's manifest, so it survives to be checked months later against a
/// database the original process no longer has in memory — which is the situation a real restore
/// happens in. The totals and row counts travel beside the hash because a mismatched hash on its
/// own tells an operator nothing, whereas "4,181 rows became 4,180" tells them what to look for.
/// </para>
/// <para>
/// Canonicalisation rules, all of which exist because their absence would make a correct restore
/// look broken: rows are ordered by identifier rather than by whatever order the query returned;
/// every number is rendered with a fixed scale under <see cref="CultureInfo.InvariantCulture"/>, so
/// a machine with different regional settings hashes the same books identically; and the separator
/// is a character that cannot occur in a name.
/// </para>
/// </remarks>
public sealed record BookFingerprint(
    string TrialBalanceHash,
    string StockSummaryHash,
    int TrialBalanceRowCount,
    int StockSummaryRowCount,
    decimal TotalDebit,
    decimal TotalCredit,
    long TotalStockValueMinor)
{
    /// <summary>
    /// Field separator: the ASCII unit separator. Nothing typed into a ledger or item name can
    /// produce it, so no name can forge a field boundary and make two different books hash alike.
    /// </summary>
    private const char Separator = '';

    /// <summary>
    /// The fingerprint of a book with nothing in it, for a restore of an archive taken before any
    /// posting existed.
    /// </summary>
    public static BookFingerprint Empty { get; } = new(
        HashOf([]),
        HashOf([]),
        TrialBalanceRowCount: 0,
        StockSummaryRowCount: 0,
        TotalDebit: 0m,
        TotalCredit: 0m,
        TotalStockValueMinor: 0L);

    /// <summary>Whether two fingerprints describe the same books.</summary>
    /// <remarks>
    /// Compares the hashes and the counts, not the record as a whole: a future field added for
    /// diagnostics must not be able to turn a correct restore into a failure.
    /// </remarks>
    public bool Matches(BookFingerprint? other) =>
        other is not null
        && string.Equals(TrialBalanceHash, other.TrialBalanceHash, StringComparison.Ordinal)
        && string.Equals(StockSummaryHash, other.StockSummaryHash, StringComparison.Ordinal)
        && TrialBalanceRowCount == other.TrialBalanceRowCount
        && StockSummaryRowCount == other.StockSummaryRowCount;

    /// <summary>
    /// Names the first difference between this fingerprint and <paramref name="other"/>, for an
    /// operator who needs to know what changed rather than that something did.
    /// </summary>
    public string Describe(BookFingerprint? other)
    {
        if (other is null)
        {
            return "The archive carries no fingerprint to compare against.";
        }

        if (Matches(other))
        {
            return "The trial balance and stock summary are identical.";
        }

        var differences = new List<string>();

        if (TrialBalanceRowCount != other.TrialBalanceRowCount)
        {
            differences.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"trial balance rows {TrialBalanceRowCount} vs {other.TrialBalanceRowCount}"));
        }

        if (TotalDebit != other.TotalDebit || TotalCredit != other.TotalCredit)
        {
            differences.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"totals {TotalDebit}/{TotalCredit} vs {other.TotalDebit}/{other.TotalCredit}"));
        }

        if (StockSummaryRowCount != other.StockSummaryRowCount)
        {
            differences.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"stock rows {StockSummaryRowCount} vs {other.StockSummaryRowCount}"));
        }

        if (TotalStockValueMinor != other.TotalStockValueMinor)
        {
            differences.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"stock value {TotalStockValueMinor} vs {other.TotalStockValueMinor} minor units"));
        }

        // Every count and total agreed but a hash did not: the figures moved between rows without
        // changing the shape of the statement. Worth saying so rather than reporting nothing.
        if (differences.Count == 0)
        {
            differences.Add("the same number of rows with the same totals, but different contents");
        }

        return "The restored books differ: " + string.Join("; ", differences) + ".";
    }

    /// <summary>
    /// Hashes a canonical rendering of <paramref name="lines"/>: SHA-256 over UTF-8, upper-case hex.
    /// </summary>
    internal static string HashOf(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var text = new StringBuilder();

        foreach (var line in lines)
        {
            text.Append(line).Append('\n');
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToUpperInvariant();
    }

    /// <summary>Joins pre-rendered fields with the separator that cannot appear inside one.</summary>
    internal static string Line(params string[] fields) =>
        string.Join(Separator, fields ?? []);
}
