namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// The database-engine half of backup and restore: the operations only the provider can perform.
/// </summary>
/// <remarks>
/// <para>
/// A port, for the reason ADR-0001 gives: taking a consistent snapshot of a live database is
/// provider-specific — SQLite has an online backup API, SQL Server has <c>BACKUP DATABASE</c> — and
/// Phase 9's move must not turn this into a rewrite. Everything that is <i>policy</i> rather than
/// mechanism (what goes in the archive, retention, verification, whether a restore is allowed)
/// lives in <see cref="BackupService"/> above this line and is provider-agnostic.
/// </para>
/// <para>
/// A plain file copy is not an implementation of <see cref="SnapshotDatabaseAsync"/>. Copying a
/// SQLite file while a connection is open can capture a torn page or miss a write-ahead log that
/// has not been checkpointed, producing an archive that restores into a corrupt database — and
/// which looks perfectly fine until the day it is needed.
/// </para>
/// </remarks>
public interface IBackupArchiveStore
{
    /// <summary>The live database file this install is using.</summary>
    string DatabaseFilePath { get; }

    /// <summary>
    /// Writes a transactionally consistent snapshot of the live database to
    /// <paramref name="destinationFilePath"/>, without stopping the application.
    /// </summary>
    Task SnapshotDatabaseAsync(
        string destinationFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>Migration identifiers applied to the live database, in order.</summary>
    Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the database file at <paramref name="databaseFilePath"/> on its own and checks it.
    /// </summary>
    /// <remarks>
    /// Takes a path rather than working on the live database, because §2's "restore into a scratch
    /// DB and check integrity" is the whole point: the file being checked is the one inside the
    /// archive, opened somewhere harmless.
    /// </remarks>
    Task<DatabaseIntegrity> CheckIntegrityAsync(
        string databaseFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the live database with <paramref name="sourceFilePath"/>, having first copied the
    /// current one to <paramref name="safetyCopyPath"/>.
    /// </summary>
    /// <remarks>
    /// The safety copy is taken by this method rather than by the caller so that no implementation
    /// can skip it: a provider that overwrote the live file without one would make a mistaken
    /// restore unrecoverable.
    /// </remarks>
    Task ReplaceDatabaseAsync(
        string sourceFilePath,
        string safetyCopyPath,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of a provider-level integrity check on one database file.</summary>
public sealed record DatabaseIntegrity(
    bool IsIntact,
    string Detail,
    IReadOnlyList<string> AppliedMigrations);
