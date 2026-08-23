using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Reports group: the derived statements, the gap report (spec §2) and the Phase 4
/// customer statement (phase-4-customer-pricing.md §2).
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

        new ScreenDescriptor(
            "Customer statement",
            services => new CustomerStatementViewModel(
                services.GetRequiredService<ICustomerStatementService>(),
                services.GetRequiredService<ICustomerService>(),
                services.GetRequiredService<ICompanyService>(),
                services.GetRequiredService<ICompanyContext>()),
            typeof(CustomerStatementView)),
    ];
}
