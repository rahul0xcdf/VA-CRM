using System.Globalization;
using System.Text;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Banking;

/// <summary>
/// Renders a rupee amount the way a cheque has to carry it — in the Indian numbering system
/// (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>In the domain, and not in the printer.</b> The words on a cheque are the legally operative
/// amount: where the figures and the words disagree, the words govern. That makes this arithmetic
/// with a rule, and ADR-0007's standing instruction is that a rule lives where a unit test can
/// reach it without a window. It is also why the printer does not do its own conversion — two
/// renderings of one payment must not word it differently.
/// </para>
/// <para>
/// <b>Indian grouping, not Western.</b> Hundreds, then thousands, then lakhs (10^5), then crores
/// (10^7) — so ₹12,34,567 is "Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven", never
/// "One Million…". Beyond a crore the system repeats: 10^9 is "One Hundred Crore" and not a new
/// word, which is why the crore group is allowed to run to seven digits of its own rather than
/// being capped at ninety-nine.
/// </para>
/// </remarks>
public static class IndianAmountInWords
{
    private static readonly string[] Ones =
    [
        string.Empty, "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen",
        "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens =
    [
        string.Empty, string.Empty, "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy",
        "Eighty", "Ninety",
    ];

    /// <summary>
    /// The cheque form of an amount: "Rupees … Only", with paise where there are any.
    /// </summary>
    /// <remarks>
    /// The trailing "Only" is not decoration. It is what stops a written amount being extended by
    /// somebody adding words after it, and every printed Indian cheque carries it.
    /// </remarks>
    public static string ForCheque(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "A cheque cannot be written for a negative amount.");
        }

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rupees = decimal.Truncate(rounded);
        var paise = (int)decimal.Round((rounded - rupees) * 100m, 0, MidpointRounding.AwayFromZero);

        // Rounding the paise can itself carry into the rupee — 99.999 becomes 100.00, not 99.100.
        if (paise == 100)
        {
            rupees += 1m;
            paise = 0;
        }

        var builder = new StringBuilder("Rupees ");
        builder.Append(Convert((long)rupees));

        if (paise > 0)
        {
            builder.Append(" and ").Append(Convert(paise)).Append(" Paise");
        }

        return builder.Append(" Only").ToString();
    }

    /// <summary>The words for a whole number, with no currency and no terminator.</summary>
    public static string Convert(long value)
    {
        if (value < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "Negative amounts have no cheque wording.");
        }

        if (value == 0L)
        {
            return "Zero";
        }

        var parts = new List<string>(5);

        // The crore group is not capped at two digits: 10^9 is "One Hundred Crore", so whatever is
        // left above a crore is itself spoken in the ordinary way and then labelled.
        var crore = value / 10_000_000L;
        var remainder = value % 10_000_000L;

        if (crore > 0L)
        {
            parts.Add($"{Convert(crore)} Crore");
        }

        var lakh = remainder / 100_000L;
        remainder %= 100_000L;

        if (lakh > 0L)
        {
            parts.Add($"{BelowHundred((int)lakh)} Lakh");
        }

        var thousand = remainder / 1_000L;
        remainder %= 1_000L;

        if (thousand > 0L)
        {
            parts.Add($"{BelowHundred((int)thousand)} Thousand");
        }

        var hundred = remainder / 100L;
        remainder %= 100L;

        if (hundred > 0L)
        {
            parts.Add($"{Ones[hundred]} Hundred");
        }

        if (remainder > 0L)
        {
            parts.Add(BelowHundred((int)remainder));
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// The figures form, grouped the Indian way: ₹12,34,567.00 rather than ₹1,234,567.00.
    /// </summary>
    /// <remarks>
    /// Built from the invariant culture with explicit group sizes rather than from an "en-IN"
    /// culture lookup, because the machine the software runs on may not have that culture
    /// installed — and a cheque that prints Western grouping on one machine and Indian on another
    /// is the kind of defect nobody notices until a bank returns one.
    /// </remarks>
    public static string ToIndianFigures(decimal amount)
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();

        // 3 then 2 repeating: the last group is three digits, every group above it is two.
        format.NumberGroupSizes = [3, 2];
        format.NumberDecimalDigits = 2;

        return amount.ToString("N", format);
    }

    /// <summary>The words for 1–99, where the teens are irregular and the tens are not.</summary>
    private static string BelowHundred(int value) => value switch
    {
        < 20 => Ones[value],
        _ when value % 10 == 0 => Tens[value / 10],
        _ => $"{Tens[value / 10]} {Ones[value % 10]}",
    };

    /// <summary>The cheque wording of an amount held in minor units (ADR-0003).</summary>
    public static string ForChequeMinor(long amountMinor) =>
        ForCheque(MoneyScale.FromMinor(amountMinor));
}
