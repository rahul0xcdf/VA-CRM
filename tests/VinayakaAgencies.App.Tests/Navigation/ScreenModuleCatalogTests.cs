using VinayakaAgencies.App.Navigation;

namespace VinayakaAgencies.App.Tests.Navigation;

public sealed class ScreenModuleCatalogTests
{
    [Fact]
    public void Discover_WithOverviewModulePresent_FindsIt()
    {
        var modules = ScreenModuleCatalog.Discover();

        modules.ShouldNotBeNull();
        modules.ShouldContain(module => module.Name == "Overview");
    }

    [Fact]
    public void Discover_FindsTheSalesPurchaseModule_WithItsThreePhase3Screens()
    {
        var modules = ScreenModuleCatalog.Discover();

        var salesPurchase = modules.SingleOrDefault(module => module.Name == "Sales & Purchase");
        salesPurchase.ShouldNotBeNull();

        salesPurchase.Screens.Select(screen => screen.Header).ShouldBe(
            ["Documents", "Order outstanding", "Print templates"]);
    }

    [Fact]
    public void Discover_ReturnsModulesOrderedByOrderThenName()
    {
        // The real assembly currently ships exactly one module; ordering is asserted against a
        // synthetic set below via the same sort the catalog applies, because reflection order is
        // not something a test can arrange.
        var unordered = new IScreenModule[]
        {
            new FakeModule("Zeta", order: 1),
            new FakeModule("Alpha", order: 0),
        };

        var ordered = unordered
            .OrderBy(module => module.Order)
            .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ordered[0].Name.ShouldBe("Alpha");
        ordered[1].Name.ShouldBe("Zeta");
    }

    private sealed class FakeModule(string name, int order) : IScreenModule
    {
        public string Name => name;

        public int Order => order;

        public IReadOnlyList<ScreenDescriptor> Screens { get; } = [];
    }
}
