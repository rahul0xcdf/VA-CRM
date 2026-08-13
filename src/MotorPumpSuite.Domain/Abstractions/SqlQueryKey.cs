namespace MotorPumpSuite.Domain.Abstractions;

/// <summary>
/// The name of a reporting query, resolved to provider-specific SQL by <see cref="ISqlDialect"/>.
/// </summary>
/// <remarks>
/// Reporting reads go through Dapper for speed, and raw SQL is exactly what would make the Phase 9
/// SQLite → SQL Server migration expensive. So callers never hold a SQL string — they hold a key.
/// The dialect layer owns the statements. See ADR-0001.
/// </remarks>
public readonly record struct SqlQueryKey
{
    public SqlQueryKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;

    public static implicit operator string(SqlQueryKey key) => key.Name;
}
