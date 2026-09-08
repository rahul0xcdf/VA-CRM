using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Gst;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The GST group: the Phase 5 statutory screens — HSN/SAC master, rate schedules, period returns
/// and GSTR-2B reconciliation (phase-5-gst-returns.md §1).
/// </summary>
public sealed class GstScreenModule : IScreenModule
{
    public string Name => "GST";

    public int Order => 25;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "HSN/SAC codes",
            services => new HsnSacMasterViewModel(
                services.GetRequiredService<IHsnSacService>(),
                services.GetRequiredService<IGstRateService>()),
            typeof(HsnSacMasterView)),

        new ScreenDescriptor(
            "GST rate schedules",
            services => new GstRateSchedulesViewModel(
                services.GetRequiredService<IHsnSacService>(),
                services.GetRequiredService<IGstRateService>()),
            typeof(GstRateSchedulesView)),

        new ScreenDescriptor(
            "GST returns",
            services => new GstReturnsViewModel(
                services.GetRequiredService<IGstReturnService>(),
                services.GetRequiredService<ICompanyService>(),
                services.GetRequiredService<ICompanyContext>()),
            typeof(GstReturnsView)),

        new ScreenDescriptor(
            "GSTR-2B reconciliation",
            services => new Gstr2BReconciliationViewModel(
                services.GetRequiredService<IGstReturnService>()),
            typeof(Gstr2BReconciliationView)),
    ];
}
