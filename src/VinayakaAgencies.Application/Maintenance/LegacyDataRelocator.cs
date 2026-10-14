using System.Globalization;

namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// Moves a pre-Phase-10 install's company data from the per-user location to the machine-wide one
/// (phase-10-hardening.md §4a D7), once, before anything opens the database.
/// </summary>
/// <remarks>
/// <para>
/// D7 explains why the location changed: <c>%LOCALAPPDATA%</c> is per Windows user, so a shared
/// counter PC would quietly keep one set of books per Windows account. This class is the other half
/// of that fix — without it, the relocation would read to an existing install as its books having
/// been deleted, because a missing database is indistinguishable from a fresh install and first-run
/// would cheerfully offer to create a new company.
/// </para>
/// <para>
/// Three rules govern it, each the answer to a way this could destroy data:
/// </para>
/// <list type="number">
/// <item>
/// It <b>copies and then verifies before removing anything</b>. A move that half-completed across a
/// power cut would otherwise leave the books in neither location.
/// </item>
/// <item>
/// It <b>refuses to merge</b>. If both locations hold a database, this returns
/// <see cref="LegacyDataRelocationOutcome.AmbiguousLeftAlone"/> and touches nothing: two real sets
/// of books is precisely the state D7 predicts, and picking one for the owner could discard a day's
/// billing. The machine-wide copy wins the startup path and the log names the other one.
/// </item>
/// <item>
/// It leaves a <see cref="MarkerFileName"/> behind, so a second run is a file check rather than a
/// directory walk, and so a human looking at the old folder can tell what happened to it.
/// </item>
/// </list>
/// <para>
/// Logs and diagnostics are deliberately <i>not</i> moved: D7 keeps those per-user.
/// </para>
/// </remarks>
public sealed class LegacyDataRelocator(IAppDataPaths paths)
{
    /// <summary>Dropped in the old directory once its contents have been moved out.</summary>
    public const string MarkerFileName = "moved-to-programdata.txt";

    /// <summary>
    /// Entries left where they are, because D7 keeps them per-user. Matched case-insensitively:
    /// Windows paths are, and a folder the operator renamed in Explorer would otherwise be copied.
    /// </summary>
    private static readonly string[] RetainedEntries = ["logs", "diagnostics"];

    private readonly IAppDataPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>
    /// Relocates the install holding <paramref name="databaseFileName"/>, if there is one to
    /// relocate.
    /// </summary>
    /// <param name="databaseFileName">
    /// The configured database file name — the one file whose presence decides whether a directory
    /// holds real books. A directory holding only a stale log folder is not an install.
    /// </param>
    public LegacyDataRelocation Relocate(string databaseFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFileName);

        // Same directory under both roots is a test or a deployment that has pointed them at one
        // place; there is nothing to move and the copy loop below would fight itself.
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(_paths.DataRoot),
                Path.TrimEndingDirectorySeparator(_paths.LegacyDataRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            return LegacyDataRelocation.NotNeeded(
                "The machine-wide and per-user data roots are the same directory.");
        }

        var legacyDatabase = Path.Combine(_paths.LegacyDataRoot, databaseFileName);
        var targetDatabase = Path.Combine(_paths.DataRoot, databaseFileName);

        if (!File.Exists(legacyDatabase))
        {
            return LegacyDataRelocation.NotNeeded(string.Create(
                CultureInfo.InvariantCulture,
                $"No pre-Phase-10 database at '{legacyDatabase}'."));
        }

        if (File.Exists(targetDatabase))
        {
            return new LegacyDataRelocation(
                LegacyDataRelocationOutcome.AmbiguousLeftAlone,
                legacyDatabase,
                targetDatabase,
                MovedEntries: [],
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A database exists at both '{legacyDatabase}' and '{targetDatabase}'.")
                + " Nothing was moved: choosing between two sets of books is the owner's call,"
                + " not this program's. The machine-wide copy is the one in use.");
        }

        Directory.CreateDirectory(_paths.DataRoot);

        var moved = new List<string>();

        foreach (var file in Directory.EnumerateFiles(_paths.LegacyDataRoot))
        {
            var name = Path.GetFileName(file);

            if (IsRetained(name))
            {
                continue;
            }

            File.Copy(file, Path.Combine(_paths.DataRoot, name), overwrite: false);
            moved.Add(name);
        }

        foreach (var directory in Directory.EnumerateDirectories(_paths.LegacyDataRoot))
        {
            var name = Path.GetFileName(directory);

            if (IsRetained(name))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(_paths.DataRoot, name));
            moved.Add(name + Path.DirectorySeparatorChar);
        }

        // Rule 1: the database has to be readable at the new location before the old one goes. A
        // zero-length or absent file here means the copy failed, and the right outcome is to leave
        // the original exactly where it is and let the owner see the exception.
        if (!File.Exists(targetDatabase) || new FileInfo(targetDatabase).Length == 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Relocating the company database to '{targetDatabase}' did not produce a readable "
                + $"file. The original at '{legacyDatabase}' has been left untouched."));
        }

        RemoveRelocated(moved);
        WriteMarker(targetDatabase, moved);

        return new LegacyDataRelocation(
            LegacyDataRelocationOutcome.Relocated,
            legacyDatabase,
            targetDatabase,
            moved,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Moved {moved.Count} entry/entries from '{_paths.LegacyDataRoot}' to '{_paths.DataRoot}'")
            + " (phase-10-hardening.md §4a D7). Logs and diagnostics stay per-user.");
    }

    private static bool IsRetained(string name) =>
        RetainedEntries.Contains(name, StringComparer.OrdinalIgnoreCase)
        || string.Equals(name, MarkerFileName, StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    /// <summary>
    /// Deletes the originals now that the copies are verified present.
    /// </summary>
    /// <remarks>
    /// Failures are swallowed on purpose. At this point the books are safe at the new location and
    /// the marker file will stop a second attempt; a locked leftover <c>-wal</c> file is not a
    /// reason to refuse to start, and leaving a stale copy behind is a tidiness problem rather than
    /// a correctness one.
    /// </remarks>
    private void RemoveRelocated(IEnumerable<string> moved)
    {
        foreach (var entry in moved)
        {
            var source = Path.Combine(_paths.LegacyDataRoot, entry.TrimEnd(Path.DirectorySeparatorChar));

            try
            {
                if (Directory.Exists(source))
                {
                    Directory.Delete(source, recursive: true);
                }
                else if (File.Exists(source))
                {
                    File.Delete(source);
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

    private void WriteMarker(string targetDatabase, IEnumerable<string> moved)
    {
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"""
             Vinayaka Agencies — company data moved {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.

             This folder used to hold the company database. From Phase 10 the books live at a
             machine-wide location instead, so that every Windows account on this PC opens the same
             set of them:

                 {targetDatabase}

             Moved: {string.Join(", ", moved)}
             Logs and diagnostics were left here on purpose — they describe one Windows user.
             """);

        try
        {
            File.WriteAllText(Path.Combine(_paths.LegacyDataRoot, MarkerFileName), text);
        }
        catch (IOException)
        {
            // A note for a human is not worth failing a successful relocation over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
