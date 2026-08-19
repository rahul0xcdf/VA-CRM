using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.Reporting;

/// <summary>
/// The named reporting queries Phase 1 adds (spec §8.4).
/// </summary>
/// <remarks>
/// A caller holds a key from here and hands it to <see cref="ISqlQueryExecutor"/>; the SQL itself
/// lives in an <see cref="ISqlDialect"/> per provider (ADR-0001 §3). No SQL string appears in this
/// assembly or in the App, which is what makes the Phase 9 SQL Server move a second dialect rather
/// than a rewrite of every report — and what checkpoint C1.11 asserts.
/// <para>
/// Two rules hold for every key below. Each takes <c>CompanyId</c> as its first parameter (§10) —
/// a reporting query without a company filter is a defect, not a convenience. And each reads
/// through the shared current-revision predicate, never a hand-written <c>FROM JournalLines</c>:
/// a statement that forgets <c>SupersededByRevisionId IS NULL</c> double-counts every altered
/// voucher, and does so silently, in a figure nobody recomputes by hand.
/// </para>
/// </remarks>
public static class AccountingQueryKeys
{
    /// <summary>
    /// Closing balance per ledger up to a date, in minor units.
    /// Parameters: <c>CompanyId</c>, <c>AsOfDate</c>.
    /// </summary>
    public static readonly SqlQueryKey LedgerBalancesAsOf = new("Accounting.LedgerBalances.AsOf");

    /// <summary>
    /// Debit and credit movement per ledger between two dates.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey LedgerBalancesForPeriod =
        new("Accounting.LedgerBalances.ForPeriod");

    /// <summary>
    /// Opening, debit, credit and closing per ledger, with its group path.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    /// <remarks>
    /// Aggregation is <c>SUM</c> over an integer column (ADR-0003), so the totals agree to exactly
    /// zero rather than to within a rounding tolerance (C1.5).
    /// </remarks>
    public static readonly SqlQueryKey TrialBalance = new("Accounting.TrialBalance");

    /// <summary>
    /// Roll-up per account group, via the §4.2 materialised-path range predicate.
    /// Parameters: <c>CompanyId</c>, <c>AsOfDate</c>.
    /// </summary>
    public static readonly SqlQueryKey GroupBalancesAsOf = new("Accounting.GroupBalances.AsOf");

    /// <summary>
    /// Income and expense sections with gross and net profit.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey ProfitAndLossForPeriod =
        new("Accounting.ProfitAndLoss.ForPeriod");

    /// <summary>
    /// Asset and liability sections as of a date.
    /// Parameters: <c>CompanyId</c>, <c>AsOfDate</c>, <c>FinancialYearStartDate</c>.
    /// </summary>
    public static readonly SqlQueryKey BalanceSheetAsOf = new("Accounting.BalanceSheet.AsOf");

    /// <summary>
    /// Every voucher in a date range, cancelled ones included and flagged.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey DayBookForPeriod = new("Accounting.DayBook.ForPeriod");

    /// <summary>
    /// Ledger statement, and the drill-down target of every other report.
    /// Parameters: <c>CompanyId</c>, <c>LedgerId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey LedgerVouchersForPeriod =
        new("Accounting.LedgerVouchers.ForPeriod");

    /// <summary>
    /// Voucher register for one voucher type.
    /// Parameters: <c>CompanyId</c>, <c>VoucherTypeId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey VoucherRegisterByType =
        new("Accounting.VoucherRegister.ByType");

    /// <summary>
    /// Missing numbers in one series for one financial year (§8.7).
    /// Parameters: <c>CompanyId</c>, <c>SeriesId</c>, <c>FinancialYearId</c>.
    /// </summary>
    public static readonly SqlQueryKey VoucherNumberGaps = new("Accounting.VoucherNumberGaps");

    /// <summary>
    /// The edit log, optionally filtered to one entity.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>, <c>EntityType</c>,
    /// <c>EntityId</c> — the last two nullable.
    /// </summary>
    public static readonly SqlQueryKey EditLogForPeriod = new("Accounting.EditLog.ForPeriod");

    /// <summary>
    /// The hash chain in sequence order, for the §11.5 integrity check.
    /// Parameters: <c>FromSequence</c>.
    /// </summary>
    /// <remarks>
    /// The one key with no <c>CompanyId</c> parameter, and deliberately so: the chain is a property
    /// of the whole log, which outlives any company row, and verifying only one company's slice of
    /// it would verify nothing.
    /// </remarks>
    public static readonly SqlQueryKey EditLogChainVerification =
        new("Accounting.EditLog.ChainVerification");

    /// <summary>
    /// Bank-ledger lines with their cleared marks (§12).
    /// Parameters: <c>CompanyId</c>, <c>LedgerId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey BankReconciliationStatement =
        new("Accounting.BankReconciliation.Statement");

    /// <summary>
    /// Every key this phase declares, in the order they appear above.
    /// </summary>
    /// <remarks>
    /// Checkpoint C1.11 walks this list and asserts every registered dialect can resolve every key,
    /// so a key that only SQLite implements fails the build rather than the Phase 9 migration.
    /// </remarks>
    public static IReadOnlyList<SqlQueryKey> All { get; } =
    [
        LedgerBalancesAsOf,
        LedgerBalancesForPeriod,
        TrialBalance,
        GroupBalancesAsOf,
        ProfitAndLossForPeriod,
        BalanceSheetAsOf,
        DayBookForPeriod,
        LedgerVouchersForPeriod,
        VoucherRegisterByType,
        VoucherNumberGaps,
        EditLogForPeriod,
        EditLogChainVerification,
        BankReconciliationStatement,
    ];
}
