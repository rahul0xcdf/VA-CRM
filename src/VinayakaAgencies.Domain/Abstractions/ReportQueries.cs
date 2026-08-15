namespace VinayakaAgencies.Domain.Abstractions;

/// <summary>
/// The vocabulary of named reporting queries, shared by every layer.
/// </summary>
/// <remarks>
/// Callers hold a key from here and hand it to <see cref="ISqlQueryExecutor"/>; the SQL itself
/// lives in an <see cref="ISqlDialect"/> per provider. Adding a report means adding a key here and
/// a statement in every dialect — which is exactly the point, because a key that only one provider
/// implements is the thing that would make the Phase 9 SQL Server migration expensive. See
/// ADR-0001.
/// </remarks>
public static class ReportQueries
{
    /// <summary>
    /// Number of companies in this install. Phase 0's proof that the Dapper reporting path works
    /// end to end.
    /// </summary>
    public static readonly SqlQueryKey CompanyCount = new("Company.Count");

    /// <summary>
    /// Every posting in a date range, current revisions only, ordered by date then number.
    /// Parameters: <c>CompanyId</c>, <c>FromDate</c>, <c>ToDate</c>.
    /// </summary>
    public static readonly SqlQueryKey DayBook = new("Report.DayBook");

    /// <summary>
    /// Closing balance per ledger up to a date, current revisions only, in minor units.
    /// Parameters: <c>CompanyId</c>, <c>AsOnDate</c>.
    /// </summary>
    /// <remarks>
    /// Aggregation is <c>SUM</c> over an integer column (ADR-0003), so the trial balance totals to
    /// exactly zero rather than to within a rounding tolerance.
    /// </remarks>
    public static readonly SqlQueryKey TrialBalance = new("Report.TrialBalance");

    /// <summary>
    /// The edit log, newest first. Parameters: <c>FromUtc</c>, <c>ToUtc</c>, <c>EntityType</c>,
    /// <c>MaxRows</c>.
    /// </summary>
    public static readonly SqlQueryKey AuditLog = new("Audit.List");

    /// <summary>
    /// Every edit-log row for one entity, oldest first. Parameters: <c>EntityType</c>, <c>EntityId</c>.
    /// </summary>
    public static readonly SqlQueryKey AuditLogForEntity = new("Audit.ForEntity");

    /// <summary>
    /// The whole edit-log chain in sequence order, for hash verification.
    /// </summary>
    public static readonly SqlQueryKey AuditChain = new("Audit.Chain");
}
