using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Payroll;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Payroll group: the Phase 7b screens — employees with their dated salary structures, the
/// statutory setup, and the monthly run (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// The group appears whether or not any statute is switched on, because salary vouchers are worth
/// posting either way — statutory output is what the flags gate, not payroll itself (C7.7).
/// </remarks>
public sealed class PayrollScreenModule : IScreenModule
{
    public string Name => "Payroll";

    public int Order => 28;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Employees",
            services => new EmployeesViewModel(
                services.GetRequiredService<IPayrollMasterService>()),
            typeof(EmployeesView)),

        new ScreenDescriptor(
            "Payroll setup",
            services => new PayrollSettingsViewModel(
                services.GetRequiredService<IPayrollMasterService>(),
                services.GetRequiredService<IChartOfAccountsService>()),
            typeof(PayrollSettingsView)),

        new ScreenDescriptor(
            "Payroll run",
            services => new PayrollRunViewModel(
                services.GetRequiredService<IPayrollRunService>(),
                services.GetRequiredService<IVoucherPostingService>()),
            typeof(PayrollRunView)),
    ];
}
