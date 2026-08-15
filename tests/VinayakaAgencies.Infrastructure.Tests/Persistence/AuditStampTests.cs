using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// The audit-stamp seam that Phase 1's edit log will hang off.
/// </summary>
/// <remarks>
/// These stamps are not yet the audit trail required by MCA Rule 3(1) — that arrives in Phase 1 —
/// but the interception point is the one the edit log will use, so it has to be correct and
/// impossible to bypass now.
/// </remarks>
public sealed class AuditStampTests
{
    [Fact]
    public async Task SaveChangesAsync_OnInsert_StampsCreatedAtAndCreatedBy()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero));
        await using var host = new SqliteTestHost(clock, new StubCurrentUser(@"SHOP-PC\rahul"));
        await host.MigrateAsync();

        var id = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Stamped", "Stamped Ltd", "29");

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        var readBack = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .GetByIdAsync(id));

        readBack.ShouldNotBeNull();
        readBack.CreatedAtUtc.ShouldBe(clock.UtcNow);
        readBack.CreatedBy.ShouldBe(@"SHOP-PC\rahul");
        readBack.ModifiedAtUtc.ShouldBeNull();
        readBack.ModifiedBy.ShouldBeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_OnUpdate_StampsModifiedAndLeavesCreationUntouched()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero));
        await using var host = new SqliteTestHost(clock, new StubCurrentUser(@"SHOP-PC\rahul"));
        await host.MigrateAsync();

        var createdAt = clock.UtcNow;

        var id = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Before", "Before Ltd", "29");

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        clock.Advance(TimeSpan.FromHours(3));
        host.CurrentUser.UserName = @"SHOP-PC\anita";

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var repository = unitOfWork.Repository<Company>();

            var company = await repository.GetByIdAsync(id);
            company!.Rename("After", "After Ltd");

            await unitOfWork.SaveChangesAsync();
        });

        var readBack = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .GetByIdAsync(id));

        readBack.ShouldNotBeNull();
        readBack.Name.ShouldBe("After");

        // Who created a record is a fact about the past; a later editor must not be able to
        // overwrite it, or the edit log built on these stamps would be rewritable.
        readBack.CreatedAtUtc.ShouldBe(createdAt);
        readBack.CreatedBy.ShouldBe(@"SHOP-PC\rahul");

        readBack.ModifiedAtUtc.ShouldBe(createdAt.AddHours(3));
        readBack.ModifiedBy.ShouldBe(@"SHOP-PC\anita");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenCurrentUserIsBlank_Throws()
    {
        await using var host = new SqliteTestHost(currentUser: new StubCurrentUser("   "));
        await host.MigrateAsync();

        // An unattributable change is not an audit trail. Failing the save is the correct outcome;
        // writing a blank author would leave a record nobody can be held to.
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(async sp =>
            {
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                await unitOfWork.Repository<Company>()
                    .AddAsync(new Company("Anonymous", "Anonymous Ltd", "29"));
                await unitOfWork.SaveChangesAsync();
            }));
    }
}
