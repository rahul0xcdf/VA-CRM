using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// Builds the file an owner sends when something goes wrong, and records the crash reports that go
/// into it (phase-10-hardening.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The hard requirement here is the redaction, not the collection. A log bundle is the one artefact
/// that deliberately leaves the premises, which makes it the most likely way for a credential to
/// escape — so every text entry goes through <see cref="SensitiveText.Redact"/> on the way in, the
/// same two-pass scrub Phase 6 built for the IRP call log (checkpoint C6.6). §4a D5 extends that
/// checkpoint's scan to cover this bundle for exactly this reason.
/// </para>
/// <para>
/// <see cref="SensitiveText"/> lives under <c>Einvoicing</c> because that is where it was first
/// needed. It is reused rather than reimplemented: a second redactor would be a second thing to
/// keep correct, and the one that already exists is the one with a checkpoint behind it.
/// </para>
/// </remarks>
public sealed class DiagnosticsBundleService(
    IAppDataPaths paths,
    IBackupArchiveStore store,
    IEinvoiceSettingsService einvoiceSettings,
    IClock clock,
    ICurrentUser currentUser) : IDiagnosticsBundleService
{
    /// <summary>
    /// How many log files to carry. The rolling sink keeps 31 days; a fault is almost always
    /// visible in the last few, and a bundle small enough to attach to an email is one that
    /// actually gets sent.
    /// </summary>
    private const int LogFilesCarried = 7;

    private readonly IAppDataPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private readonly IBackupArchiveStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly IEinvoiceSettingsService _einvoiceSettings = einvoiceSettings
        ?? throw new ArgumentNullException(nameof(einvoiceSettings));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    public async Task<DiagnosticsBundle> CreateAsync(
        string? operatorNote = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.DiagnosticsDirectory);

        var bundlePath = Path.Combine(
            _paths.DiagnosticsDirectory,
            "vinayaka-diagnostics-"
            + _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + ".zip");

        var contents = new List<string>();
        var redacted = 0;

        // The exact secrets this install holds, so redaction is the same two passes Phase 6 built
        // for the IRP call log rather than only the first one. The key pass catches a credential
        // sitting under a field named after it; the value pass catches one quoted back inside an
        // error message under a field name nobody anticipated. For the artefact that is designed to
        // be emailed to a stranger, one pass is not enough.
        var knownSecrets = await ResolveKnownSecretsAsync(cancellationToken).ConfigureAwait(false);

        await using (var file = File.Create(bundlePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            await WriteTextEntryAsync(
                archive,
                "report.txt",
                await BuildReportAsync(operatorNote, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            contents.Add("report.txt");

            foreach (var log in RecentFiles(_paths.LogDirectory, "log-*.txt", LogFilesCarried))
            {
                var entry = "logs/" + Path.GetFileName(log);

                await WriteTextEntryAsync(
                    archive,
                    entry,
                    SensitiveText.Redact(
                        await ReadAllTextSafelyAsync(log, cancellationToken).ConfigureAwait(false),
                        knownSecrets),
                    cancellationToken).ConfigureAwait(false);

                contents.Add(entry);
                redacted++;
            }

            foreach (var crash in RecentFiles(_paths.DiagnosticsDirectory, "crash-*.txt", int.MaxValue))
            {
                var entry = "crashes/" + Path.GetFileName(crash);

                await WriteTextEntryAsync(
                    archive,
                    entry,
                    SensitiveText.Redact(
                        await ReadAllTextSafelyAsync(crash, cancellationToken).ConfigureAwait(false),
                        knownSecrets),
                    cancellationToken).ConfigureAwait(false);

                contents.Add(entry);
                redacted++;
            }

            // The operator's own configuration, which is where a wrong setting hides. Redacted like
            // everything else: this file can hold a connection string, and a connection string can
            // hold a password.
            if (File.Exists(_paths.LocalSettingsPath))
            {
                await WriteTextEntryAsync(
                    archive,
                    "settings/appsettings.Local.json",
                    SensitiveText.Redact(
                        await ReadAllTextSafelyAsync(_paths.LocalSettingsPath, cancellationToken)
                            .ConfigureAwait(false),
                        knownSecrets),
                    cancellationToken).ConfigureAwait(false);

                contents.Add("settings/appsettings.Local.json");
                redacted++;
            }
        }

        var info = new FileInfo(bundlePath);

        return new DiagnosticsBundle(bundlePath, info.Length, contents, redacted);
    }

    public async Task<string> RecordCrashAsync(
        string source,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);

        Directory.CreateDirectory(_paths.DiagnosticsDirectory);

        var path = Path.Combine(
            _paths.DiagnosticsDirectory,
            "crash-"
            + _clock.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
            + ".txt");

        var text = new StringBuilder();

        text.AppendLine(Line("Crash", source))
            .AppendLine(Line("When (UTC)", _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture)))
            .AppendLine(Line("User", _currentUser.UserName))
            .AppendLine(Line("Machine", Environment.MachineName))
            .AppendLine(Line("App version", ResolveAppVersion()))
            .AppendLine()

            // ToString() rather than just the message: the stack and the inner exceptions are the
            // part that locates the fault, and an operator cannot be asked to expand them.
            .AppendLine(exception.ToString());

        await File.WriteAllTextAsync(path, SensitiveText.Redact(text.ToString()), cancellationToken)
            .ConfigureAwait(false);

        return path;
    }

    /// <summary>
    /// The credentials this install actually holds, so redaction can replace the exact strings and
    /// not only the fields named after them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every failure returns an empty list rather than propagating. The bundle is what an operator
    /// reaches for when the application is already misbehaving, and refusing to produce one because
    /// the credential store could not be read would withhold the diagnostics at the exact moment
    /// they are needed. An empty list still leaves the key-based pass in force.
    /// </para>
    /// <para>
    /// <see cref="CryptographicException"/> is caught by name because it is the expected failure,
    /// not a hypothetical one: DPAPI decrypts for one Windows account, so credentials saved by the
    /// owner cannot be unprotected by a second operator on the same machine (Phase 6 §2).
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> ResolveKnownSecretsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _einvoiceSettings
                .GetEntityAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!settings.HasCredentials)
            {
                return [];
            }

            var credentials = await _einvoiceSettings
                .ResolveCredentialsAsync(cancellationToken)
                .ConfigureAwait(false);

            return
            [
                credentials.ClientId,
                credentials.ClientSecret,
                credentials.Username,
                credentials.Password,
            ];
        }
        catch (CryptographicException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private async Task<string> BuildReportAsync(
        string? operatorNote,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();

        text.AppendLine("Vinayaka Agencies — diagnostics report")
            .AppendLine("=====================================")
            .AppendLine();

        if (!string.IsNullOrWhiteSpace(operatorNote))
        {
            text.AppendLine("What the operator was doing")
                .AppendLine("---------------------------")
                .AppendLine(operatorNote.Trim())
                .AppendLine();
        }

        text.AppendLine("Install")
            .AppendLine("-------")
            .AppendLine(Line("Collected (UTC)", _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture)))
            .AppendLine(Line("App version", ResolveAppVersion()))
            .AppendLine(Line("Signed in as", _currentUser.UserName))
            .AppendLine(Line("Machine", Environment.MachineName))
            .AppendLine(Line("Windows user", Environment.UserName))
            .AppendLine(Line("OS", Environment.OSVersion.VersionString))
            .AppendLine(Line("64-bit process", Environment.Is64BitProcess.ToString()))
            .AppendLine(Line(".NET", Environment.Version.ToString()))
            .AppendLine(Line("Culture", CultureInfo.CurrentCulture.Name))
            .AppendLine()
            .AppendLine("Locations")
            .AppendLine("---------")
            .AppendLine(Line("Data root", _paths.DataRoot))
            .AppendLine(Line("Database", _store.DatabaseFilePath))
            .AppendLine(Line("Backups", _paths.BackupDirectory))
            .AppendLine(Line("Logs", _paths.LogDirectory))
            .AppendLine();

        AppendDatabaseFacts(text);
        AppendDiskFacts(text);

        text.AppendLine("Schema")
            .AppendLine("------");

        // Read rather than assumed. A mismatch between the build and the schema is the single most
        // useful fact in a bundle, and the one an operator is least able to report.
        try
        {
            var migrations = await _store
                .GetAppliedMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);

            text.AppendLine(Line("Applied migrations", migrations.Count.ToString(CultureInfo.InvariantCulture)));

            if (migrations.Count > 0)
            {
                text.AppendLine(Line("Latest", migrations[^1]));
            }
        }
        catch (InvalidOperationException ex)
        {
            text.AppendLine(Line("Applied migrations", "could not be read: " + ex.Message));
        }

        AppendBackupFacts(text);

        return text.ToString();
    }

    private void AppendDatabaseFacts(StringBuilder text)
    {
        text.AppendLine("Database file")
            .AppendLine("-------------");

        var database = new FileInfo(_store.DatabaseFilePath);

        if (!database.Exists)
        {
            text.AppendLine(Line("Present", "no — there is no database at that path"))
                .AppendLine();
            return;
        }

        text.AppendLine(Line("Size", Megabytes(database.Length)))
            .AppendLine(Line(
                "Last written (UTC)",
                database.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture)))
            .AppendLine();
    }

    private void AppendDiskFacts(StringBuilder text)
    {
        text.AppendLine("Disk")
            .AppendLine("----");

        // Free space on the drive holding the books, because "it stopped saving" is very often this
        // and nothing else.
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_paths.DataRoot)!);

            text.AppendLine(Line("Drive", drive.Name))
                .AppendLine(Line("Free", Megabytes(drive.AvailableFreeSpace)))
                .AppendLine(Line("Total", Megabytes(drive.TotalSize)));
        }
        catch (ArgumentException ex)
        {
            text.AppendLine(Line("Drive", "could not be read: " + ex.Message));
        }
        catch (IOException ex)
        {
            text.AppendLine(Line("Drive", "could not be read: " + ex.Message));
        }

        text.AppendLine();
    }

    private void AppendBackupFacts(StringBuilder text)
    {
        text.AppendLine()
            .AppendLine("Backups")
            .AppendLine("-------");

        if (!Directory.Exists(_paths.BackupDirectory))
        {
            text.AppendLine(Line("Archives", "none — the backup folder does not exist"));
            return;
        }

        var archives = Directory
            .EnumerateFiles(_paths.BackupDirectory, "vinayaka-backup-*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        text.AppendLine(Line("Archives", archives.Count.ToString(CultureInfo.InvariantCulture)));

        if (archives.Count == 0)
        {
            // Said plainly rather than left to be inferred from a zero. An install that has never
            // taken a backup is the most important thing in this report.
            text.AppendLine(Line("Warning", "this install has never completed a backup"));
            return;
        }

        var newest = archives[0];

        text.AppendLine(Line(
                "Newest",
                newest.Name + " (" + Megabytes(newest.Length) + ")"))
            .AppendLine(Line(
                "Newest age",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(_clock.UtcNow - newest.LastWriteTimeUtc).TotalDays:F1} days")));
    }

    /// <summary>
    /// Reads a file that another process is writing to, which is exactly the case for today's log.
    /// </summary>
    /// <remarks>
    /// <see cref="File.ReadAllTextAsync(string, CancellationToken)"/> opens without sharing and
    /// would fail against the live Serilog sink — the one log file a diagnostics bundle most needs.
    /// </remarks>
    private static async Task<string> ReadAllTextSafelyAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return "[this file could not be read: " + ex.Message + "]";
        }
        catch (UnauthorizedAccessException ex)
        {
            return "[this file could not be read: " + ex.Message + "]";
        }
    }

    private static List<string> RecentFiles(string directory, string pattern, int take)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, pattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(take)
            .ToList();
    }

    private static async Task WriteTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string text,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string Megabytes(long bytes) => string.Create(
        CultureInfo.InvariantCulture,
        $"{bytes / 1024d / 1024d:F2} MB");

    private static string Line(string label, string value) => string.Create(
        CultureInfo.InvariantCulture,
        $"{label,-22}: {value}");

    private static string ResolveAppVersion() =>
        typeof(DiagnosticsBundleService).Assembly.GetName().Version?.ToString() ?? "unknown";
}
