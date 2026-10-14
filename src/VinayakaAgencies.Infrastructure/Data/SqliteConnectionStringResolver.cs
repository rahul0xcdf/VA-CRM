using Microsoft.Data.Sqlite;
using VinayakaAgencies.Application.Maintenance;
using VinayakaAgencies.Infrastructure.Configuration;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// Works out where the SQLite company database lives, and on what terms it is opened.
/// </summary>
/// <remarks>
/// One resolver is shared by the EF Core registration and the Dapper connection factory. If they
/// derived the path independently they could drift, and a reporting query silently reading a
/// different file than the one being written to is a bug that would not surface until the numbers
/// disagreed.
/// <para>
/// Every connection string it returns — supplied or derived — carries <c>Foreign Keys=True</c>.
/// SQLite enforces foreign keys only when the pragma is set on the connection, and it is off by
/// default, so a supplied connection string that omitted it would make every foreign key in the
/// schema decorative. That is a silent failure: the schema would look correct in the migration and
/// in the model, and orphaned rows would appear only in the data.
/// </para>
/// <para>
/// From Phase 10 the derived location comes from <see cref="IAppDataPaths"/> rather than being
/// built here: §4a D7 moved company data to the machine-wide <c>%ProgramData%</c> root, and the one
/// thing that must not happen is this resolver and the relocator disagreeing about where the
/// database is. A supplied <see cref="DatabaseOptions.ConnectionString"/> still wins outright —
/// that is how both test hosts point at a throwaway file, and how a deployment overrides the
/// default.
/// </para>
/// </remarks>
internal static class SqliteConnectionStringResolver
{
    /// <summary>Fallback name when configuration leaves <c>DatabaseFileName</c> blank.</summary>
    public const string DefaultDatabaseFileName = "motorpump.db";

    public static string Resolve(DatabaseOptions options, IAppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);

        // A supplied string is parsed rather than used verbatim, so that whatever the caller set
        // (data source, cache mode, password) is preserved while the pragma below is still forced.
        var builder = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? BuildDefault(options, paths)
            : new SqliteConnectionStringBuilder(options.ConnectionString);

        // Not negotiable, and deliberately applied after the caller's settings so it cannot be
        // turned off from configuration.
        builder.ForeignKeys = true;

        return builder.ToString();
    }

    /// <summary>
    /// The database file this configuration resolves to, whether it was supplied or derived.
    /// </summary>
    /// <remarks>
    /// Phase 10's backup service needs the file itself, not a connection string: a snapshot is
    /// taken of a path and written beside it (§4a D1). Deriving it from the resolved connection
    /// string rather than recomputing it is what keeps a backup from ever being taken of a
    /// different file than the one being billed into — including under a test host, whose backups
    /// then land in its own temp directory.
    /// </remarks>
    public static string ResolveDatabaseFilePath(DatabaseOptions options, IAppDataPaths paths) =>
        new SqliteConnectionStringBuilder(Resolve(options, paths)).DataSource;

    /// <summary>Name of the configured database file, with the default applied.</summary>
    public static string ResolveDatabaseFileName(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.DatabaseFileName)
            ? DefaultDatabaseFileName
            : options.DatabaseFileName;
    }

    private static SqliteConnectionStringBuilder BuildDefault(
        DatabaseOptions options,
        IAppDataPaths paths)
    {
        paths.EnsureCreated();

        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(paths.DataRoot, ResolveDatabaseFileName(options)),

            // Creates the file on first run; a missing database on a fresh install is expected,
            // not an error.
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
    }
}
