namespace MotorPumpSuite.Domain.Abstractions;

/// <summary>
/// Resolves a <see cref="SqlQueryKey"/> to the SQL statement for the active database provider.
/// </summary>
/// <remarks>
/// One implementation per provider: SQLite for Phases 1–8, SQL Server from Phase 9. Adding the
/// second provider must mean writing a second dialect, not editing report code.
/// </remarks>
public interface ISqlDialect
{
    /// <summary>Provider name, e.g. "Sqlite" or "SqlServer".</summary>
    string Name { get; }

    /// <summary>Returns the SQL for <paramref name="key"/>, or throws if the key is unknown.</summary>
    string Resolve(SqlQueryKey key);

    /// <summary>Whether this dialect has a statement registered for <paramref name="key"/>.</summary>
    bool CanResolve(SqlQueryKey key);
}
