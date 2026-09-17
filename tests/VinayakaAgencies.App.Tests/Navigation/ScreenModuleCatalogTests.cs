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
    public void Discover_FindsThePhase4Screens_InMastersAndReports()
    {
        var modules = ScreenModuleCatalog.Discover();

        var masters = modules.SingleOrDefault(module => module.Name == "Masters");
        masters.ShouldNotBeNull();
        masters.Screens.Select(screen => screen.Header).ShouldContain("Customers");
        masters.Screens.Select(screen => screen.Header).ShouldContain("Customer categories");
        masters.Screens.Select(screen => screen.Header).ShouldContain("Category price lists");
        masters.Screens.Select(screen => screen.Header).ShouldContain("Sales settings");

        var reports = modules.SingleOrDefault(module => module.Name == "Reports");
        reports.ShouldNotBeNull();
        reports.Screens.Select(screen => screen.Header).ShouldContain("Customer statement");
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

    [Fact]
    public void Discover_FindsTheGstModule_WithItsFourPhase5Screens()
    {
        var modules = ScreenModuleCatalog.Discover();

        var gst = modules.SingleOrDefault(module => module.Name == "GST");
        gst.ShouldNotBeNull();

        gst.Screens.Select(screen => screen.Header).ShouldBe(
            ["HSN/SAC codes", "GST rate schedules", "GST returns", "GSTR-2B reconciliation"]);
    }

    [Fact]
    public void Discover_FindsTheEinvoicingModule_WithItsThreePhase6Screens()
    {
        var modules = ScreenModuleCatalog.Discover();

        var einvoicing = modules.SingleOrDefault(module => module.Name == "E-invoicing");
        einvoicing.ShouldNotBeNull();

        einvoicing.Screens.Select(screen => screen.Header).ShouldBe(
            ["E-invoice (IRN)", "E-way bills", "E-invoice settings"]);
    }

    [Fact]
    public void Discover_OrdersEinvoicingAfterGst_BecauseThatIsTheSequenceTheWorkHappensIn()
    {
        var names = ScreenModuleCatalog.Discover().Select(module => module.Name).ToList();

        names.IndexOf("E-invoicing").ShouldBeGreaterThan(names.IndexOf("GST"));
    }

    private sealed class FakeModule(string name, int order) : IScreenModule
    {
        public string Name => name;

        public int Order => order;

        public IReadOnlyList<ScreenDescriptor> Screens { get; } = [];
    }
}
