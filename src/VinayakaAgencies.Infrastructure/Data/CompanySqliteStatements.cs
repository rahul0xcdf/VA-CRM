using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// SQLite statements for the company master.
/// </summary>
internal sealed class CompanySqliteStatements : ISqlStatementSource
{
    public string Provider => DatabaseProviders.Sqlite;

    public IReadOnlyDictionary<SqlQueryKey, string> Statements { get; } =
        new Dictionary<SqlQueryKey, string>
        {
            // Phase 0's proof that the Dapper seam is genuinely exercised end to end rather than
            // merely wired up.
            [ReportQueries.CompanyCount] = "SELECT COUNT(*) FROM Companies;",
        };
}
