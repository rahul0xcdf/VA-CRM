using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Tests.TestDoubles;
using VinayakaAgencies.Domain.Entities;
using Shouldly;

namespace VinayakaAgencies.Application.Tests.Companies;

/// <summary>
/// <see cref="CompanyService"/> is deliberately thin (see its own remarks): these tests prove the
/// seam — Application → <c>IUnitOfWork</c> → <c>IRepository&lt;Company&gt;</c> — with fakes only, no
/// database, which is the whole point of Phase 0.
/// </summary>
public sealed class CompanyServiceTests
{
    private static CreateCompanyRequest ValidRequest(string name = "Om Traders") => new(
        Name: name,
        LegalName: "Om Traders Private Limited",
        StateCode: "27",
        Gstin: "27ABCDE1234F1Z5",
        FinancialYearStartMonth: 4,
        BaseCurrencyCode: "INR");

    [Fact]
    public void Constructor_NullUnitOfWork_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new CompanyService(null!));
    }

    [Fact]
    public async Task CreateAsync_NullRequest_ThrowsArgumentNullException()
    {
        var sut = new CompanyService(new FakeUnitOfWork());

        await Should.ThrowAsync<ArgumentNullException>(() => sut.CreateAsync(null!));
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_AddsCompanyThroughRepositoryAndSavesExactlyOnce()
    {
        var unitOfWork = new FakeUnitOfWork();
        var sut = new CompanyService(unitOfWork);

        await sut.CreateAsync(ValidRequest());

        var repository = unitOfWork.FakeRepositoryFor<Company>();
        repository.Items.Count.ShouldBe(1);
        unitOfWork.SaveChangesCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsCorrectlyMappedSummary()
    {
        var unitOfWork = new FakeUnitOfWork();
        var sut = new CompanyService(unitOfWork);
        var request = ValidRequest();

        var summary = await sut.CreateAsync(request);

        summary.Id.ShouldNotBe(Guid.Empty);
        summary.Name.ShouldBe(request.Name);
        summary.LegalName.ShouldBe(request.LegalName);
        summary.Gstin.ShouldBe(request.Gstin);
        summary.StateCode.ShouldBe(request.StateCode);
        summary.FinancialYearStartMonth.ShouldBe(request.FinancialYearStartMonth);
        summary.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var sut = new CompanyService(new FakeUnitOfWork());

        var result = await sut.GetAsync(Guid.NewGuid());

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsMappedSummary()
    {
        var unitOfWork = new FakeUnitOfWork();
        var sut = new CompanyService(unitOfWork);
        var created = await sut.CreateAsync(ValidRequest());

        var result = await sut.GetAsync(created.Id);

        result.ShouldNotBeNull();
        result!.Id.ShouldBe(created.Id);
        result.Name.ShouldBe(created.Name);
        result.Gstin.ShouldBe(created.Gstin);
    }

    [Fact]
    public async Task ListAsync_MultipleCompanies_MapsEveryEntityAndPassesTheSpecificationThrough()
    {
        var unitOfWork = new FakeUnitOfWork();
        var sut = new CompanyService(unitOfWork);
        await sut.CreateAsync(ValidRequest("Zenith Pumps"));
        await sut.CreateAsync(ValidRequest("Aarav Motors"));
        await sut.CreateAsync(ValidRequest("Mahesh Engineering"));

        var result = await sut.ListAsync();

        // Order proves the specification's ApplyOrderBy(c => c.Name) actually reached the fake
        // repository rather than being dropped on the floor — the fake applies whatever Criteria/
        // OrderBy it is handed, it does not know CompanyService orders by name.
        result.Count.ShouldBe(3);
        result.Select(c => c.Name).ShouldBe(["Aarav Motors", "Mahesh Engineering", "Zenith Pumps"]);
    }

    [Fact]
    public async Task ListAsync_NoCompanies_ReturnsEmptyList()
    {
        var sut = new CompanyService(new FakeUnitOfWork());

        var result = await sut.ListAsync();

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task CountAsync_DelegatesToRepositoryCount()
    {
        var unitOfWork = new FakeUnitOfWork();
        var sut = new CompanyService(unitOfWork);
        await sut.CreateAsync(ValidRequest("Alpha"));
        await sut.CreateAsync(ValidRequest("Beta"));

        var count = await sut.CountAsync();

        count.ShouldBe(2);
        // And it is genuinely the repository's own count, not a service-side field: adding directly
        // through the same repository instance moves the count CompanyService reports.
        await unitOfWork.FakeRepositoryFor<Company>().AddAsync(
            new Company("Gamma", "Gamma Pvt Ltd", "27"));
        (await sut.CountAsync()).ShouldBe(3);
    }
}
