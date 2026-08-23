namespace VinayakaAgencies.Domain.Abstractions;

/// <summary>
/// Read-only reporting queries, executed via Dapper against a named, dialect-resolved statement.
/// </summary>
/// <remarks>
/// Read-only by design. Writes go through <see cref="IRepository{TEntity}"/> and
/// <see cref="IUnitOfWork"/> so that the change tracker, the audit stamps and (from Phase 1) the
/// audit trail cannot be bypassed.
/// </remarks>
public interface ISqlQueryExecutor
{
    Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        SqlQueryKey key,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<TRow?> QuerySingleOrDefaultAsync<TRow>(
        SqlQueryKey key,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<TResult?> ExecuteScalarAsync<TResult>(
        SqlQueryKey key,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}
