using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Domain.Tests.Inventory;

public class ItemTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid BaseUnitId = Guid.NewGuid();

    [Fact]
    public void Constructor_BlankName_Throws()
    {
        Should.Throw<ArgumentException>(() => new Item(CompanyId, "   ", BaseUnitId));
    }

    [Fact]
    public void Constructor_NormalisesNameForUniqueness()
    {
        // The display name keeps its interior spacing; only the comparison form collapses runs of
        // whitespace and upper-cases, so "cisno pump" and "CISNO  PUMP" collide on the index.
        var item = new Item(CompanyId, "  cisno PUMP 1 hp ", BaseUnitId);

        item.Name.ShouldBe("cisno PUMP 1 hp");
        item.NameNormalised.ShouldBe("CISNO PUMP 1 HP");
    }

    [Fact]
    public void SetUnits_AlternateWithoutFactor_Throws()
    {
        var item = NewItem();

        Should.Throw<InvalidOperationException>(
            () => item.SetUnits(BaseUnitId, Guid.NewGuid(), conversionFactor: null));
    }

    [Fact]
    public void SetUnits_ZeroFactor_Throws()
    {
        var item = NewItem();

        Should.Throw<InvalidOperationException>(
            () => item.SetUnits(BaseUnitId, Guid.NewGuid(), 0m));
    }

    [Fact]
    public void SetUnits_SameUnitTwice_Throws()
    {
        var item = NewItem();

        Should.Throw<InvalidOperationException>(
            () => item.SetUnits(BaseUnitId, BaseUnitId, 10m));
    }

    [Fact]
    public void SetUnits_ClearingAlternate_ClearsFactor()
    {
        var item = NewItem();
        item.SetUnits(BaseUnitId, Guid.NewGuid(), 12m);

        item.SetUnits(BaseUnitId, null, null);

        item.AlternateUnitId.ShouldBeNull();
        item.ConversionFactor.ShouldBeNull();
    }

    [Fact]
    public void SetTracking_SerialAndBatchTogether_Throws()
    {
        var item = NewItem();

        var rejection = Should.Throw<InvalidOperationException>(
            () => item.SetTracking(isSerialTracked: true, isBatchTracked: true));

        rejection.Message.ShouldContain("serial-tracked");
    }

    [Fact]
    public void SetReorderLevel_Negative_Throws()
    {
        var item = NewItem();

        Should.Throw<ArgumentOutOfRangeException>(
            () => item.SetReorderLevel(-0.0001m));
    }

    [Fact]
    public void SetReorderLevel_MoreThanFourDecimals_Throws()
    {
        var item = NewItem();

        Should.Throw<ArgumentOutOfRangeException>(
            () => item.SetReorderLevel(5.12345m));
    }

    private static Item NewItem() => new(CompanyId, "Cisno Pump 1 HP", BaseUnitId);
}
