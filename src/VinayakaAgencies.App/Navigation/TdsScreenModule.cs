using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Banking;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Application.Tds;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The TDS &amp; Banking group: the Phase 7a screens — the section and rate masters, the
/// applicability settings, the deduction register, and payments with their instruments
/// (phase-7-tds-payroll-banking.md §4a D5).
/// </summary>
public sealed class TdsScreenModule : IScreenModule
{
    public string Name => "TDS & Banking";

    public int Order => 27;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "TDS/TCS sections",
            services => new TaxSectionsViewModel(
                services.GetRequiredService<ITaxSectionService>()),
            typeof(TaxSectionsView)),

        new ScreenDescriptor(
            "TDS/TCS setup",
            services => new TdsSettingsViewModel(
                services.GetRequiredService<ITdsSettingsService>(),
                services.GetRequiredService<ITaxSectionService>(),
                services.GetRequiredService<IChartOfAccountsService>()),
            typeof(TdsSettingsView)),

        new ScreenDescriptor(
            "TDS/TCS register",
            services => new TaxDeductionRegisterViewModel(
                services.GetRequiredService<ITaxDeductionService>(),
                services.GetRequiredService<ITaxSectionService>()),
            typeof(TaxDeductionRegisterView)),

        new ScreenDescriptor(
            "Payments & receipts",
            services => new PaymentsViewModel(
                services.GetRequiredService<IBankingService>(),
                services.GetRequiredService<IVoucherPostingService>(),
                services.GetRequiredService<IChartOfAccountsService>(),
                services.GetRequiredService<IPrintTemplateService>()),
            typeof(PaymentsView)),
    ];
}
