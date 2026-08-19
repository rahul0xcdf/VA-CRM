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
/// <para>
/// The statements themselves are contributed by <see cref="ISqlStatementSource"/> implementations,
/// one per reporting area, and composed here. The dialect owns resolution; it does not own the SQL.
/// </para>
/// </remarks>
internal sealed class SqliteDialect : ISqlDialect
{
    private static readonly IReadOnlyDictionary<SqlQueryKey, string> Statements =
        SqlStatementSources.For(DatabaseProviders.Sqlite);

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
