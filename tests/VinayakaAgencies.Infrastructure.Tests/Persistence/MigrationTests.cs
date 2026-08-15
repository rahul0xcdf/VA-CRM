using Microsoft.Data.Sqlite;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// Checkpoint C0.2 — the empty-schema migration runs against a real SQLite file.
/// </summary>
public sealed class MigrationTests
{
    [Fact]
    public async Task InitialiseAsync_OnFreshDatabase_AppliesInitialCreate()
    {
        await using var host = new SqliteTestHost();

        var applied = await host.MigrateAsync();

        applied.ShouldContain(name => name.EndsWith("InitialCreate", StringComparison.Ordinal));
        File.Exists(host.DatabasePath).ShouldBeTrue();
    }

    [Fact]
    public async Task InitialiseAsync_OnFreshDatabase_CreatesCompaniesTable()
    {
        await using var host = new SqliteTestHost();

        await host.MigrateAsync();

        var tables = await ReadTableNamesAsync(host.DatabasePath);

        tables.ShouldContain("Companies");
        tables.ShouldContain("__EFMigrationsHistory");
    }

    [Fact]
    public async Task InitialiseAsync_WhenAlreadyCurrent_AppliesNothing()
    {
        await using var host = new SqliteTestHost();

        await host.MigrateAsync();
        var second = await host.MigrateAsync();

        // Startup runs this on every launch; a re-run must be a no-op rather than an error.
        second.ShouldBeEmpty();
    }

    [Fact]
    public async Task InitialCreate_CreatesTheIndexesTheSchemaDeclares()
    {
        await using var host = new SqliteTestHost();

        await host.MigrateAsync();

        await using var connection = new SqliteConnection($"Data Source={host.DatabasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Companies';";

        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        indexes.ShouldContain("IX_Companies_Name");
        indexes.ShouldContain("IX_Companies_Gstin");
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
