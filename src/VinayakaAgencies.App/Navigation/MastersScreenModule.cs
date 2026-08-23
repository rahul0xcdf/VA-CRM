using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Inventory;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Masters group: chart-of-accounts screens (spec §2) and the Phase 4 customer/pricing
/// masters (phase-4-customer-pricing.md §1).
/// </summary>
public sealed class MastersScreenModule : IScreenModule
{
    public string Name => "Masters";

    public int Order => 10;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Account groups",
            services => new AccountGroupsViewModel(
                services.GetRequiredService<IChartOfAccountsService>()),
            typeof(AccountGroupsView)),

        new ScreenDescriptor(
            "Ledgers",
            services => new LedgersViewModel(
                services.GetRequiredService<IChartOfAccountsService>()),
            typeof(LedgersView)),

        new ScreenDescriptor(
            "Customer categories",
            services => new CustomerCategoriesViewModel(
                services.GetRequiredService<ICustomerCategoryService>()),
            typeof(CustomerCategoriesView)),

        new ScreenDescriptor(
            "Customers",
            services => new CustomerMasterViewModel(
                services.GetRequiredService<ICustomerService>(),
                services.GetRequiredService<ICustomerCategoryService>(),
                services.GetRequiredService<IPriceListService>(),
                services.GetRequiredService<IInventoryMasterService>()),
            typeof(CustomerMasterView)),

        new ScreenDescriptor(
            "Category price lists",
            services => new CategoryPriceListViewModel(
                services.GetRequiredService<ICustomerCategoryService>(),
                services.GetRequiredService<IPriceListService>(),
                services.GetRequiredService<IInventoryMasterService>()),
            typeof(CategoryPriceListView)),

        new ScreenDescriptor(
            "Sales settings",
            services => new SalesSettingsViewModel(
                services.GetRequiredService<ISalesSettingsService>()),
            typeof(SalesSettingsView)),
    ];
}
