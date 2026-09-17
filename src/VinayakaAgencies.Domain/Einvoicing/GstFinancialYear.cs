using System.Globalization;

namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Renders the <c>YYYY-YY</c> financial-year label that forms the second component of the IRN
/// (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// This is the one date rule in the codebase that is deliberately <em>not</em> driven by
/// <c>Company.FinancialYearStartMonth</c>. That column governs the books, and a future company on
/// this schema may keep books on some other cycle; the GST financial year is fixed at 1 April to
/// 31 March by the CGST Act. Feeding a company-specific cycle into an IRN would produce a hash the
/// portal computes differently, so the statutory year is used here and the books' year everywhere
/// else. This is a definition in statute, not a rate or a threshold, which is why it is not a
/// config row under non-negotiable #6 — nothing about it changes by notification.
/// </para>
/// <para>
/// The label is the first year in full and the second in two digits: 12 December 2019 is
/// <c>2019-20</c>, and 1 March 2020 — still the same year — is also <c>2019-20</c>.
/// </para>
/// </remarks>
public static class GstFinancialYear
{
    /// <summary>The month a GST financial year begins (April), per the CGST Act.</summary>
    public const int StartMonth = 4;

    /// <summary>The <c>YYYY-YY</c> label for the GST financial year containing <paramref name="date"/>.</summary>
    public static string For(DateOnly date)
    {
        var startYear = date.Month >= StartMonth ? date.Year : date.Year - 1;
        var endYear = startYear + 1;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{startYear:D4}-{endYear % 100:D2}");
    }
}
