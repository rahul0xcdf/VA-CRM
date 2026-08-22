using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Audit group: the read-only edit log with its chain check, and the numbering-gap report
/// (spec §2, §11, §8.7).
/// </summary>
public sealed class AuditScreenModule : IScreenModule
{
    public string Name => "Audit";

    public int Order => 40;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Edit log",
            services => new EditLogViewModel(
                services.GetRequiredService<IAuditLogReader>()),
            typeof(EditLogView)),

        new ScreenDescriptor(
            "Number gaps",
            services => new NumberGapsViewModel(
                services.GetRequiredService<IAccountingReportService>(),
                services.GetRequiredService<IVoucherPostingService>(),
                services.GetRequiredService<IFinancialYearProvider>()),
            typeof(NumberGapsView)),
    ];
}
