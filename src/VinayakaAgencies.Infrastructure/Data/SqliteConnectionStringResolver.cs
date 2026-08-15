using Microsoft.Data.Sqlite;
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
/// </remarks>
internal static class SqliteConnectionStringResolver
{
    public static string Resolve(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A supplied string is parsed rather than used verbatim, so that whatever the caller set
        // (data source, cache mode, password) is preserved while the pragma below is still forced.
        var builder = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? BuildDefault(options)
            : new SqliteConnectionStringBuilder(options.ConnectionString);

        // Not negotiable, and deliberately applied after the caller's settings so it cannot be
        // turned off from configuration.
        builder.ForeignKeys = true;

        return builder.ToString();
    }

    private static SqliteConnectionStringBuilder BuildDefault(DatabaseOptions options)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VinayakaAgencies");

        Directory.CreateDirectory(directory);

        var fileName = string.IsNullOrWhiteSpace(options.DatabaseFileName)
            ? "motorpump.db"
            : options.DatabaseFileName;

        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, fileName),

            // Creates the file on first run; a missing database on a fresh install is expected,
            // not an error.
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
    }
}
