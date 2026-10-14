namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// The real locations: <c>%ProgramData%\VinayakaAgencies</c> for company data,
/// <c>%LOCALAPPDATA%\VinayakaAgencies</c> for one operator's logs and crash reports.
/// </summary>
/// <remarks>
/// Plain BCL path arithmetic, which is why it sits in the application layer rather than behind an
/// infrastructure port: there is nothing here for a provider to decide, and the backup service, the
/// SQLite resolver and the log configuration all need the same answer.
/// <para>
/// The folder name is spelled out rather than taken from the assembly name. An assembly rename —
/// which this project has already done once (<c>MotorPumpSuite</c> → <c>VinayakaAgencies</c>,
/// open-questions.md #7) — would otherwise silently point a working install at an empty directory
/// and walk the operator through first-run as though the books had never existed.
/// </para>
/// </remarks>
public sealed class AppDataPaths : IAppDataPaths
{
    /// <summary>The single spelling of the product's folder name under both roots.</summary>
    public const string FolderName = "VinayakaAgencies";

    /// <summary>The Windows locations. Used by the product; tests pass their own roots.</summary>
    public AppDataPaths()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                FolderName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FolderName))
    {
    }

    /// <summary>
    /// Explicit roots, so a test can exercise the real layout without writing to
    /// <c>%ProgramData%</c> — which on a developer machine needs elevation and on a build agent is
    /// shared between runs.
    /// </summary>
    public AppDataPaths(string dataRoot, string legacyDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyDataRoot);

        DataRoot = Path.GetFullPath(dataRoot);
        LegacyDataRoot = Path.GetFullPath(legacyDataRoot);
    }

    public string DataRoot { get; }

    public string LegacyDataRoot { get; }

    /// <summary>
    /// Logs stay under the legacy per-user root, which for logs was never the wrong place.
    /// </summary>
    /// <remarks>
    /// Unchanged from Phase 0 on purpose: the path is in <c>CLAUDE.md</c>, in the Phase 0 evidence
    /// and in whatever note the owner has about where to look when something goes wrong. Moving it
    /// would invalidate all three to no benefit.
    /// </remarks>
    public string LogDirectory => Path.Combine(LegacyDataRoot, "logs");

    public string DiagnosticsDirectory => Path.Combine(LegacyDataRoot, "diagnostics");

    public string BackupDirectory => Path.Combine(DataRoot, "backups");

    public string LocalSettingsPath => Path.Combine(DataRoot, "appsettings.Local.json");

    public string EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);

        return DataRoot;
    }
}
