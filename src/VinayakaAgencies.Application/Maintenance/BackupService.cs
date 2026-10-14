using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// Backup and restore policy: what goes in an archive, how it is checked, what retention keeps, and
/// on what terms the live books may be replaced (phase-10-hardening.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Everything provider-specific is behind <see cref="IBackupArchiveStore"/>; this class decides.
/// The division matters for the Phase 9 move — the policy below is the part that must not be
/// rewritten when the database changes.
/// </para>
/// <para>
/// The archive is an ordinary ZIP, deliberately. A proprietary container would be marginally harder
/// to tamper with and would also mean that an owner whose application will not start has no way to
/// reach their own books: with a ZIP, the recovery path of last resort is to open the file in
/// Explorer and take the database out of <c>database/</c>. That path is documented in the user
/// guide, and it is worth more than the obscurity it costs.
/// </para>
/// </remarks>
public sealed class BackupService(
    IAppDataPaths paths,
    IBackupArchiveStore store,
    IBookFingerprintService fingerprints,
    ICompanyService companies,
    IClock clock,
    ICurrentUser currentUser,
    BackupOptions options) : IBackupService
{
    /// <summary>Extension of a backup archive. A ZIP, and named as one on purpose.</summary>
    public const string ArchiveExtension = ".zip";

    private const string ArchivePrefix = "vinayaka-backup-";

    private const string DatabaseFolder = "database";

    private const string DataFolder = "data";

    private readonly IAppDataPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private readonly IBackupArchiveStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly IBookFingerprintService _fingerprints = fingerprints
        ?? throw new ArgumentNullException(nameof(fingerprints));

    private readonly ICompanyService _companies = companies
        ?? throw new ArgumentNullException(nameof(companies));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly BackupOptions _options = options
        ?? throw new ArgumentNullException(nameof(options));

    public async Task<BackupSummary> CreateAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Directory.CreateDirectory(_paths.BackupDirectory);

        // Taken before the snapshot, from the live books. Taking it from the snapshot instead would
        // make the fingerprint a description of the archive rather than of what was backed up,
        // which is a tautology: it could never disagree and so could never catch anything.
        var fingerprint = await _fingerprints.ComputeAsync(cancellationToken).ConfigureAwait(false);
        var migrations = await _store.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        var company = await ResolveCompanyAsync(cancellationToken).ConfigureAwait(false);

        var staging = Path.Combine(
            Path.GetTempPath(),
            "VinayakaAgencies.Backup",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        var archivePath = Path.Combine(
            _paths.BackupDirectory,
            ArchivePrefix
            + _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + ArchiveExtension);

        try
        {
            var databaseFileName = Path.GetFileName(_store.DatabaseFilePath);

            Directory.CreateDirectory(Path.Combine(staging, DatabaseFolder));

            await _store
                .SnapshotDatabaseAsync(
                    Path.Combine(staging, DatabaseFolder, databaseFileName),
                    cancellationToken)
                .ConfigureAwait(false);

            CopyOperatorOwnedFiles(Path.Combine(staging, DataFolder));

            var manifest = new BackupManifest(
                BackupManifest.CurrentFormatVersion,
                ResolveAppVersion(),
                _clock.UtcNow,
                _currentUser.UserName,
                Environment.MachineName,
                company?.LegalName,
                company?.Id,
                databaseFileName,
                migrations,
                HashStagedEntries(staging),
                fingerprint,
                reason);

            await WriteManifestAsync(staging, manifest, cancellationToken).ConfigureAwait(false);

            ZipFile.CreateFromDirectory(
                staging,
                archivePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        // Verified by reopening the finished archive, not by trusting the write that produced it.
        // §2 asks for verified backups; an archive checked from the buffers that wrote it would
        // pass even when nothing reached the disk.
        var verification = _options.VerifyAfterWrite
            ? await VerifyAsync(archivePath, cancellationToken).ConfigureAwait(false)
            : null;

        var destinations = new List<BackupDestinationResult>
        {
            BackupDestinationResult.Written(archivePath),
        };

        destinations.AddRange(CopyToSecondDestination(archivePath));

        await PruneAsync(cancellationToken).ConfigureAwait(false);

        var info = new FileInfo(archivePath);

        return new BackupSummary(
            archivePath,
            _clock.UtcNow,
            info.Exists ? info.Length : 0L,
            await ReadManifestAsync(archivePath, cancellationToken).ConfigureAwait(false),
            verification,
            destinations);
    }

    public async Task<IReadOnlyList<BackupSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_paths.BackupDirectory))
        {
            return [];
        }

        var summaries = new List<BackupSummary>();

        foreach (var file in Directory.EnumerateFiles(
            _paths.BackupDirectory,
            ArchivePrefix + "*" + ArchiveExtension))
        {
            var info = new FileInfo(file);

            // Listing deliberately does not verify. Verification extracts and opens a database per
            // archive, and doing thirty of those to paint a list would make opening the screen feel
            // broken. The screen verifies the one the operator selects.
            var manifest = await ReadManifestAsync(file, cancellationToken).ConfigureAwait(false);

            summaries.Add(new BackupSummary(
                file,
                manifest?.CreatedAtUtc ?? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                info.Length,
                manifest,
                Verification: null,
                Destinations: []));
        }

        return summaries
            .OrderByDescending(summary => summary.CreatedAtUtc)
            .ToList();
    }

    public async Task<BackupVerification> VerifyAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        if (!File.Exists(archivePath))
        {
            return BackupVerification.Invalid(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no file at '{archivePath}'."));
        }

        var problems = new List<string>();
        BackupManifest? manifest;

        try
        {
            manifest = await ReadManifestAsync(archivePath, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            return BackupVerification.Invalid(string.Create(
                CultureInfo.InvariantCulture,
                $"'{Path.GetFileName(archivePath)}' is not a readable archive: {ex.Message}"));
        }

        if (manifest is null)
        {
            return BackupVerification.Invalid(
                "The archive carries no manifest, so there is nothing to check it against.");
        }

        if (manifest.FormatVersion > BackupManifest.CurrentFormatVersion)
        {
            problems.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"The archive is format version {manifest.FormatVersion}; this build understands up to {BackupManifest.CurrentFormatVersion}."));
        }

        using var archive = ZipFile.OpenRead(archivePath);

        foreach (var expected in manifest.Entries)
        {
            var entry = archive.GetEntry(expected.Path);

            if (entry is null)
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{expected.Path}' is named in the manifest but missing from the archive."));
                continue;
            }

            var actual = await HashEntryAsync(entry, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(actual, expected.Sha256, StringComparison.Ordinal))
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{expected.Path}' does not match the hash recorded when it was written."));
            }
        }

        // §2: restore into a scratch database and check it. Hash equality proves the bytes survived
        // the round trip; it does not prove they were a valid database when they went in.
        var integrity = await CheckArchivedDatabaseAsync(archive, manifest, cancellationToken)
            .ConfigureAwait(false);

        if (!integrity.IsIntact)
        {
            problems.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"The database inside the archive failed its integrity check: {integrity.Detail}"));
        }

        return problems.Count == 0
            ? BackupVerification.Valid(integrity.IsIntact, manifest.Fingerprint)
            : new BackupVerification(
                IsValid: false,
                problems,
                integrity.IsIntact,
                manifest.Fingerprint);
    }

    public async Task<RestoreOutcome> RestoreAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var verification = await VerifyAsync(archivePath, cancellationToken).ConfigureAwait(false);

        if (!verification.IsValid)
        {
            throw new InvalidOperationException(
                "This archive did not pass verification, so it will not be restored: "
                + string.Join(" ", verification.Problems));
        }

        var manifest = await ReadManifestAsync(archivePath, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The archive carries no manifest.");

        var liveMigrations = await _store
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        // Refused up front rather than discovered halfway through billing. A database from a newer
        // schema has columns this build does not know; one from an older schema is missing columns
        // this build will select. Both run, and both fail later, somewhere unrelated.
        var newerInArchive = manifest.SchemaMigrations.Except(liveMigrations, StringComparer.Ordinal).ToList();

        if (newerInArchive.Count > 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The archive was taken against a newer schema than this build knows. Upgrade first, then restore. Unknown migration(s): {string.Join(", ", newerInArchive)}"));
        }

        var staging = Path.Combine(
            Path.GetTempPath(),
            "VinayakaAgencies.Restore",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        var safetyCopy = Path.Combine(
            _paths.BackupDirectory,
            "replaced-"
            + _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-"
            + Path.GetFileName(_store.DatabaseFilePath));

        var restored = new List<string>();

        try
        {
            Directory.CreateDirectory(staging);

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var databaseEntry = archive.GetEntry(
                    DatabaseFolder + "/" + manifest.DatabaseFileName)
                    ?? throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"The archive has no database at '{DatabaseFolder}/{manifest.DatabaseFileName}'."));

                var stagedDatabase = Path.Combine(staging, manifest.DatabaseFileName);
                databaseEntry.ExtractToFile(stagedDatabase, overwrite: true);

                await _store
                    .ReplaceDatabaseAsync(stagedDatabase, safetyCopy, cancellationToken)
                    .ConfigureAwait(false);

                restored.Add(databaseEntry.FullName);
                restored.AddRange(RestoreOperatorOwnedFiles(archive));
            }
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        // Recomputed against the live books now holding the restored file — which is the claim
        // C10.6 actually makes. Reading it back out of the manifest would prove only that the
        // manifest had not changed.
        var actual = await _fingerprints.ComputeAsync(cancellationToken).ConfigureAwait(false);

        return new RestoreOutcome(
            archivePath,
            safetyCopy,
            manifest.Fingerprint,
            actual,
            restored,
            actual.Describe(manifest.Fingerprint));
    }

    public Task<int> PruneAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_paths.BackupDirectory))
        {
            return Task.FromResult(0);
        }

        // At least one, whatever configuration says. §4a D1 makes the floor the load-bearing part
        // of the policy: a retention setting of zero must not be able to empty the folder.
        var keep = Math.Max(1, _options.RetainedBackupCount);

        var removed = 0;

        var archives = Directory
            .EnumerateFiles(_paths.BackupDirectory, ArchivePrefix + "*" + ArchiveExtension)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var stale in archives.Skip(keep))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                stale.Delete();
                removed++;
            }
            catch (IOException)
            {
                // A locked archive is not worth failing a backup over; it will be reconsidered on
                // the next run.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return Task.FromResult(removed);
    }

    private async Task<CompanySummary?> ResolveCompanyAsync(CancellationToken cancellationToken)
    {
        var all = await _companies.ListAsync(cancellationToken).ConfigureAwait(false);

        return all.Count == 0 ? null : all[0];
    }

    private static string ResolveAppVersion() =>
        typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Copies everything under the data root that the operator owns rather than the installer:
    /// local configuration, and any logo or artwork they dropped in.
    /// </summary>
    /// <remarks>
    /// §2 requires the archive to carry templates, logos and configuration, not only the database.
    /// Print templates are rows (Phase 3 made them versioned data), so the database snapshot
    /// already holds them; this covers the files. The backups folder is excluded for the obvious
    /// reason, and the live database files because the snapshot above is the correct copy of those.
    /// </remarks>
    private void CopyOperatorOwnedFiles(string destinationRoot)
    {
        if (!Directory.Exists(_paths.DataRoot))
        {
            return;
        }

        var databaseFileName = Path.GetFileName(_store.DatabaseFilePath);
        var backupDirectory = Path.TrimEndingDirectorySeparator(_paths.BackupDirectory);

        foreach (var file in Directory.EnumerateFiles(
            _paths.DataRoot,
            "*",
            SearchOption.AllDirectories))
        {
            if (Path.GetDirectoryName(file) is { } directory
                && directory.StartsWith(backupDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Path.GetFileName(file);

            if (name.StartsWith(databaseFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(_paths.DataRoot, file);
            var destination = Path.Combine(destinationRoot, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    /// <summary>
    /// Puts the operator-owned files from an archive back, beside the restored database.
    /// </summary>
    /// <remarks>
    /// §2 is explicit that a restore which loses the customised invoice template is not a restore.
    /// Existing files are overwritten, because a half-restored configuration — the archive's
    /// database with the current machine's settings — is a state nobody asked for and nobody can
    /// reason about.
    /// </remarks>
    private List<string> RestoreOperatorOwnedFiles(ZipArchive archive)
    {
        var restored = new List<string>();
        var prefix = DataFolder + "/";

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)
                || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var relative = entry.FullName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.Combine(_paths.DataRoot, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            restored.Add(entry.FullName);
        }

        return restored;
    }

    private IEnumerable<BackupDestinationResult> CopyToSecondDestination(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(_options.SecondDestination))
        {
            yield break;
        }

        var destination = Path.Combine(
            _options.SecondDestination,
            Path.GetFileName(archivePath));

        // §4a D1: reported, never fatal. An unplugged drive or an unreachable share must not cost
        // the operator the local copy that did succeed.
        BackupDestinationResult result;

        try
        {
            Directory.CreateDirectory(_options.SecondDestination);
            File.Copy(archivePath, destination, overwrite: true);
            result = BackupDestinationResult.Written(destination);
        }
        catch (IOException ex)
        {
            result = BackupDestinationResult.Failed(destination, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            result = BackupDestinationResult.Failed(destination, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            result = BackupDestinationResult.Failed(destination, ex.Message);
        }

        yield return result;
    }

    private async Task<DatabaseIntegrity> CheckArchivedDatabaseAsync(
        ZipArchive archive,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(DatabaseFolder + "/" + manifest.DatabaseFileName);

        if (entry is null)
        {
            return new DatabaseIntegrity(
                IsIntact: false,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"no database at '{DatabaseFolder}/{manifest.DatabaseFileName}'"),
                AppliedMigrations: []);
        }

        var scratch = Path.Combine(
            Path.GetTempPath(),
            "VinayakaAgencies.Verify",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        try
        {
            Directory.CreateDirectory(scratch);

            var scratchDatabase = Path.Combine(scratch, manifest.DatabaseFileName);
            entry.ExtractToFile(scratchDatabase, overwrite: true);

            return await _store
                .CheckIntegrityAsync(scratchDatabase, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    private static List<BackupEntry> HashStagedEntries(string staging)
    {
        var entries = new List<BackupEntry>();

        foreach (var file in Directory
            .EnumerateFiles(staging, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            using var stream = File.OpenRead(file);

            entries.Add(new BackupEntry(
                Path.GetRelativePath(staging, file).Replace('\\', '/'),
                stream.Length,
                Convert.ToHexString(SHA256.HashData(stream)).ToUpperInvariant()));
        }

        return entries;
    }

    private static async Task<string> HashEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();

        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToUpperInvariant();
    }

    private static async Task WriteManifestAsync(
        string staging,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(Path.Combine(staging, BackupManifest.EntryName));

        await JsonSerializer
            .SerializeAsync(stream, manifest, ManifestJson, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<BackupManifest?> ReadManifestAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var entry = archive.GetEntry(BackupManifest.EntryName);

        if (entry is null)
        {
            return null;
        }

        await using var stream = entry.Open();

        return await JsonSerializer
            .DeserializeAsync<BackupManifest>(stream, ManifestJson, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Indented on purpose: the manifest is the one part of an archive a human reads during a
    /// recovery, often on a machine with nothing but Notepad.
    /// </summary>
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
    };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
