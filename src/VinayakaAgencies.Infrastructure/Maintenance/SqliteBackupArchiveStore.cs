using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VinayakaAgencies.Application.Maintenance;
using VinayakaAgencies.Infrastructure.Configuration;
using VinayakaAgencies.Infrastructure.Data;
using VinayakaAgencies.Infrastructure.Persistence;

namespace VinayakaAgencies.Infrastructure.Maintenance;

/// <summary>
/// The SQLite half of backup and restore: online snapshot, integrity check, file replacement
/// (phase-10-hardening.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SnapshotDatabaseAsync"/> uses SQLite's own online backup API through
/// <see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/> rather than copying the file.
/// That is not a refinement, it is the difference between a backup and a plausible-looking
/// corruption: this database runs with a write-ahead log, so the committed state of the book is
/// split across the main file and the <c>-wal</c> beside it. A file copy taken while a connection
/// is open can catch a page mid-write or miss an uncheckpointed log, and the archive that results
/// restores into a database that fails its integrity check — on the day it is needed, having looked
/// fine for months.
/// </para>
/// <para>
/// ADR-0001: when Phase 9's SQL Server provider arrives it brings a sibling of this class built on
/// <c>BACKUP DATABASE</c>, and <see cref="BackupService"/> does not change.
/// </para>
/// </remarks>
internal sealed class SqliteBackupArchiveStore(
    AppDbContext context,
    IOptions<DatabaseOptions> options,
    IAppDataPaths paths) : IBackupArchiveStore
{
    private readonly AppDbContext _context = context
        ?? throw new ArgumentNullException(nameof(context));

    private readonly DatabaseOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    private readonly IAppDataPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public string DatabaseFilePath =>
        SqliteConnectionStringResolver.ResolveDatabaseFilePath(_options, _paths);

    public async Task SnapshotDatabaseAsync(
        string destinationFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);

        await using var source = new SqliteConnection(
            SqliteConnectionStringResolver.Resolve(_options, _paths));

        await source.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Pooling off. Microsoft.Data.Sqlite returns a disposed connection to its pool and keeps the
        // underlying handle open, which on Windows leaves this file locked — so the caller could not
        // then hash it into the archive, and could not delete the staging directory afterwards.
        // Nothing reconnects to a snapshot, so there is no pool worth having.
        await using var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = destinationFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());

        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Synchronous by design: SQLite's backup API has no asynchronous form, and wrapping it in a
        // Task.Run would add a thread hop without making anything cancellable. For a book this size
        // it is milliseconds.
        source.BackupDatabase(destination);
    }

    public async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default) =>
        (await _context.Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false))
        .ToList();

    public async Task<DatabaseIntegrity> CheckIntegrityAsync(
        string databaseFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);

        if (!File.Exists(databaseFilePath))
        {
            return new DatabaseIntegrity(
                IsIntact: false,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"there is no file at '{databaseFilePath}'"),
                AppliedMigrations: []);
        }

        // Read-only, so a check can never be the thing that damages the file it is checking.
        // Pooling off for the same reason as the snapshot above: this file is usually a scratch copy
        // the caller is about to delete, and a pooled handle would keep it alive.
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databaseFilePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());

        // Every SQLite call below is inside one catch, deliberately. A damaged file does not fail at
        // open: SQLite reads no page until it is asked to, so a truncated or overwritten archive
        // raises "file is not a database" on the first pragma instead. An integrity check whose
        // answer to a corrupt file is to throw is not a check — the caller asked a question and is
        // entitled to "no, and here is why" rather than an exception to handle.
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var integrity = await ReadScalarTextAsync(
                connection,
                "PRAGMA integrity_check;",
                cancellationToken).ConfigureAwait(false);

            // SQLite answers the single word "ok" when it finds nothing wrong, and otherwise
            // returns one row per problem. Anything that is not exactly "ok" is a problem.
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseIntegrity(
                    IsIntact: false,
                    integrity ?? "integrity_check returned nothing",
                    AppliedMigrations: []);
            }

            // Foreign keys are enforced per connection, so a snapshot can be internally consistent
            // as pages and still hold orphaned rows. Worth knowing before a restore, not after.
            var violations = await ReadScalarTextAsync(
                connection,
                "PRAGMA foreign_key_check;",
                cancellationToken).ConfigureAwait(false);

            if (violations is not null)
            {
                return new DatabaseIntegrity(
                    IsIntact: false,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"foreign key violations found, first in table '{violations}'"),
                    AppliedMigrations: []);
            }

            return new DatabaseIntegrity(
                IsIntact: true,
                "ok",
                await ReadMigrationsAsync(connection, cancellationToken).ConfigureAwait(false));
        }
        catch (SqliteException ex)
        {
            return new DatabaseIntegrity(
                IsIntact: false,
                string.Create(CultureInfo.InvariantCulture, $"SQLite rejected the file: {ex.Message}"),
                AppliedMigrations: []);
        }
    }

    public async Task ReplaceDatabaseAsync(
        string sourceFilePath,
        string safetyCopyPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(safetyCopyPath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException(
                "The database to restore from does not exist.",
                sourceFilePath);
        }

        var live = DatabaseFilePath;

        Directory.CreateDirectory(Path.GetDirectoryName(safetyCopyPath)!);

        // Before anything is overwritten. An operator who picks the wrong archive has destroyed the
        // current books unless this exists, and "restore the one you just replaced" has to be a
        // file copy somebody can be talked through on the telephone.
        if (File.Exists(live))
        {
            await CopyWithoutPoolingAsync(live, safetyCopyPath, cancellationToken).ConfigureAwait(false);
        }

        // Microsoft.Data.Sqlite pools connections, and a pooled handle keeps the file open on
        // Windows. Without this the copy below fails with a sharing violation — and worse, a later
        // connection could be handed a pooled handle still pointing at the replaced file.
        SqliteConnection.ClearAllPools();

        File.Copy(sourceFilePath, live, overwrite: true);

        // The write-ahead log and shared-memory file belong to the database that has just been
        // replaced. Leaving them beside a different main file is how a restore turns into
        // corruption: SQLite would try to recover the previous book's uncommitted pages into the
        // restored one.
        DeleteIfPresent(live + "-wal");
        DeleteIfPresent(live + "-shm");
    }

    private static async Task CopyWithoutPoolingAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();

        await using var from = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        await using var to = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        await from.CopyToAsync(to, cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Left behind rather than failing the restore; SQLite will discard a log whose main
            // file does not match it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Runs a pragma and returns the first column of the first row, or null when there are no rows.
    /// </summary>
    private static async Task<string?> ReadScalarTextAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? reader.GetValue(0)?.ToString()
            : null;
    }

    private static async Task<IReadOnlyList<string>> ReadMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var migrations = new List<string>();

        await using var command = connection.CreateCommand();

        // Read straight from the history table rather than through EF Core: this connection points
        // at an archived file, and building a DbContext over it to ask the question would mean
        // constructing a context that could save to it.
        command.CommandText =
            """
            SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
            """;

        try
        {
            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                migrations.Add(reader.GetString(0));
            }
        }
        catch (SqliteException)
        {
            // No history table: a database that was never migrated. Reported as an empty list
            // rather than an error, because the restore path treats "no migrations" as something to
            // refuse on its own terms.
        }

        return migrations;
    }
}
