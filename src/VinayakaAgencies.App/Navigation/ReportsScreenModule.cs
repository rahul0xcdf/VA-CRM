using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Reports group: the derived statements and the gap report (spec §2). On-screen grids only —
/// export and printing belong to later phases.
/// </summary>
public sealed class ReportsScreenModule : IScreenModule
{
    public string Name => "Reports";

    public int Order => 30;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Trial balance",
            services => new TrialBalanceViewModel(
                services.GetRequiredService<IAccountingReportService>()),
            typeof(TrialBalanceView)),

        new ScreenDescriptor(
            "Profit & loss",
            services => new ProfitAndLossViewModel(
                services.GetRequiredService<IAccountingReportService>()),
            typeof(ProfitAndLossView)),

        new ScreenDescriptor(
            "Balance sheet",
            services => new BalanceSheetViewModel(
                services.GetRequiredService<IAccountingReportService>()),
            typeof(BalanceSheetView)),

        new ScreenDescriptor(
            "Bank reconciliation",
            services => new BankReconciliationViewModel(
                services.GetRequiredService<IBankReconciliationService>(),
                services.GetRequiredService<IChartOfAccountsService>()),
            typeof(BankReconciliationView)),
    ];
}
