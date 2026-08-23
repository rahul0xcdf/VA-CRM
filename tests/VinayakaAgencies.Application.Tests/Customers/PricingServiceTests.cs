using Shouldly;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Tests.TestDoubles;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Tests.Customers;

/// <summary>
/// Checkpoint C4.4: each of the three pricing bases is verified against a hand-computed fixture
/// worked out on paper before the code was written.
/// </summary>
public sealed class PricingServiceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 8, 23);

    // The fixture's cost: 250.0000 per unit, from the Phase 2 valuation service.
    private const long CostMinor = 2_500_000L;

    [Fact]
    public async Task Resolve_FixedPriceLine_ReturnsTheStoredPrice()
    {
        var (service, itemId, customerId) = await ArrangeAsync(
            itemDefaultSalePrice: null,
            categoryLine: new(PriceBasis.FixedPrice, 1_750_000L, null));

        // Hand-computed: the stored 175.0000 passes through untouched.
        var resolved = await service.ResolveForSaleAsync(CompanyId, customerId, itemId);

        resolved.ShouldNotBeNull();
        resolved.Source.ShouldBe(SalePriceSource.CategoryPriceList);
        resolved.Basis.ShouldBe(PriceBasis.FixedPrice);
        resolved.RateMinor.ShouldBe(1_750_000L);
        resolved.BasisValueMinor.ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_MarginOnCost_TwentyPercentOverCost250_Prices300()
    {
        var (service, itemId, customerId) = await ArrangeAsync(
            itemDefaultSalePrice: null,
            categoryLine: new(PriceBasis.MarginOnCost, null, 200_000L));

        // Hand-computed: 250.0000 × 1.20 = 300.0000 → 3_000_000 minor.
        var resolved = await service.ResolveForSaleAsync(CompanyId, customerId, itemId);

        resolved.ShouldNotBeNull();
        resolved.Basis.ShouldBe(PriceBasis.MarginOnCost);
        resolved.RateMinor.ShouldBe(3_000_000L);
        resolved.MarginPercentMinor.ShouldBe(200_000L);
        resolved.BasisValueMinor.ShouldBe(CostMinor);
    }

    [Fact]
    public async Task Resolve_MarginOnSalePrice_TenPercentOffMrp500_Prices450()
    {
        var (service, itemId, customerId) = await ArrangeAsync(
            itemDefaultSalePrice: 5_000_000L,
            categoryLine: new(PriceBasis.MarginOnSalePrice, null, 100_000L));

        // Hand-computed: MRP 500.0000 less 10% = 450.0000 → 4_500_000 minor. The percent is a
        // discount off the MRP on this basis, which is why it subtracts.
        var resolved = await service.ResolveForSaleAsync(CompanyId, customerId, itemId);

        resolved.ShouldNotBeNull();
        resolved.Basis.ShouldBe(PriceBasis.MarginOnSalePrice);
        resolved.RateMinor.ShouldBe(4_500_000L);
        resolved.BasisValueMinor.ShouldBe(5_000_000L);
    }

    [Fact]
    public async Task Resolve_CustomerSpecialPrice_BeatsTheCategoryLine()
    {
        var (service, itemId, customerId) = await ArrangeAsync(
            itemDefaultSalePrice: null,
            categoryLine: new(PriceBasis.FixedPrice, 1_750_000L, null),
            specialPrice: new(PriceBasis.FixedPrice, 1_600_000L, null));

        var resolved = await service.ResolveForSaleAsync(CompanyId, customerId, itemId);

        resolved.ShouldNotBeNull();
        resolved.Source.ShouldBe(SalePriceSource.CustomerSpecialPrice);
        resolved.RateMinor.ShouldBe(1_600_000L);
    }

    [Fact]
    public async Task Resolve_CategoryLine_BeatsTheItemDefault()
    {
        var (service, itemId, customerId) = await ArrangeAsync(
            itemDefaultSalePrice: 9_999_999L,
            categoryLine: new(PriceBasis.FixedPrice, 1_750_000L, null));

        var resolved = await service.ResolveForSaleAsync(CompanyId, customerId, itemId);

        resolved.ShouldNotBeNull();
        resolved.Source.ShouldBe(SalePriceSource.CategoryPriceList);
        resolved.RateMinor.ShouldBe(1_750_000L);
    }

    [Fact]
    public async Task Resolve_NothingConfigured_FallsBackToTheItemDefault()
    {
        var (service, itemId, _) = await ArrangeAsync(itemDefaultSalePrice: 2_950_000L, categoryLine: null);

        var resolved = await service.ResolveForSaleAsync(CompanyId, null, itemId);

        resolved.ShouldNotBeNull();
        resolved.Source.ShouldBe(SalePriceSource.ItemDefault);
        resolved.RateMinor.ShouldBe(2_950_000L);
    }

    [Fact]
    public async Task Resolve_NothingAnywhere_ReturnsNull()
    {
        // No prices, no default: the honest answer is "unpriced", not zero.
        var (service, itemId, customerId) = await ArrangeAsync(
            itemDefaultSalePrice: null, categoryLine: null);

        (await service.ResolveForSaleAsync(CompanyId, customerId, itemId)).ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_MarginOnCostWithoutAnyCosting_FallsThroughToTheItemDefault()
    {
        // An item never received has no cost to margin over: that tier declines and the default
        // sale price answers instead of silently selling at zero.
        var unitOfWork = await BuildWorldAsync(itemDefaultSalePrice: 2_950_000L);
        var item = unitOfWork.FakeRepositoryFor<Item>().Items.Single();
        var categoryLine = new CategoryPriceListLine(
            CompanyId, Guid.NewGuid(), item.Id, PriceBasis.MarginOnCost, marginPercent: 20m);
        await unitOfWork.Repository<CategoryPriceListLine>().AddAsync(categoryLine);

        var customer = NewCustomer(categoryLine.CustomerCategoryId);
        await unitOfWork.Repository<Customer>().AddAsync(customer);

        var service = NewService(unitOfWork, costMinor: 0L);

        var resolved = await service.ResolveForSaleAsync(CompanyId, customer.Id, item.Id);

        resolved.ShouldNotBeNull();
        resolved.Source.ShouldBe(SalePriceSource.ItemDefault);
    }

    private static PricingService NewService(FakeUnitOfWork unitOfWork, long costMinor) =>
        new(
            unitOfWork,
            new StubValuationService(costMinor),
            new FixedClock(new DateTimeOffset(2026, 8, 23, 4, 0, 0, TimeSpan.Zero), Today));

    /// <summary>
    /// Builds the world: one item, one customer in one category, the optional price lines — and
    /// returns the service over it.
    /// </summary>
    private static async Task<(PricingService Service, Guid ItemId, Guid CustomerId)> ArrangeAsync(
        long? itemDefaultSalePrice,
        (PriceBasis Basis, long? FixedPriceMinor, long? MarginPercentMinor)? categoryLine,
        (PriceBasis Basis, long? FixedPriceMinor, long? MarginPercentMinor)? specialPrice = null)
    {
        var unitOfWork = await BuildWorldAsync(itemDefaultSalePrice);

        var item = unitOfWork.FakeRepositoryFor<Item>().Items.Single();
        var category = unitOfWork.FakeRepositoryFor<CustomerCategory>().Items.Single();

        if (categoryLine.HasValue)
        {
            await unitOfWork.Repository<CategoryPriceListLine>().AddAsync(new CategoryPriceListLine(
                CompanyId,
                category.Id,
                item.Id,
                categoryLine.Value.Basis,
                categoryLine.Value.FixedPriceMinor is null
                    ? null
                    : MoneyScale.FromMinor(categoryLine.Value.FixedPriceMinor.Value),
                categoryLine.Value.MarginPercentMinor is null
                    ? null
                    : MoneyScale.FromMinor(categoryLine.Value.MarginPercentMinor.Value)));
        }

        var customer = NewCustomer(category.Id);
        await unitOfWork.Repository<Customer>().AddAsync(customer);

        if (specialPrice.HasValue)
        {
            await unitOfWork.Repository<CustomerSpecialPrice>().AddAsync(new CustomerSpecialPrice(
                CompanyId,
                customer.Id,
                item.Id,
                specialPrice.Value.Basis,
                specialPrice.Value.FixedPriceMinor is null
                    ? null
                    : MoneyScale.FromMinor(specialPrice.Value.FixedPriceMinor.Value),
                specialPrice.Value.MarginPercentMinor is null
                    ? null
                    : MoneyScale.FromMinor(specialPrice.Value.MarginPercentMinor.Value)));
        }

        return (NewService(unitOfWork, CostMinor), item.Id, customer.Id);
    }

    private static async Task<FakeUnitOfWork> BuildWorldAsync(long? itemDefaultSalePrice)
    {
        var unitOfWork = new FakeUnitOfWork();

        var item = new Item(CompanyId, "Cisno Pump 1 HP", Guid.NewGuid());
        if (itemDefaultSalePrice.HasValue)
        {
            item.SetDefaultSalePrice(MoneyScale.FromMinor(itemDefaultSalePrice.Value));
        }

        await unitOfWork.Repository<Item>().AddAsync(item);
        await unitOfWork.Repository<CustomerCategory>()
            .AddAsync(new CustomerCategory(CompanyId, "Dealer"));

        return unitOfWork;
    }

    private static Customer NewCustomer(Guid categoryId) =>
        new(CompanyId, "Ram Motors", Guid.NewGuid(), categoryId);

    /// <summary>Stands in for the Phase 2 costing engine with one fixed unit cost.</summary>
    private sealed class StubValuationService(long unitCostMinor) : IStockValuationService
    {
        public Task<ValuedIssue> ValueIssueAsync(
            Guid itemId,
            decimal quantity,
            DateOnly asOfDate,
            decimal alreadyIssuedQuantity = 0m,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ValuedIssue.Clean(unitCostMinor, ValuationMethod.Fifo));

        public Task<ValuationMethod> EffectiveMethodAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ValuationMethod.Fifo);
    }
}
