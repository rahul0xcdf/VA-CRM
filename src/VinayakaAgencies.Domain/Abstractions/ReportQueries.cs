namespace VinayakaAgencies.Domain.Abstractions;

/// <summary>
/// Reporting query keys that belong to no single feature area.
/// </summary>
/// <remarks>
/// Callers hold a key from here and hand it to <see cref="ISqlQueryExecutor"/>; the SQL itself
/// lives in an <see cref="ISqlDialect"/> per provider. Adding a report means adding a key and a
/// statement in every dialect — which is exactly the point, because a key that only one provider
/// implements is the thing that would make the Phase 9 SQL Server migration expensive. See
/// ADR-0001.
/// <para>
/// Phase 1's accounting keys live in <c>VinayakaAgencies.Application.Reporting.AccountingQueryKeys</c>
/// where spec §8.4 puts them, next to the report contracts that use them. This class keeps only the
/// keys that predate them.
/// </para>
/// </remarks>
public static class ReportQueries
{
    /// <summary>
    /// Number of companies in this install. Phase 0's proof that the Dapper reporting path works
    /// end to end.
    /// </summary>
    public static readonly SqlQueryKey CompanyCount = new("Company.Count");
}
