namespace VinayakaAgencies.Infrastructure.Configuration;

/// <summary>
/// Database settings bound from the <c>Database</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="Provider"/> is the Phase 9 switch. Phases 1–8 ship single-user SQLite; Phase 9 moves
/// to SQL Server Express. Keeping the choice in configuration — rather than in a compile-time
/// reference — is what makes that migration a settings change plus one new dialect, not a rewrite.
/// See ADR-0001.
/// </remarks>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Provider name: <c>Sqlite</c> today, <c>SqlServer</c> from Phase 9.</summary>
    public string Provider { get; set; } = DatabaseProviders.Sqlite;

    /// <summary>
    /// Explicit connection string. Left blank by default so the SQLite path is derived from
    /// <see cref="DatabaseFileName"/> under <c>%LOCALAPPDATA%</c>; a deployment that needs a
    /// specific location sets this instead.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>File name of the SQLite database when no explicit connection string is given.</summary>
    public string DatabaseFileName { get; set; } = "motorpump.db";

    /// <summary>
    /// Apply pending migrations unattended during startup.
    /// </summary>
    /// <remarks>
    /// <b>Defaults to off from Phase 9</b>, where it shipped as on for Phases 0–8
    /// (phase-0-foundations.md §2.4). The reason is §2: with two or three clients, several of them
    /// racing to migrate one database as they launch is a corruption risk rather than a
    /// convenience. Off means the host finds the pending migrations, names them, and asks one
    /// operator to apply them with everybody else out.
    /// <para>
    /// Turning it back on is a deliberate single-user choice, which is what the test hosts do: the
    /// Phase 1–8 suites each own their database file and nothing else is launching against it.
    /// </para>
    /// </remarks>
    public bool ApplyMigrationsOnStartup { get; set; }
}

/// <summary>Known values for <see cref="DatabaseOptions.Provider"/>.</summary>
public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";

    public const string SqlServer = "SqlServer";
}
