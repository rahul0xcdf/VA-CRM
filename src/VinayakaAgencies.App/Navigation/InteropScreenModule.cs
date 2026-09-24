using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Interop;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Import &amp; Export group: the Phase 8 screens (phase-8-interoperability.md §2).
/// </summary>
/// <remarks>
/// Ordered after the statutory groups because that is when they are used — the books are kept first
/// and handed to the accountant afterwards. Column mappings sits last of the three: it is the setup
/// step for the middle one, reached once and then left alone.
/// </remarks>
public sealed class InteropScreenModule : IScreenModule
{
    public string Name => "Import & Export";

    public int Order => 29;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Export",
            services => new InteropExportViewModel(
                services.GetRequiredService<IInteropExportService>(),
                services.GetRequiredService<IInteropWorkbookExporter>()),
            typeof(InteropExportView)),

        new ScreenDescriptor(
            "Import",
            services => new InteropImportViewModel(
                services.GetRequiredService<IInteropImportService>(),
                services.GetRequiredService<IInteropSourceFactory>(),
                services.GetRequiredService<IExcelMappingService>()),
            typeof(InteropImportView)),

        new ScreenDescriptor(
            "Column mappings",
            services => new ExcelMappingsViewModel(
                services.GetRequiredService<IExcelMappingService>()),
            typeof(ExcelMappingsView)),
    ];
}
