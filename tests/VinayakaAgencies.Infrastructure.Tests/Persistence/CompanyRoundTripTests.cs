using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// Checkpoint C0.3 — a single entity round-trips through the repository and unit of work.
/// </summary>
/// <remarks>
/// Every read here happens in a scope of its own, so the assertion is about what reached the
/// database rather than about what is still sitting in a change tracker.
/// </remarks>
public sealed class CompanyRoundTripTests
{
    [Fact]
    public async Task Company_CanBeRoundTripped_ThroughRepositoryAndUnitOfWork()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var id = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company(
                name: "Vinayaka Agencies",
                legalName: "Vinayaka Agencies Private Limited",
                stateCode: "29",
                gstin: "29ABCDE1234F1Z5");

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        var readBack = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .GetByIdAsync(id));

        readBack.ShouldNotBeNull();
        readBack.Id.ShouldBe(id);
        readBack.Name.ShouldBe("Vinayaka Agencies");
        readBack.LegalName.ShouldBe("Vinayaka Agencies Private Limited");
        readBack.StateCode.ShouldBe("29");
        readBack.Gstin.ShouldBe("29ABCDE1234F1Z5");
        readBack.FinancialYearStartMonth.ShouldBe(4);
        readBack.BaseCurrencyCode.ShouldBe("INR");
        readBack.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task AddAsync_WithoutSaveChanges_PersistsNothing()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            await unitOfWork.Repository<Company>()
                .AddAsync(new Company("Unsaved", "Unsaved Ltd", "29"));
        });

        // The commit boundary belongs to the unit of work alone; a repository that could persist
        // on its own would make Phase 3's atomic invoice posting impossible.
        var count = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .CountAsync());

        count.ShouldBe(0);
    }

    [Fact]
    public async Task Repository_UsesTheIdAssignedOnTheClient()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var company = new Company("Client Id", "Client Id Ltd", "29");
        var expectedId = company.Id;

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();
        });

        // Phase 8's Tally export depends on a record's GUID surviving an export / re-import cycle,
        // which only holds if the database never generates one of its own.
        company.Id.ShouldBe(expectedId);

        var readBack = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Company>()
            .GetByIdAsync(expectedId));

        readBack.ShouldNotBeNull();
    }

    [Fact]
    public async Task ListAsync_AppliesTheSpecificationOrdering()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var repository = unitOfWork.Repository<Company>();

            await repository.AddAsync(new Company("Zenith Pumps", "Zenith Pumps Ltd", "29"));
            await repository.AddAsync(new Company("Anand Motors", "Anand Motors Ltd", "29"));
            await unitOfWork.SaveChangesAsync();
        });

        var names = await host.InScopeAsync(async sp =>
        {
            var companies = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<Company>()
                .ListAsync(new CompaniesByNameSpecification());

            return companies.Select(c => c.Name).ToList();
        });

        names.ShouldBe(["Anand Motors", "Zenith Pumps"]);
    }

    private sealed class CompaniesByNameSpecification : Specification<Company>
    {
        public CompaniesByNameSpecification()
        {
            ApplyOrderBy(c => c.Name);
            ApplyNoTracking();
        }
    }
}
