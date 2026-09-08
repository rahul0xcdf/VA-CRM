namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// The SQL predicates used by partial (SQLite) / filtered (SQL Server) indexes.
/// </summary>
/// <remarks>
/// <b>This is the only place in the persistence layer that writes provider SQL for an index
/// filter.</b> ADR-0001 keeps dialect differences in one layer so that the Phase 9 SQLite → SQL
/// Server Express move is a configuration change rather than a rewrite; an index filter is dialect
/// SQL like any other, and thirteen configurations each carrying their own hand-written
/// <c>"[Column] IS NULL"</c> would be thirteen places to find and fix.
/// <para>
/// Both providers accept bracket-delimited identifiers — SQL Server natively, SQLite for
/// MS-Access/SQL-Server compatibility — so the two dialects currently emit the same text. That is a
/// fact about today's two providers, not a licence to inline the string: if a third provider (or a
/// case-sensitive quoting rule) ever disagrees, this file is the single edit.
/// </para>
/// <para>
/// The output is deliberately byte-identical to the filter already scaffolded into
/// <c>InitialCreate</c> for <c>Companies.Gstin</c>. A filter string that differs by so much as a
/// space is a model difference, and the next <c>migrations add</c> would scaffold a spurious
/// drop-and-recreate of an index that has not actually changed.
/// </para>
/// </remarks>
internal static class IndexFilters
{
    private const string IdentifierPrefix = "[";

    private const string IdentifierSuffix = "]";

    /// <summary>The predicate for "only rows where this column is null", e.g. the current revision.</summary>
    internal static string IsNull(string columnName) =>
        string.Concat(Quote(columnName), " IS NULL");

    /// <summary>The predicate for "only rows where this column has a value".</summary>
    internal static string IsNotNull(string columnName) =>
        string.Concat(Quote(columnName), " IS NOT NULL");

    /// <summary>
    /// The predicate for "only rows where this boolean column is set", e.g. soft-uniqueness among
    /// active master rows.
    /// </summary>
    internal static string IsTrue(string columnName) =>
        string.Concat(Quote(columnName), " = 1");

    private static string Quote(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        return string.Concat(IdentifierPrefix, columnName, IdentifierSuffix);
    }
}
