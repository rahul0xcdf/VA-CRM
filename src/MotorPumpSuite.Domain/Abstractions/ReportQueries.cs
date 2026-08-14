namespace MotorPumpSuite.Domain.Abstractions;

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
    /// end to end; the substantive reports arrive with Phase 1.
    /// </summary>
    public static readonly SqlQueryKey CompanyCount = new("Company.Count");
}
