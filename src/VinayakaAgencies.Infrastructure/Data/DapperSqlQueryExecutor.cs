using Dapper;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// Executes named reporting queries through Dapper.
/// </summary>
/// <remarks>
/// Read-only by construction. Writes go through <see cref="IRepository{TEntity}"/> and
/// <see cref="IUnitOfWork"/> so the change tracker, the audit stamps and — from Phase 1 — the
/// edit log cannot be bypassed. A reporting path that could also write would be a hole straight
/// through the audit trail that MCA Rule 3(1) requires.
/// </remarks>
internal sealed class DapperSqlQueryExecutor(
    IDbConnectionFactory connectionFactory,
    ISqlDialect dialect) : ISqlQueryExecutor
{
    private readonly IDbConnectionFactory _connectionFactory = connectionFactory
        ?? throw new ArgumentNullException(nameof(connectionFactory));

    private readonly ISqlDialect _dialect = dialect
        ?? throw new ArgumentNullException(nameof(dialect));

    public async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        SqlQueryKey key,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(key, parameters, cancellationToken);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<TRow>(command).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<TRow?> QuerySingleOrDefaultAsync<TRow>(
        SqlQueryKey key,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(key, parameters, cancellationToken);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<TRow>(command).ConfigureAwait(false);
    }

    public async Task<TResult?> ExecuteScalarAsync<TResult>(
        SqlQueryKey key,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(key, parameters, cancellationToken);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<TResult>(command).ConfigureAwait(false);
    }

    private CommandDefinition CreateCommand(
        SqlQueryKey key,
        object? parameters,
        CancellationToken cancellationToken) =>
        new(_dialect.Resolve(key), parameters, cancellationToken: cancellationToken);
}
