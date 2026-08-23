using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Domain.Tests.Customers;

public class CategoryPriceListLineTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    [Fact]
    public void Constructor_FixedPriceBasis_StoresPriceAndNoMargin()
    {
        var line = new CategoryPriceListLine(
            CompanyId, CategoryId, ItemId, PriceBasis.FixedPrice, fixedPrice: 1250.5m);

        line.Basis.ShouldBe(PriceBasis.FixedPrice);
        line.FixedPrice.ShouldBe(1250.5m);
        line.FixedPriceMinor.ShouldBe(12_505_000L);
        line.MarginPercent.ShouldBeNull();
        line.MarginPercentMinor.ShouldBeNull();
    }

    [Fact]
    public void Constructor_MarginBasisWithoutPercent_Throws()
    {
        Should.Throw<ArgumentException>(
            () => new CategoryPriceListLine(
                CompanyId, CategoryId, ItemId, PriceBasis.MarginOnCost));
    }

    [Fact]
    public void Constructor_FixedPriceBasisWithoutPrice_Throws()
    {
        Should.Throw<ArgumentException>(
            () => new CategoryPriceListLine(
                CompanyId, CategoryId, ItemId, PriceBasis.FixedPrice, marginPercent: 10m));
    }

    [Fact]
    public void SetPrice_FixedPriceLineAlsoCarryingMargin_Throws()
    {
        var line = NewFixedPriceLine();

        var rejection = Should.Throw<ArgumentException>(
            () => line.SetPrice(PriceBasis.FixedPrice, fixedPrice: 100m, marginPercent: 10m));

        rejection.ParamName.ShouldBe("marginPercent");
    }

    [Fact]
    public void SetPrice_MarginLineAlsoCarryingFixedPrice_Throws()
    {
        var line = NewFixedPriceLine();

        var rejection = Should.Throw<ArgumentException>(
            () => line.SetPrice(PriceBasis.MarginOnSalePrice, fixedPrice: 100m, marginPercent: 10m));

        rejection.ParamName.ShouldBe("fixedPrice");
    }

    [Fact]
    public void SetPrice_SwitchingBasisToMargin_ClearsTheFixedPrice()
    {
        // The mutual exclusion is enforced on edit too, so a repriced line can never carry both
        // values and leave the pricing service guessing which one applies.
        var line = NewFixedPriceLine();

        line.SetPrice(PriceBasis.MarginOnCost, marginPercent: 12.345m);

        // Percentages keep two decimal places by explicit banker's rounding at birth
        // (TradeDocumentLine.DiscountPercentMinor's precedent): 12.345 stores as 12.34.
        line.Basis.ShouldBe(PriceBasis.MarginOnCost);
        line.FixedPrice.ShouldBeNull();
        line.FixedPriceMinor.ShouldBeNull();
        line.MarginPercent.ShouldBe(12.34m);
        line.MarginPercentMinor.ShouldBe(123_400L);
    }

    [Fact]
    public void Constructor_NegativeMarginPercent_Throws()
    {
        // Line discounts already reduce prices; a negative margin would double-count the reduction.
        Should.Throw<ArgumentOutOfRangeException>(
            () => new CategoryPriceListLine(
                CompanyId, CategoryId, ItemId, PriceBasis.MarginOnCost, marginPercent: -1m));
    }

    [Fact]
    public void Constructor_FixedPriceMoreThanFourDecimals_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new CategoryPriceListLine(
                CompanyId, CategoryId, ItemId, PriceBasis.FixedPrice, fixedPrice: 10.12345m));
    }

    private static CategoryPriceListLine NewFixedPriceLine() => new(
        CompanyId, CategoryId, ItemId, PriceBasis.FixedPrice, fixedPrice: 100m);
}
