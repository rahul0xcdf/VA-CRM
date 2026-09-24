using System.Globalization;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Every encoding rule the Tally file format imposes, in one place.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because of what phase-8-interoperability.md §4a D2 admits: no real Tally export
/// of the owner's books has been supplied yet (open-questions.md #22), so each rule below is written
/// from the published format and is <b>unverified on real data</b>. Scattering them through the
/// writers and readers would mean a wrong assumption had to be found and fixed in a dozen places
/// once a real file arrives. Collected here, each is one line.
/// </para>
/// <para>
/// The sign convention is the one most worth stating plainly, because it is invisible when wrong:
/// Tally writes a <b>debit as a negative</b> <c>AMOUNT</c> and a credit as a positive one, the
/// opposite of <c>JournalLine.AmountMinor</c>. A file written with the sign the intuitive way round
/// still parses, still balances, and reverses every entry in the accountant's books.
/// </para>
/// </remarks>
public static class TallyConventions
{
    /// <summary>Tally writes business dates as <c>yyyyMMdd</c> with no separators.</summary>
    public const string DateFormat = "yyyyMMdd";

    /// <summary>The envelope's request kind for an import payload.</summary>
    public const string ImportRequest = "Import Data";

    /// <summary>Report name for a masters payload.</summary>
    public const string MastersReportName = "All Masters";

    /// <summary>Report name for a transactions payload.</summary>
    public const string VouchersReportName = "Vouchers";

    /// <summary>
    /// Which report name an envelope declares for the payload it carries.
    /// </summary>
    /// <remarks>
    /// An envelope declares one report name, so a document carrying both masters and vouchers has
    /// to pick. It declares <see cref="VouchersReportName"/> and writes its masters first, relying
    /// on Tally reading the messages in document order so that a ledger exists by the time a
    /// voucher references it. That ordering dependency is the assumption in this file most worth
    /// re-checking against a real import (open-questions.md #22); if it does not hold, the fix is to
    /// write masters and vouchers as two envelopes, and it is this method that decides.
    /// </remarks>
    public static string ReportNameFor(InteropDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Vouchers.Count > 0 ? VouchersReportName : MastersReportName;
    }

    /// <summary>
    /// Tally's own action verb on a master or voucher element.
    /// </summary>
    /// <remarks>
    /// Always <c>Create</c>, never <c>Alter</c>. Tally's import overwrites an existing record when
    /// the GUID matches and creates one when it does not, which is precisely the behaviour C8.2
    /// depends on; sending <c>Alter</c> would fail for records the target company has never seen.
    /// </remarks>
    public const string CreateAction = "Create";

    public static string Bool(bool value) => value ? "Yes" : "No";

    public static bool ParseBool(string? value) =>
        value is not null && value.Trim().StartsWith('Y');

    public static string Date(DateOnly date) =>
        date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        // yyyyMMdd is what Tally writes. The dashed form is accepted on the way in because a file
        // that has been through a spreadsheet on its way to us often comes back reformatted, and
        // rejecting it would send the owner hunting for a difference that does not matter.
        return DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact)
            ? compact
            : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dashed)
                ? dashed
                : null;
    }

    /// <summary>
    /// Renders an amount held in this application's sign convention into Tally's.
    /// </summary>
    /// <remarks>
    /// Four decimal places, matching <see cref="MoneyScale.Places"/>. Tally's own display precision
    /// follows the currency's configuration — two for rupees — but the file carries what it is
    /// given, and C8.5 is about this application's round-trip, where four places in must be four
    /// places out.
    /// </remarks>
    public static string Amount(long amountMinor) =>
        MoneyScale.ToInvariantString(MoneyScale.FromMinor(-amountMinor));

    /// <summary>Reads a Tally amount back into this application's sign convention.</summary>
    public static long? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        // Tally rounds to its own currency precision, so a file can legitimately carry more places
        // than we store. Rounding here rather than letting MoneyScale.ToMinor throw is deliberate:
        // an over-precise amount in someone else's file is a data condition, not a defect in ours.
        return -MoneyScale.ToMinor(MoneyScale.Round(parsed));
    }

    /// <summary><c>ISDEEMEDPOSITIVE</c> is Yes for a debit.</summary>
    public static string DeemedPositive(long amountMinor) => Bool(amountMinor > 0);

    /// <summary>
    /// Renders a quantity the way Tally writes one: the number, then the unit symbol.
    /// </summary>
    public static string Quantity(long quantityMinor, string? unitCode)
    {
        var number = MoneyScale.ToInvariantString(MoneyScale.FromMinor(quantityMinor));
        return string.IsNullOrWhiteSpace(unitCode) ? number : $"{number} {unitCode}";
    }

    /// <summary>
    /// Splits a Tally quantity back into its number and its unit symbol.
    /// </summary>
    /// <remarks>
    /// Returns nulls rather than throwing on anything unparseable. This runs during the validation
    /// pass, where a bad value has to become a reported row (C8.4) rather than an exception that
    /// abandons the rest of the file — the whole point of validating everything before writing
    /// anything is that the owner gets the full list of problems in one pass.
    /// </remarks>
    public static (long? QuantityMinor, string? UnitCode) ParseQuantity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var text = value.Trim();
        var split = text.IndexOf(' ');
        var numberPart = split < 0 ? text : text[..split];
        var unitPart = split < 0 ? null : text[(split + 1)..].Trim();

        if (!decimal.TryParse(numberPart, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return (null, string.IsNullOrWhiteSpace(unitPart) ? null : unitPart);
        }

        return (MoneyScale.ToMinor(MoneyScale.Round(parsed)), string.IsNullOrWhiteSpace(unitPart) ? null : unitPart);
    }

    /// <summary>Tally's built-in voucher type name for one of the eight core behaviours.</summary>
    public static string VoucherTypeName(VoucherKind kind) => kind switch
    {
        VoucherKind.Payment => "Payment",
        VoucherKind.Receipt => "Receipt",
        VoucherKind.Contra => "Contra",
        VoucherKind.Journal => "Journal",
        VoucherKind.Sales => "Sales",
        VoucherKind.Purchase => "Purchase",
        VoucherKind.CreditNote => "Credit Note",
        VoucherKind.DebitNote => "Debit Note",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown voucher kind."),
    };

    /// <summary>
    /// Maps a voucher type name back to one of the eight behaviours.
    /// </summary>
    /// <remarks>
    /// Returns null rather than guessing. A Tally company routinely carries user-defined voucher
    /// types ("Bank Payment", "Cash Sales") whose behaviour is declared by their parent in the
    /// file's own voucher-type masters, and inferring one from a name we do not recognise would
    /// post real money through the wrong path. The importer asks the file; if the file does not
    /// say, that is a reported row.
    /// </remarks>
    public static VoucherKind? ParseVoucherKind(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalised = NameNormaliser.Normalise(name);

        foreach (var kind in Enum.GetValues<VoucherKind>())
        {
            if (NameNormaliser.Normalise(VoucherTypeName(kind)) == normalised)
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>
    /// Tally's parent name for an account group at the top of the tree.
    /// </summary>
    /// <remarks>
    /// Tally writes an empty <c>PARENT</c> for a primary group rather than omitting the element.
    /// </remarks>
    public const string PrimaryParent = "";

    /// <summary>Renders an entity id as the stable GUID Tally matches on.</summary>
    public static string GuidText(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Reads a GUID back, returning null for anything that is not one.</summary>
    public static Guid? ParseGuid(string? value) =>
        System.Guid.TryParse(value, out var parsed) && parsed != System.Guid.Empty ? parsed : null;
}
