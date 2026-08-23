using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Application.Trade;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Sales &amp; Purchase group: the Phase 3 pipeline screens
/// (phase-3-sales-purchase-pipeline.md §1).
/// </summary>
public sealed class SalesPurchaseScreenModule : IScreenModule
{
    public string Name => "Sales & Purchase";

    public int Order => 15;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Documents",
            services => new TradeDocumentsViewModel(
                services.GetRequiredService<ITradeDocumentService>(),
                services.GetRequiredService<IChartOfAccountsService>(),
                services.GetRequiredService<IInventoryMasterService>(),
                services.GetRequiredService<IStockReportService>(),
                services.GetRequiredService<IPrintTemplateService>(),
                services.GetRequiredService<ICompanyService>(),
                services.GetRequiredService<ICompanyContext>(),
                services.GetRequiredService<ICustomerService>(),
                services.GetRequiredService<IPricingService>(),
                services.GetRequiredService<ISalesSettingsService>(),
                services.GetRequiredService<ICreditLimitService>()),
            typeof(TradeDocumentsView)),

        new ScreenDescriptor(
            "Order outstanding",
            services => new OutstandingOrdersViewModel(
                services.GetRequiredService<ITradeDocumentService>()),
            typeof(OutstandingOrdersView)),

        new ScreenDescriptor(
            "Print templates",
            services => new PrintTemplatesViewModel(
                services.GetRequiredService<IPrintTemplateService>()),
            typeof(PrintTemplatesView)),
    ];
}
