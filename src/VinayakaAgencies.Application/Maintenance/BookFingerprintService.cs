using System.Globalization;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// Renders the two statements C10.6 names and hashes them (see <see cref="BookFingerprint"/>).
/// </summary>
/// <remarks>
/// <para>
/// Both statements are read through the ordinary report services rather than through a query
/// written for this purpose. That is the point: a fingerprint computed by its own SQL would prove
/// that <i>that</i> SQL round-tripped, which is not what the checkpoint asks. Reading the same
/// services the Trial Balance and Stock Summary screens read means the thing being compared is the
/// thing the operator would look at.
/// </para>
/// <para>
/// <b>The trial balance is financial-year scoped</b>, and that shapes this whole class.
/// <c>GetTrialBalanceAsync</c> accumulates from the start of the financial year containing the date
/// it is given, so there is no single "as on" date that means "the whole book" — asking for one far
/// in the future returns an empty statement, because the year it implies has no postings in it.
/// A fingerprint built that way would hash an empty report, match itself after any restore
/// whatsoever, and quietly carry no information at all. So the whole book is covered the only way
/// it can be: one trial balance per financial year that has something in it, oldest first,
/// concatenated. A multi-year migration (Phase 8) is therefore checked year by year.
/// </para>
/// <para>
/// Stock on-hand is cumulative rather than year-scoped, so it is taken once, as at the end of the
/// latest year on record.
/// </para>
/// </remarks>
public sealed class BookFingerprintService(
    IAccountingReportService accountingReports,
    IStockReportService stockReports,
    IFinancialYearProvider financialYears) : IBookFingerprintService
{
    /// <summary>Scale every figure is rendered at — ADR-0003's four decimal places.</summary>
    private const string MoneyFormat = "F4";

    private readonly IAccountingReportService _accountingReports = accountingReports
        ?? throw new ArgumentNullException(nameof(accountingReports));

    private readonly IStockReportService _stockReports = stockReports
        ?? throw new ArgumentNullException(nameof(stockReports));

    private readonly IFinancialYearProvider _financialYears = financialYears
        ?? throw new ArgumentNullException(nameof(financialYears));

    public async Task<BookFingerprint> ComputeAsync(CancellationToken cancellationToken = default)
    {
        // Years exist only once something has been posted into them (§12), so this list is exactly
        // the set of years the books actually span — no empty years to hash, none missed.
        var years = (await _financialYears.ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(year => year.StartDate)
            .ToList();

        if (years.Count == 0)
        {
            // Nothing posted. Hashing the empty statements would be correct but indistinguishable
            // from a failure to read them, so the fingerprint of an untouched book is explicit.
            return BookFingerprint.Empty;
        }

        var trialBalanceLines = new List<string>();
        var totalDebit = 0m;
        var totalCredit = 0m;
        var rowCount = 0;

        foreach (var year in years)
        {
            var trialBalance = await _accountingReports
                .GetTrialBalanceAsync(year.EndDate, cancellationToken)
                .ConfigureAwait(false);

            // The year label is inside the hash. Without it, two years whose rows happened to be
            // identical would hash the same as one year posted twice.
            trialBalanceLines.Add(BookFingerprint.Line(
                "YEAR",
                year.Label,
                year.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                year.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

            // Ordered by identifier, not by the order the query happened to return. Two restores of
            // the same archive must hash alike even if a future index changes the natural row
            // order, and a GUID ordering is stable where a name ordering moves on a rename.
            trialBalanceLines.AddRange(trialBalance.Rows
                .OrderBy(row => row.LedgerId)
                .Select(row => BookFingerprint.Line(
                    row.LedgerId.ToString("D", CultureInfo.InvariantCulture),
                    row.LedgerName,
                    row.AccountGroupName,
                    row.Nature.ToString(),
                    row.Debit.ToString(MoneyFormat, CultureInfo.InvariantCulture),
                    row.Credit.ToString(MoneyFormat, CultureInfo.InvariantCulture))));

            // The totals are inside the hash as well as beside it, so a restore that dropped a
            // ledger and coincidentally produced the same row hash would still have to produce the
            // same totals.
            trialBalanceLines.Add(BookFingerprint.Line(
                "TOTAL",
                trialBalance.TotalDebit.ToString(MoneyFormat, CultureInfo.InvariantCulture),
                trialBalance.TotalCredit.ToString(MoneyFormat, CultureInfo.InvariantCulture)));

            totalDebit += trialBalance.TotalDebit;
            totalCredit += trialBalance.TotalCredit;
            rowCount += trialBalance.Rows.Count;
        }

        var stockAsOf = years[^1].EndDate;

        // Guards against a book whose latest year ends in the future relative to the clock: on-hand
        // as at a date later than today would read the same, but the date goes into the hash, so
        // pinning it to the year end keeps the figure reproducible on any later day.
        var onHand = await _stockReports
            .GetOnHandAsync(stockAsOf, godownId: null, cancellationToken)
            .ConfigureAwait(false);

        var stockLines = new List<string>
        {
            BookFingerprint.Line("ASOF", stockAsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        };

        stockLines.AddRange(onHand
            .OrderBy(row => row.ItemId)
            .ThenBy(row => row.GodownId)
            .Select(row => BookFingerprint.Line(
                row.ItemId.ToString("D", CultureInfo.InvariantCulture),
                row.GodownId.ToString("D", CultureInfo.InvariantCulture),
                row.OnHand.ToString(MoneyFormat, CultureInfo.InvariantCulture),

                // Minor units, exactly as stored (ADR-0003). Hashing a converted decimal would put
                // a rounding step inside the comparison.
                row.ValueMinor.ToString(CultureInfo.InvariantCulture))));

        return new BookFingerprint(
            BookFingerprint.HashOf(trialBalanceLines),
            BookFingerprint.HashOf(stockLines),
            rowCount,
            onHand.Count,
            totalDebit,
            totalCredit,
            onHand.Sum(row => row.ValueMinor));
    }
}
