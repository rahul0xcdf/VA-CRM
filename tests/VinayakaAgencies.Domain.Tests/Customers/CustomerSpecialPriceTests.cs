using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Domain.Tests.Customers;

public class CustomerSpecialPriceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    [Fact]
    public void Constructor_FixedPriceBasis_StoresPriceAndNoMargin()
    {
        var specialPrice = new CustomerSpecialPrice(
            CompanyId, CustomerId, ItemId, PriceBasis.FixedPrice, fixedPrice: 999.9999m);

        specialPrice.FixedPrice.ShouldBe(999.9999m);
        specialPrice.FixedPriceMinor.ShouldBe(9_999_999L);
        specialPrice.MarginPercentMinor.ShouldBeNull();
    }

    [Fact]
    public void Constructor_MarginOnSalePriceWithoutPercent_Throws()
    {
        Should.Throw<ArgumentException>(
            () => new CustomerSpecialPrice(
                CompanyId, CustomerId, ItemId, PriceBasis.MarginOnSalePrice));
    }

    [Fact]
    public void SetPrice_SwitchingBasisToFixed_ClearsTheMargin()
    {
        var specialPrice = new CustomerSpecialPrice(
            CompanyId, CustomerId, ItemId, PriceBasis.MarginOnCost, marginPercent: 15m);

        specialPrice.SetPrice(PriceBasis.FixedPrice, fixedPrice: 480m);

        specialPrice.Basis.ShouldBe(PriceBasis.FixedPrice);
        specialPrice.MarginPercent.ShouldBeNull();
        specialPrice.MarginPercentMinor.ShouldBeNull();
        specialPrice.FixedPrice.ShouldBe(480m);
    }

    [Fact]
    public void Constructor_EmptyCustomerId_Throws()
    {
        Should.Throw<ArgumentException>(
            () => new CustomerSpecialPrice(
                CompanyId, Guid.Empty, ItemId, PriceBasis.FixedPrice, fixedPrice: 1m));
    }
}
