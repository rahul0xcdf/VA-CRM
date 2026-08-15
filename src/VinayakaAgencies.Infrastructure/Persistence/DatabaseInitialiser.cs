using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations to the company database.
/// </summary>
/// <remarks>
/// Migrating on startup is safe only because the product is single-user through Phase 8
/// (phase-0-foundations.md §2.4). Phase 9 must make this an explicit, gated step — several clients
/// racing to migrate one SQL Server database as they launch is a corruption risk, and
/// <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> is the switch that will turn it off.
/// </remarks>
internal sealed partial class DatabaseInitialiser(
    AppDbContext context,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitialiser> logger) : IDatabaseInitialiser
{
    private readonly AppDbContext _context = context
        ?? throw new ArgumentNullException(nameof(context));

    private readonly DatabaseOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    private readonly ILogger<DatabaseInitialiser> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<string>> InitialiseAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.ApplyMigrationsOnStartup)
        {
            LogStartupMigrationDisabled(_logger);
            return [];
        }

        var pending = (await _context.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false)).ToList();

        if (pending.Count == 0)
        {
            LogSchemaUpToDate(_logger);
            return [];
        }

        LogApplyingMigrations(_logger, pending.Count, string.Join(", ", pending));

        await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        return pending;
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Startup migration is disabled; the schema is assumed to be current.")]
    private static partial void LogStartupMigrationDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Database schema is up to date.")]
    private static partial void LogSchemaUpToDate(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Applying {Count} pending migration(s): {Migrations}")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, string migrations);
}
