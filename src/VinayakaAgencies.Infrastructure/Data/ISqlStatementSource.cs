using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// One area's contribution to a provider's statement table.
/// </summary>
/// <remarks>
/// A dialect is the SQL for every reporting query in the product, which for a Tally-class ledger is
/// eventually a few hundred statements. Holding them in one dictionary literal makes that one file
/// nobody can edit without colliding with everybody else, so each area — balances, statements,
/// registers, the edit log — contributes its own source and the dialect composes them.
/// <para>
/// Implementations are discovered by reflection over this assembly rather than registered by hand,
/// because a hand-maintained registration list is a list somebody forgets to add to; the failure
/// mode is then a <see cref="SqlQueryKey"/> that resolves in tests and throws in the product.
/// A source therefore needs a public parameterless constructor and nothing else.
/// </para>
/// </remarks>
internal interface ISqlStatementSource
{
    /// <summary>The provider these statements are written for, e.g. "Sqlite".</summary>
    string Provider { get; }

    /// <summary>The statements, keyed by the query key each one answers.</summary>
    IReadOnlyDictionary<SqlQueryKey, string> Statements { get; }
}
