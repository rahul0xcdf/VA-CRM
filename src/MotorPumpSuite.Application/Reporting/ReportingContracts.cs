using MotorPumpSuite.Domain.Accounting;

namespace MotorPumpSuite.Application.Reporting;

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
    Guid VoucherId);

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
}
