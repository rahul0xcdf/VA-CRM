using Shouldly;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Tests.TestDoubles;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Tests.Customers;

public sealed class SalesSettingsServiceTests
{
    [Fact]
    public async Task Get_NoRowYet_CreatesRowAtTheApprovedDefaults()
    {
        // Spec §2: the single-operator install must not start by blocking its own sales, and the
        // override gate starts open — both defaults applied on first touch.
        var unitOfWork = new FakeUnitOfWork();
        var service = new SalesSettingsService(unitOfWork, new StubCompanyContext());

        var settings = await service.GetAsync();

        settings.AllowPriceOverride.ShouldBeTrue();
        settings.CreditLimitPolicy.ShouldBe(CreditLimitPolicy.Warn);
        unitOfWork.FakeRepositoryFor<SalesSettings>().Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Get_ExistingRow_ReturnsItWithoutWriting()
    {
        var unitOfWork = new FakeUnitOfWork();
        var companyId = new StubCompanyContext().CompanyId;
        await unitOfWork.Repository<SalesSettings>()
            .AddAsync(new SalesSettings(companyId, allowPriceOverride: false, CreditLimitPolicy.Block));

        var savesBefore = unitOfWork.SaveChangesCallCount;
        var service = new SalesSettingsService(unitOfWork, new StubCompanyContext(companyId));

        var settings = await service.GetAsync();

        settings.AllowPriceOverride.ShouldBeFalse();
        settings.CreditLimitPolicy.ShouldBe(CreditLimitPolicy.Block);
        unitOfWork.SaveChangesCallCount.ShouldBe(savesBefore);
    }

    [Fact]
    public async Task Update_ChangesPolicyAndGate()
    {
        var unitOfWork = new FakeUnitOfWork();
        var service = new SalesSettingsService(unitOfWork, new StubCompanyContext());

        await service.GetAsync();
        var updated = await service.UpdateAsync(
            new SaveSalesSettingsRequest(AllowPriceOverride: false, CreditLimitPolicy.Block));

        updated.AllowPriceOverride.ShouldBeFalse();
        updated.CreditLimitPolicy.ShouldBe(CreditLimitPolicy.Block);

        // The change is visible to a fresh read of the same row.
        (await service.GetAsync()).CreditLimitPolicy.ShouldBe(CreditLimitPolicy.Block);
    }
}
