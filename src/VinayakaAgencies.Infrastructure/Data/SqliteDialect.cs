using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// Resolves reporting query keys to SQLite statements.
/// </summary>
/// <remarks>
/// Every reporting statement in the product lives in a dialect like this one, keyed by
/// <see cref="SqlQueryKey"/>. Phase 9 adds a <c>SqlServerDialect</c> with the same keys and
/// different SQL; report code, which only ever holds a key, does not change. A key with no
/// statement is a startup-time bug, so <see cref="Resolve"/> throws rather than returning null.
/// See ADR-0001.
/// </remarks>
internal sealed class SqliteDialect : ISqlDialect
{
    private static readonly Dictionary<SqlQueryKey, string> Statements =
        new()
        {
            // Phase 0 registers a single statement so that the Dapper seam is genuinely exercised
            // end to end rather than merely wired up. The real reporting queries — trial balance,
            // day book, P&L — arrive with Phase 1.
            [ReportQueries.CompanyCount] = "SELECT COUNT(*) FROM Companies;",
        };

    public string Name => DatabaseProviders.Sqlite;

    public bool CanResolve(SqlQueryKey key) => Statements.ContainsKey(key);

    public string Resolve(SqlQueryKey key)
    {
        if (Statements.TryGetValue(key, out var sql))
        {
            return sql;
        }

        throw new KeyNotFoundException(
            $"No {Name} statement is registered for query key '{key}'. " +
            "Every dialect must implement every key — see ADR-0001.");
    }
}
