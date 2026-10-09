using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Shouldly;

// Aliased because this assembly has its own Tests.Persistence namespace, which would otherwise win
// the lookup for an unqualified `Persistence.` prefix.
using AppDbContext = VinayakaAgencies.Infrastructure.Persistence.AppDbContext;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// Optimistic concurrency — the lost-update half of `phase-9-multiuser.md` §2 and C9.1.
/// </summary>
/// <remarks>
/// <b>What these prove and what they do not.</b> They prove the mechanism: a token exists on every
/// mutable entity, it rotates on write, a second writer who loaded the same row is refused, and the
/// first writer's change survives. Two scopes against one SQLite file is a real second connection
/// with its own change tracker, which is what makes the refusal meaningful rather than a change-tracker
/// artefact.
/// <para>
/// They do not prove C9.1 as written — "two clients transact concurrently" against SQL Server
/// Express — because this machine has no instance (<c>open-questions.md</c> #24). SQLite's locking
/// is not SQL Server's, so the timing behaviour under genuine contention is unverified. The
/// <i>detection</i> is provider-independent by construction, which is exactly why the token is a
/// domain-managed <see cref="Guid"/> rather than a <c>rowversion</c>.
/// </para>
/// </remarks>
public sealed class ConcurrencyTokenTests
{
    [Fact]
    public async Task Model_Always_PutsAConcurrencyTokenOnEveryMutableEntity()
    {
        // The rule is "every IAuditStamped entity", which is where the model already draws the line
        // between a row that can be updated and one that cannot. A fixed list would rot; this
        // cannot, because a new mutable entity is covered the moment it implements the interface.
        var model = await ReadModelAsync();

        var missing = model.GetEntityTypes()
            .Where(entityType => entityType.ClrType is not null
                && typeof(IAuditStamped).IsAssignableFrom(entityType.ClrType))
            .Where(entityType =>
            {
                var property = entityType.FindProperty("ConcurrencyToken");

                return property is null || !property.IsConcurrencyToken;
            })
            .Select(entityType => entityType.DisplayName())
            .ToList();

        missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_Always_PutsNoConcurrencyTokenOnAnAppendOnlyEntity()
    {
        // A token on an append-only row would be dead weight, and would imply those rows can be
        // updated — which ADR-0002 forbids for a posted revision or journal line.
        var model = await ReadModelAsync();

        var offenders = model.GetEntityTypes()
            .Where(entityType => entityType.ClrType is not null
                && !typeof(IAuditStamped).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => entityType.FindProperty("ConcurrencyToken") is not null)
            .Select(entityType => entityType.DisplayName())
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_Always_CoversTheEntitiesThisPhaseCaresAboutMost()
    {
        // The interface rule above is the real guard. This names a handful explicitly so that a
        // refactor which accidentally stopped an entity implementing IAuditStamped — and so passed
        // the rule by shrinking it — still fails here.
        var model = await ReadModelAsync();

        foreach (var type in new[] { typeof(Ledger), typeof(Customer), typeof(Voucher), typeof(Company) })
        {
            var property = model.FindEntityType(type)?.FindProperty("ConcurrencyToken");

            property.ShouldNotBeNull($"{type.Name} must carry a concurrency token.");
            property.IsConcurrencyToken.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task SaveChangesAsync_AnUpdate_RotatesTheToken()
    {
        // If the token did not change on write, every other test here would pass vacuously: the
        // second writer's WHERE clause would keep matching for ever.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddCompanyAsync(host);

        var before = await TokenOfCompanyAsync(host, companyId);

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = await unitOfWork.Repository<Company>().GetByIdAsync(companyId);

            company!.Rename("Vinayaka Agencies & Sons", "Vinayaka Agencies & Sons (Regd.)");
            unitOfWork.Repository<Company>().Update(company);
            await unitOfWork.SaveChangesAsync();
        });

        var after = await TokenOfCompanyAsync(host, companyId);

        after.ShouldNotBe(before);
    }

    [Fact]
    public async Task SaveChangesAsync_TwoScopesEditTheRowTheyBothLoaded_TheSecondIsRefused()
    {
        // C9.1's mechanism. Two scopes means two DbContexts and two change trackers, so this is the
        // real shape of the failure: both read, both change, both save.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddCompanyAsync(host);

        await using var firstScope = host.CreateScope();
        await using var secondScope = host.CreateScope();

        var firstUnitOfWork = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var secondUnitOfWork = secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var asFirstSawIt = await firstUnitOfWork.Repository<Company>().GetByIdAsync(companyId);
        var asSecondSawIt = await secondUnitOfWork.Repository<Company>().GetByIdAsync(companyId);

        asFirstSawIt!.Rename("Renamed by the office", "Renamed by the office (Regd.)");
        firstUnitOfWork.Repository<Company>().Update(asFirstSawIt);
        await firstUnitOfWork.SaveChangesAsync();

        asSecondSawIt!.Rename("Renamed by the counter", "Renamed by the counter (Regd.)");
        secondUnitOfWork.Repository<Company>().Update(asSecondSawIt);

        var refusal = await Record.ExceptionAsync(() => secondUnitOfWork.SaveChangesAsync());

        refusal.ShouldBeOfType<ConcurrentUpdateException>();
    }

    [Fact]
    public async Task SaveChangesAsync_TwoScopesEditTheRowTheyBothLoaded_TheFirstWriteSurvives()
    {
        // The half that matters to the owner. "Detected" is only useful if the earlier change is
        // still there afterwards — a silent overwrite is the failure §2 names.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddCompanyAsync(host);

        await using var firstScope = host.CreateScope();
        await using var secondScope = host.CreateScope();

        var firstUnitOfWork = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var secondUnitOfWork = secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var asFirstSawIt = await firstUnitOfWork.Repository<Company>().GetByIdAsync(companyId);
        var asSecondSawIt = await secondUnitOfWork.Repository<Company>().GetByIdAsync(companyId);

        asFirstSawIt!.Rename("Renamed by the office", "Renamed by the office (Regd.)");
        firstUnitOfWork.Repository<Company>().Update(asFirstSawIt);
        await firstUnitOfWork.SaveChangesAsync();

        asSecondSawIt!.Rename("Renamed by the counter", "Renamed by the counter (Regd.)");
        secondUnitOfWork.Repository<Company>().Update(asSecondSawIt);
        await Record.ExceptionAsync(() => secondUnitOfWork.SaveChangesAsync());

        var persisted = await host.InScopeAsync(async sp =>
        {
            var company = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<Company>()
                .GetByIdAsync(companyId);

            return company!.Name;
        });

        persisted.ShouldBe("Renamed by the office");
    }

    [Fact]
    public async Task SaveChangesAsync_ALostUpdate_NamesTheRecordToReload()
    {
        // The operator has to be told what to go and reload. "A concurrency violation occurred" is
        // true and useless at a billing counter.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddCompanyAsync(host);

        await using var firstScope = host.CreateScope();
        await using var secondScope = host.CreateScope();

        var firstUnitOfWork = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var secondUnitOfWork = secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var asFirstSawIt = await firstUnitOfWork.Repository<Company>().GetByIdAsync(companyId);
        var asSecondSawIt = await secondUnitOfWork.Repository<Company>().GetByIdAsync(companyId);

        asFirstSawIt!.Rename("Renamed by the office", "Renamed by the office (Regd.)");
        firstUnitOfWork.Repository<Company>().Update(asFirstSawIt);
        await firstUnitOfWork.SaveChangesAsync();

        asSecondSawIt!.Rename("Renamed by the counter", "Renamed by the counter (Regd.)");
        secondUnitOfWork.Repository<Company>().Update(asSecondSawIt);

        var refusal = (ConcurrentUpdateException)(await Record.ExceptionAsync(
            () => secondUnitOfWork.SaveChangesAsync()))!;

        refusal.EntityName.ShouldBe(nameof(Company));
        refusal.Message.ShouldContain("Reload it");
    }

    [Fact]
    public async Task SaveChangesAsync_ASecondSaveFromTheSameScope_IsNotMistakenForAConflict()
    {
        // The token rotates on every write, so a scope that saves the same entity twice has to pick
        // up its own new value. If it did not, ordinary two-step work — save, adjust, save again —
        // would report a phantom conflict against nobody.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddCompanyAsync(host);

        var secondSave = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = await unitOfWork.Repository<Company>().GetByIdAsync(companyId);

            company!.Rename("First change", "First change (Regd.)");
            unitOfWork.Repository<Company>().Update(company);
            await unitOfWork.SaveChangesAsync();

            company.Rename("Second change", "Second change (Regd.)");
            unitOfWork.Repository<Company>().Update(company);

            return await Record.ExceptionAsync(() => unitOfWork.SaveChangesAsync());
        });

        secondSave.ShouldBeNull();
    }

    private static async Task<IModel> ReadModelAsync()
    {
        await using var host = new SqliteTestHost();

        return await host.InScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<AppDbContext>().Model));
    }

    private static Task<Guid> TokenOfCompanyAsync(SqliteTestHost host, Guid companyId) =>
        host.InScopeAsync(async sp =>
        {
            var company = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<Company>()
                .GetByIdAsync(companyId);

            // Read off the entity rather than through the change tracker: the token being a real
            // property is the whole point — a shadow property would not survive the no-tracking
            // read plus detached Update that half this codebase does.
            return company!.ConcurrencyToken;
        });

    private static Task<Guid> AddCompanyAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });
}
