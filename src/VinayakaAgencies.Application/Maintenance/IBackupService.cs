namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// On-demand and scheduled backup, listing, verification and restore
/// (phase-10-hardening.md §2, checkpoints C10.1 and C10.6).
/// </summary>
public interface IBackupService
{
    /// <summary>Takes, verifies and files one backup.</summary>
    /// <param name="reason">
    /// Why this backup exists — "operator requested", "scheduled", "on shutdown",
    /// "before restore". Recorded in the manifest, because the backup an owner wants during a
    /// recovery is usually identifiable by the reason rather than by the minute it was taken.
    /// </param>
    Task<BackupSummary> CreateAsync(
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Every archive at the default destination, newest first.</summary>
    Task<IReadOnlyList<BackupSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens one archive and checks it: manifest readable, every entry hashing to what the
    /// manifest recorded, and the database inside it passing a provider integrity check.
    /// </summary>
    Task<BackupVerification> VerifyAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the live books with the contents of one archive, after verifying it and taking a
    /// safety copy of what is being replaced.
    /// </summary>
    /// <remarks>
    /// The application must be restarted afterwards. The live database file has been swapped under
    /// a running process; anything already loaded into a screen is from the previous books.
    /// </remarks>
    Task<RestoreOutcome> RestoreAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the retention policy (§4a D1) and returns how many archives were removed.
    /// </summary>
    Task<int> PruneAsync(CancellationToken cancellationToken = default);
}
