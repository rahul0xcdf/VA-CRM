using Microsoft.Data.Sqlite;
using MotorPumpSuite.Infrastructure.Configuration;

namespace MotorPumpSuite.Infrastructure.Data;

/// <summary>
/// Works out where the SQLite company database lives.
/// </summary>
/// <remarks>
/// One resolver is shared by the EF Core registration and the Dapper connection factory. If they
/// derived the path independently they could drift, and a reporting query silently reading a
/// different file than the one being written to is a bug that would not surface until the numbers
/// disagreed.
/// </remarks>
internal static class SqliteConnectionStringResolver
{
    public static string Resolve(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return options.ConnectionString;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MotorPumpSuite");

        Directory.CreateDirectory(directory);

        var fileName = string.IsNullOrWhiteSpace(options.DatabaseFileName)
            ? "motorpump.db"
            : options.DatabaseFileName;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, fileName),
            // Creates the file on first run; a missing database on a fresh install is expected,
            // not an error.
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        };

        return builder.ToString();
    }
}
