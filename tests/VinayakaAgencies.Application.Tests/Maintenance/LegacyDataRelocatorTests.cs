using Shouldly;
using VinayakaAgencies.Application.Maintenance;

namespace VinayakaAgencies.Application.Tests.Maintenance;

/// <summary>
/// The per-user to machine-wide relocation of phase-10-hardening.md §4a D7, which is the
/// application half of checkpoint C10.3: an upgrade preserves all company data.
/// </summary>
/// <remarks>
/// The MSI half of C10.3 is that the installer never owns <c>%ProgramData%</c> and so cannot remove
/// it. This half is the other risk, and the sharper one: Phases 0–9 kept the database under
/// <c>%LOCALAPPDATA%</c>, so an upgrade that simply started reading the new location would present a
/// working install with years of books as a brand-new one and walk the owner into first-run company
/// creation. Nothing would look broken. That is why these tests exist, and why three of them are
/// about refusing to act rather than acting.
/// </remarks>
public sealed class LegacyDataRelocatorTests : IDisposable
{
    private const string DatabaseFileName = "motorpump.db";

    private readonly string _root;
    private readonly AppDataPaths _paths;

    public LegacyDataRelocatorTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "VinayakaAgencies.RelocatorTests",
            Guid.NewGuid().ToString("N"));

        _paths = new AppDataPaths(
            Path.Combine(_root, "programdata"),
            Path.Combine(_root, "localappdata"));
    }

    [Fact]
    public void Relocate_OnAFreshInstallWithNothingToMove_DoesNothing()
    {
        Directory.CreateDirectory(_paths.LegacyDataRoot);

        var result = new LegacyDataRelocator(_paths).Relocate(DatabaseFileName);

        result.Outcome.ShouldBe(LegacyDataRelocationOutcome.NotNeeded);
        result.MovedEntries.ShouldBeEmpty();

        // The explanation names the path it looked at. "Nothing to do" is useless to an owner whose
        // books have apparently vanished; the path tells them where to look next.
        result.Explanation.ShouldContain(_paths.LegacyDataRoot);
    }

    [Fact]
    public void Relocate_WhenTheLegacyRootDoesNotExistAtAll_DoesNothing()
    {
        var result = new LegacyDataRelocator(_paths).Relocate(DatabaseFileName);

        result.Outcome.ShouldBe(LegacyDataRelocationOutcome.NotNeeded);
    }

    [Fact]
    public void Relocate_OnAnUpgradeFromAPerUserInstall_MovesTheBooksAndLeavesAMarker()
    {
        WriteLegacy(DatabaseFileName, "the company database");
        WriteLegacy("appsettings.Local.json", """{"Backup":{"RetainedBackupCount":7}}""");
        WriteLegacy(Path.Combine("Assets", "logo.png"), "the owner's artwork");
        WriteLegacy(Path.Combine("backups", "vinayaka-backup-20261001-090000.zip"), "an old archive");

        // Logs are per-user and must stay where they are (§4a D7).
        WriteLegacy(Path.Combine("logs", "log-20261001.txt"), "yesterday's log");

        var result = new LegacyDataRelocator(_paths).Relocate(DatabaseFileName);

        result.Outcome.ShouldBe(LegacyDataRelocationOutcome.Relocated);

        // The books, the settings, the artwork and the existing backups all arrive.
        File.ReadAllText(Path.Combine(_paths.DataRoot, DatabaseFileName)).ShouldBe("the company database");
        File.ReadAllText(_paths.LocalSettingsPath).ShouldContain("RetainedBackupCount");
        File.ReadAllText(Path.Combine(_paths.DataRoot, "Assets", "logo.png")).ShouldBe("the owner's artwork");
        File.Exists(Path.Combine(_paths.BackupDirectory, "vinayaka-backup-20261001-090000.zip")).ShouldBeTrue();

        // Originals are gone, so there is one set of books rather than two that can drift apart.
        File.Exists(Path.Combine(_paths.LegacyDataRoot, DatabaseFileName)).ShouldBeFalse();

        // Except the logs, which describe what one Windows user did and belong to that user.
        File.Exists(Path.Combine(_paths.LegacyDataRoot, "logs", "log-20261001.txt")).ShouldBeTrue();

        // And a note a human can read, so the old folder does not look like an unexplained ruin.
        var marker = Path.Combine(_paths.LegacyDataRoot, LegacyDataRelocator.MarkerFileName);
        File.Exists(marker).ShouldBeTrue();
        File.ReadAllText(marker).ShouldContain(Path.Combine(_paths.DataRoot, DatabaseFileName));
    }

    [Fact]
    public void Relocate_RunTwice_DoesNothingTheSecondTime()
    {
        WriteLegacy(DatabaseFileName, "the company database");

        var relocator = new LegacyDataRelocator(_paths);

        relocator.Relocate(DatabaseFileName).Outcome.ShouldBe(LegacyDataRelocationOutcome.Relocated);

        // Startup runs this every launch. A second pass must be a cheap no-op, not a second attempt
        // at a move whose source is gone.
        relocator.Relocate(DatabaseFileName).Outcome.ShouldBe(LegacyDataRelocationOutcome.NotNeeded);

        File.ReadAllText(Path.Combine(_paths.DataRoot, DatabaseFileName)).ShouldBe("the company database");
    }

    [Fact]
    public void Relocate_WhenBothLocationsHoldADatabase_RefusesAndTouchesNeither()
    {
        WriteLegacy(DatabaseFileName, "the old per-user books");
        WriteNew(DatabaseFileName, "the machine-wide books already in use");

        var result = new LegacyDataRelocator(_paths).Relocate(DatabaseFileName);

        // The state D7 predicts: two Windows accounts each billed into their own books before the
        // upgrade. Choosing between them is the owner's call — picking one here could silently
        // discard a day's invoices, and neither file is recoverable from the other.
        result.Outcome.ShouldBe(LegacyDataRelocationOutcome.AmbiguousLeftAlone);
        result.MovedEntries.ShouldBeEmpty();

        File.ReadAllText(Path.Combine(_paths.LegacyDataRoot, DatabaseFileName)).ShouldBe("the old per-user books");
        File.ReadAllText(Path.Combine(_paths.DataRoot, DatabaseFileName))
            .ShouldBe("the machine-wide books already in use");

        // And it says where both of them are, because that is the only way the owner can act on it.
        result.Explanation.ShouldContain(_paths.LegacyDataRoot);
        result.Explanation.ShouldContain(_paths.DataRoot);
    }

    [Fact]
    public void Relocate_WhenALegacyRootHoldsOnlyLogs_IsNotTreatedAsAnInstall()
    {
        // A machine where the application was launched and closed without a company ever being
        // created. There are no books to move, and moving the logs would be wrong anyway.
        WriteLegacy(Path.Combine("logs", "log-20261001.txt"), "a log and nothing else");

        var result = new LegacyDataRelocator(_paths).Relocate(DatabaseFileName);

        result.Outcome.ShouldBe(LegacyDataRelocationOutcome.NotNeeded);
        Directory.Exists(Path.Combine(_paths.DataRoot, "logs")).ShouldBeFalse();
    }

    [Fact]
    public void Relocate_WhenBothRootsAreTheSameDirectory_DoesNothingRatherThanFightItself()
    {
        var single = Path.Combine(_root, "one-and-the-same");
        var paths = new AppDataPaths(single, single);

        Directory.CreateDirectory(single);
        File.WriteAllText(Path.Combine(single, DatabaseFileName), "the company database");

        var result = new LegacyDataRelocator(paths).Relocate(DatabaseFileName);

        result.Outcome.ShouldBe(LegacyDataRelocationOutcome.NotNeeded);
        File.ReadAllText(Path.Combine(single, DatabaseFileName)).ShouldBe("the company database");
    }

    [Fact]
    public void Relocate_WithNoDatabaseFileName_Refuses()
    {
        var relocator = new LegacyDataRelocator(_paths);

        // The file name is what distinguishes an install from a directory holding a stale log
        // folder. Defaulting it would make that distinction silent.
        Should.Throw<ArgumentException>(() => relocator.Relocate(string.Empty));
        Should.Throw<ArgumentException>(() => relocator.Relocate("   "));
    }

    [Fact]
    public void AppDataPaths_KeepsCompanyDataMachineWideAndLogsPerUser()
    {
        // The whole of D7 in one assertion. The defect it fixes is that these two were the same
        // root, which made the books per Windows account.
        _paths.BackupDirectory.ShouldStartWith(_paths.DataRoot);
        _paths.LocalSettingsPath.ShouldStartWith(_paths.DataRoot);

        _paths.LogDirectory.ShouldStartWith(_paths.LegacyDataRoot);
        _paths.DiagnosticsDirectory.ShouldStartWith(_paths.LegacyDataRoot);

        _paths.LogDirectory.ShouldNotStartWith(_paths.DataRoot);
    }

    [Fact]
    public void AppDataPaths_EnsureCreated_MakesEveryDirectoryItPromises()
    {
        _paths.EnsureCreated().ShouldBe(_paths.DataRoot);

        Directory.Exists(_paths.DataRoot).ShouldBeTrue();
        Directory.Exists(_paths.BackupDirectory).ShouldBeTrue();
        Directory.Exists(_paths.LogDirectory).ShouldBeTrue();
        Directory.Exists(_paths.DiagnosticsDirectory).ShouldBeTrue();
    }

    [Fact]
    public void AppDataPaths_DefaultConstructor_PointsAtProgramDataAndLocalAppData()
    {
        // Not asserting the literal strings, which differ by machine — asserting the property that
        // matters: company data under the machine-wide root, logs under the per-user one.
        var paths = new AppDataPaths();

        paths.DataRoot.ShouldStartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        paths.LogDirectory.ShouldStartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        paths.DataRoot.ShouldEndWith(AppDataPaths.FolderName);
    }

    private void WriteLegacy(string relativePath, string content) =>
        Write(Path.Combine(_paths.LegacyDataRoot, relativePath), content);

    private void WriteNew(string relativePath, string content) =>
        Write(Path.Combine(_paths.DataRoot, relativePath), content);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
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
