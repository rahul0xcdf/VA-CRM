using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Auditing;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// The edit log required by MCA Rule 3(1) and spec §11 — the project's third non-negotiable.
/// </summary>
/// <remarks>
/// These cover checkpoint criteria C1.3 (every change is logged with user, timestamp and before and
/// after values), C1.4 defences (b) and (c) (the log cannot be written to or removed), and C1.16
/// (the hash chain verifies). The remaining C1.4 defences are asserted elsewhere: (a) is a
/// compile-time property of <c>AuditLogEntry</c> not deriving from <c>Entity</c>, and (d) and (e)
/// are source and configuration scans that belong with the Phase 1 architecture tests.
/// </remarks>
public sealed class AuditTrailCaptureTests
{
    [Fact]
    public async Task SaveChangesAsync_OnCreate_WritesAnAfterImageAndNoBeforeImage()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero));
        await using var host = new SqliteTestHost(clock, new StubCurrentUser(@"SHOP-PC\rahul"));
        await host.MigrateAsync();

        await AddCompanyAsync(host, "Vinayaka", "Vinayaka Agencies", "29");

        var rows = await host.InScopeAsync(AuditLogAccess.AllAsync);
        var row = rows.ShouldHaveSingleItem();

        row.Action.ShouldBe(AuditAction.Create);
        row.EntityType.ShouldBe(nameof(Company));
        row.UserName.ShouldBe(@"SHOP-PC\rahul");
        row.OccurredAtUtc.ShouldBe(clock.UtcNow);

        // Null rather than "{}" — there was no prior state, and an empty object would read as
        // "it existed and every field was blank".
        row.BeforeJson.ShouldBeNull();
        row.AfterJson.ShouldNotBeNullOrWhiteSpace();

        using var after = JsonDocument.Parse(row.AfterJson!);
        after.RootElement.GetProperty(nameof(Company.Name)).GetString().ShouldBe("Vinayaka");
    }

    [Fact]
    public async Task SaveChangesAsync_OnUpdate_WritesBothImagesAndTheChangedProperties()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero));
        await using var host = new SqliteTestHost(clock, new StubCurrentUser(@"SHOP-PC\rahul"));
        await host.MigrateAsync();

        var id = await AddCompanyAsync(host, "Before", "Before Ltd", "29");

        clock.Advance(TimeSpan.FromHours(2));
        host.CurrentUser.UserName = @"SHOP-PC\anita";

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = await unitOfWork.Repository<Company>().GetByIdAsync(id);
            company!.Rename("After", "After Ltd");
            await unitOfWork.SaveChangesAsync();
        });

        var rows = await host.InScopeAsync(AuditLogAccess.AllAsync);
        rows.Count.ShouldBe(2);

        var update = rows[1];
        update.Action.ShouldBe(AuditAction.Update);
        update.UserName.ShouldBe(@"SHOP-PC\anita");
        update.ChangedProperties.ShouldContain(nameof(Company.Name));

        using var before = JsonDocument.Parse(update.BeforeJson.ShouldNotBeNull());
        using var after = JsonDocument.Parse(update.AfterJson.ShouldNotBeNull());

        before.RootElement.GetProperty(nameof(Company.Name)).GetString().ShouldBe("Before");
        after.RootElement.GetProperty(nameof(Company.Name)).GetString().ShouldBe("After");
    }

    /// <summary>
    /// C1.16, and the case the architect review singled out: EF Core does not formally guarantee
    /// the order of inserts within one batch, and the chain depends on rows landing in the order
    /// they were sealed. Three rows from a single <c>SaveChanges</c> is the shape that would expose
    /// it.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_WritingSeveralRowsAtOnce_ProducesAVerifiableChain()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var repository = unitOfWork.Repository<Company>();

            await repository.AddAsync(new Company("One", "One Ltd", "29"));
            await repository.AddAsync(new Company("Two", "Two Ltd", "29"));
            await repository.AddAsync(new Company("Three", "Three Ltd", "29"));

            await unitOfWork.SaveChangesAsync();
        });

        var rows = await host.InScopeAsync(AuditLogAccess.AllAsync);
        rows.Count.ShouldBe(3);

        // Every row is sealed over its own content...
        foreach (var row in rows)
        {
            row.VerifyHash().ShouldBeTrue();
        }

        // ...and linked to the one before it, with the first anchored to the genesis value.
        rows[0].PreviousHash.ShouldBe(AuditLogEntry.GenesisHash);
        rows[1].PreviousHash.ShouldBe(rows[0].RowHash);
        rows[2].PreviousHash.ShouldBe(rows[1].RowHash);

        // One action, one batch id — a voucher and its lines must read back as the single act
        // they were (§11.1).
        rows.Select(row => row.SaveBatchId).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenALogRowIsMarkedModified_ThrowsTamperException()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await AddCompanyAsync(host, "Vinayaka", "Vinayaka Agencies", "29");

        await Should.ThrowAsync<AuditTrailTamperException>(async () =>
            await host.InScopeAsync(async sp =>
            {
                var context = AuditLogAccess.RawContext(sp);
                var row = await context.Set<AuditLogEntry>().FirstAsync();

                // The properties have no public setters, so this is the only way to reach the
                // state the guard exists to reject — which is the point: defence 1 is the type
                // system, and this test stands where defence 1 cannot see.
                context.Entry(row).State = EntityState.Modified;

                await context.SaveChangesAsync();
            }));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenALogRowIsMarkedDeleted_ThrowsTamperException()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await AddCompanyAsync(host, "Vinayaka", "Vinayaka Agencies", "29");

        await Should.ThrowAsync<AuditTrailTamperException>(async () =>
            await host.InScopeAsync(async sp =>
            {
                var context = AuditLogAccess.RawContext(sp);
                var row = await context.Set<AuditLogEntry>().FirstAsync();

                context.Remove(row);

                await context.SaveChangesAsync();
            }));
    }

    /// <summary>
    /// C1.4 defence (c). A raw connection never reaches EF, so neither the type system nor the
    /// <c>SaveChangesAsync</c> guard can see it. Only the database can refuse.
    /// </summary>
    [Theory]
    [InlineData("UPDATE AuditLogEntries SET UserName = 'forged' WHERE Sequence = 1")]
    [InlineData("DELETE FROM AuditLogEntries WHERE Sequence = 1")]
    public async Task RawConnection_AlteringOrRemovingALogRow_IsAbortedByTheTrigger(string sql)
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await AddCompanyAsync(host, "Vinayaka", "Vinayaka Agencies", "29");

        await host.InScopeAsync(async sp =>
        {
            var factory = sp.GetRequiredService<IDbConnectionFactory>();
            await using var connection = await factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            var thrown = await Should.ThrowAsync<DbException>(async () =>
                await command.ExecuteNonQueryAsync());

            thrown.Message.ShouldContain("append-only");
        });

        // And the row is still there, unchanged.
        var rows = await host.InScopeAsync(AuditLogAccess.AllAsync);
        rows.ShouldHaveSingleItem().UserName.ShouldNotBe("forged");
    }

    /// <summary>
    /// The synchronous save path is closed off rather than left as a second, unaudited way in.
    /// </summary>
    [Fact]
    public async Task SaveChanges_Synchronous_IsNotSupported()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(sp =>
        {
            var context = AuditLogAccess.RawContext(sp);
            context.Set<Company>().Add(new Company("Sync", "Sync Ltd", "29"));

            Should.Throw<NotSupportedException>(() => context.SaveChanges());

            return Task.CompletedTask;
        });
    }

    private static Task<Guid> AddCompanyAsync(
        SqliteTestHost host,
        string name,
        string legalName,
        string stateCode) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company(name, legalName, stateCode);

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });
}
