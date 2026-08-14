namespace MotorPumpSuite.Infrastructure.Configuration;

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
    /// Apply pending migrations during startup.
    /// </summary>
    /// <remarks>
    /// Safe while the product is single-user (phase-0-foundations.md §2.4). Phase 9 must turn this
    /// into an explicit, gated step: several clients racing to migrate the same SQL Server database
    /// on launch is a corruption risk, not a convenience.
    /// </remarks>
    public bool ApplyMigrationsOnStartup { get; set; } = true;
}

/// <summary>Known values for <see cref="DatabaseOptions.Provider"/>.</summary>
public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";

    public const string SqlServer = "SqlServer";
}
