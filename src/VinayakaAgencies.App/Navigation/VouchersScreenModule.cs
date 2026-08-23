using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Vouchers group: entry/alteration and the day book (spec §2).
/// </summary>
public sealed class VouchersScreenModule : IScreenModule
{
    public string Name => "Vouchers";

    public int Order => 20;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Voucher entry",
            services => new VoucherEntryViewModel(
                services.GetRequiredService<IVoucherPostingService>(),
                services.GetRequiredService<IChartOfAccountsService>(),
                services.GetRequiredService<IAuditLogReader>()),
            typeof(VoucherEntryView)),

        new ScreenDescriptor(
            "Day book",
            services => new DayBookViewModel(
                services.GetRequiredService<IAccountingReportService>()),
            typeof(DayBookView)),
    ];
}
