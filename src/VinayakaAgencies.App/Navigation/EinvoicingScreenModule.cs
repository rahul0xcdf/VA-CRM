using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Application.Trade;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The E-invoicing group: the Phase 6 screens — IRN registration, e-way bills, and the portal
/// settings that gate both (phase-6-einvoice-eway.md §1).
/// </summary>
/// <remarks>
/// Ordered just after GST, because that is the sequence the work happens in: an invoice is taxed,
/// then registered, then moved. Settings sits last in the group rather than first — it is configured
/// once and then left alone, so it does not deserve the top slot people read past every day.
/// </remarks>
public sealed class EinvoicingScreenModule : IScreenModule
{
    public string Name => "E-invoicing";

    public int Order => 26;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "E-invoice (IRN)",
            services => new EinvoiceRegistrationsViewModel(
                services.GetRequiredService<IEinvoiceService>(),
                services.GetRequiredService<ITradeDocumentService>()),
            typeof(EinvoiceRegistrationsView)),

        new ScreenDescriptor(
            "E-way bills",
            services => new EwayBillsViewModel(
                services.GetRequiredService<IEwayBillService>(),
                services.GetRequiredService<ITradeDocumentService>()),
            typeof(EwayBillsView)),

        new ScreenDescriptor(
            "E-invoice settings",
            services => new EinvoiceSettingsViewModel(
                services.GetRequiredService<IEinvoiceSettingsService>()),
            typeof(EinvoiceSettingsView)),
    ];
}
