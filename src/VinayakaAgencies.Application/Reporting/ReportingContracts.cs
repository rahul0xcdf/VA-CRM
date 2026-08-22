using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Reporting;

/// <summary>One posting as it appears in the day book.</summary>
public sealed record DayBookRow(
    DateOnly VoucherDate,
    string VoucherNumber,
    string VoucherTypeName,
    VoucherKind Kind,
    string LedgerName,
    decimal Debit,
    decimal Credit,
    string? Narration,
    Guid VoucherId,
    bool IsCancelled);

/// <summary>One ledger's closing position in the trial balance.</summary>
/// <remarks>
/// <see cref="Debit"/> and <see cref="Credit"/> are presentation: a ledger has one net balance, and
/// it appears in whichever column its sign puts it in. Exactly one of the two is non-zero.
/// </remarks>
public sealed record TrialBalanceRow(
    Guid LedgerId,
    string LedgerName,
    string AccountGroupName,
    AccountNature Nature,
    decimal Debit,
    decimal Credit);

/// <summary>A trial balance and its totals, which must be equal.</summary>
public sealed record TrialBalance(
    DateOnly AsOnDate,
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit)
{
    /// <summary>
    /// Whether the books balance. Exact, with no tolerance — the underlying aggregation is integer
    /// arithmetic over minor units (ADR-0003), so a near-miss would be a real defect rather than a
    /// rounding artefact.
    /// </summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>One income or expense line of the profit &amp; loss statement.</summary>
public sealed record ProfitAndLossRow(
    Guid LedgerId,
    string LedgerName,
    string AccountGroupName,
    AccountNature Nature,
    bool AffectsGrossProfit,
    decimal Amount);

/// <summary>The P&amp;L for a period, with its gross and net profit lines.</summary>
public sealed record ProfitAndLoss(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<ProfitAndLossRow> Rows,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal GrossProfit,
    decimal NetProfit);

/// <summary>One asset or liability line of the balance sheet.</summary>
public sealed record BalanceSheetRow(
    Guid LedgerId,
    string LedgerName,
    string AccountGroupName,
    AccountNature Nature,
    decimal Amount);

/// <summary>
/// The balance sheet as of a date: assets on one side, liabilities plus the year's derived net
/// profit carried into the Profit &amp; Loss A/c line on the other.
/// </summary>
public sealed record BalanceSheet(
    DateOnly AsOnDate,
    IReadOnlyList<BalanceSheetRow> Assets,
    IReadOnlyList<BalanceSheetRow> Liabilities,
    decimal TotalAssets,
    decimal TotalLiabilities)
{
    /// <summary>Whether the sheet balances — exact, in minor units (C1.6).</summary>
    public bool IsBalanced => TotalAssets == TotalLiabilities;
}

/// <summary>One posting line of a ledger statement — the drill-down target of every report.</summary>
public sealed record LedgerStatementRow(
    Guid VoucherId,
    DateOnly VoucherDate,
    string VoucherNumber,
    string VoucherTypeName,
    VoucherKind Kind,
    decimal Debit,
    decimal Credit,
    string? Narration);

/// <summary>One missing number in a series' numbering for a financial year (spec §8.7).</summary>
public sealed record VoucherNumberGap(long MissingNumber);

/// <summary>
/// Derived statements. Every figure here is computed from journal lines; nothing is stored.
/// </summary>
/// <remarks>
/// Reads go through <c>ISqlQueryExecutor</c> against a named <c>SqlQueryKey</c> resolved per
/// provider (ADR-0001), never an inline SQL string, so the Phase 9 SQL Server move is a new dialect
/// rather than a rewrite of every report.
/// </remarks>
public interface IAccountingReportService
{
    Task<IReadOnlyList<DayBookRow>> GetDayBookAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    Task<TrialBalance> GetTrialBalanceAsync(
        DateOnly asOnDate,
        CancellationToken cancellationToken = default);

    Task<ProfitAndLoss> GetProfitAndLossAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    Task<BalanceSheet> GetBalanceSheetAsync(
        DateOnly asOnDate,
        CancellationToken cancellationToken = default);

    /// <summary>The ledger statement: every posting against one ledger between two dates.</summary>
    Task<IReadOnlyList<LedgerStatementRow>> GetLedgerStatementAsync(
        Guid ledgerId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LedgerStatementRow>> GetVoucherRegisterAsync(
        Guid voucherTypeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>Missing numbers in one series for one financial year, ascending (§8.7).</summary>
    Task<IReadOnlyList<VoucherNumberGap>> GetVoucherNumberGapsAsync(
        Guid seriesId,
        Guid financialYearId,
        CancellationToken cancellationToken = default);
}
