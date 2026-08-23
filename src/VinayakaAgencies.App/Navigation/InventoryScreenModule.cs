using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Inventory;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Inventory group: the Phase 2 stock screens (phase-2-inventory.md §1).
/// </summary>
public sealed class InventoryScreenModule : IScreenModule
{
    public string Name => "Inventory";

    public int Order => 20;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Items",
            services => new ItemsViewModel(
                services.GetRequiredService<IInventoryMasterService>()),
            typeof(ItemsView)),

        new ScreenDescriptor(
            "Stock groups",
            services => new StockGroupsViewModel(
                services.GetRequiredService<IInventoryMasterService>()),
            typeof(StockGroupsView)),

        new ScreenDescriptor(
            "Godowns",
            services => new GodownsViewModel(
                services.GetRequiredService<IInventoryMasterService>()),
            typeof(GodownsView)),

        new ScreenDescriptor(
            "Stock vouchers",
            services => new StockVoucherViewModel(
                services.GetRequiredService<IInventoryMasterService>(),
                services.GetRequiredService<IStockVoucherService>(),
                services.GetRequiredService<IChartOfAccountsService>(),
                services.GetRequiredService<IStockReportService>()),
            typeof(StockVoucherView)),

        new ScreenDescriptor(
            "Stock reports",
            services => new StockReportsViewModel(
                services.GetRequiredService<IStockReportService>()),
            typeof(StockReportsView)),
    ];
}
