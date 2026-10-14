using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using VinayakaAgencies.Application.Maintenance;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// Opens SQLite connections for the Dapper reporting path.
/// </summary>
/// <remarks>
/// The concrete <c>SqliteConnection</c> type is named here and nowhere else. Phase 9 adds a
/// <c>SqlServerConnectionFactory</c> beside this one and swaps the registration; no reporting code
/// changes. See ADR-0001.
/// </remarks>
internal sealed class SqliteConnectionFactory(
    IOptions<DatabaseOptions> options,
    IAppDataPaths paths) : IDbConnectionFactory
{
    private readonly DatabaseOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    private readonly IAppDataPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(SqliteConnectionStringResolver.Resolve(_options, _paths));

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
