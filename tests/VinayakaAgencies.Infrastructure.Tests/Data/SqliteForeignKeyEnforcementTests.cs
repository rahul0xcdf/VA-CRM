using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Data;

/// <summary>
/// SQLite enforces foreign keys only when the connection says so.
/// </summary>
/// <remarks>
/// The pragma is off by default and is per-connection, not per-database, so a connection string
/// that omits it leaves every foreign key in the schema decorative — the migration still creates
/// the constraints, the model still declares them, and orphaned rows appear anyway. That is
/// exactly the failure mode Phase 1's schema cannot afford: spec §5 configures every relationship
/// <c>DeleteBehavior.Restrict</c> so that <c>Ledger → JournalLine</c> gives §4.6's "hard delete
/// only when the row has no journal line" a database-level backstop, and a backstop that is not
/// enforced is not a backstop.
/// <para>
/// Asserted against a connection from <see cref="IDbConnectionFactory"/> — the Dapper reporting
/// path — and against a test host configured the way the application is, with an explicit
/// <c>Database:ConnectionString</c>. That combination is the one that used to slip through.
/// </para>
/// </remarks>
public sealed class SqliteForeignKeyEnforcementTests
{
    [Fact]
    public async Task OpenConnectionAsync_WithAnExplicitConnectionString_EnforcesForeignKeys()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var pragma = await host.InScopeAsync(async sp =>
        {
            var factory = sp.GetRequiredService<IDbConnectionFactory>();
            await using var connection = await factory.OpenConnectionAsync();

            return await ReadForeignKeysPragmaAsync(connection);
        });

        pragma.ShouldBe(1L,
            "PRAGMA foreign_keys must read 1 on every connection the factory hands out; "
            + "at 0 SQLite silently ignores every foreign key in the schema.");
    }

    [Fact]
    public async Task OpenConnectionAsync_OnASecondConnection_StillEnforcesForeignKeys()
    {
        // The pragma is per-connection and SQLite pools connections. A second open must be as
        // enforced as the first, or the guarantee holds only until the pool is warm.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var pragmas = await host.InScopeAsync(async sp =>
        {
            var factory = sp.GetRequiredService<IDbConnectionFactory>();

            await using var first = await factory.OpenConnectionAsync();
            await using var second = await factory.OpenConnectionAsync();

            return new[]
            {
                await ReadForeignKeysPragmaAsync(first),
                await ReadForeignKeysPragmaAsync(second),
            };
        });

        pragmas.ShouldAllBe(value => value == 1L);
    }

    private static async Task<long> ReadForeignKeysPragmaAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
