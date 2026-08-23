using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// The Overview group: the startup diagnostics screen.
/// </summary>
/// <remarks>
/// Screen view models are constructed here rather than resolved from the container: the module
/// names its own dependencies, and nothing has to be registered in the composition root for a
/// screen to appear.
/// </remarks>
public sealed class OverviewScreenModule : IScreenModule
{
    public string Name => "Overview";

    public int Order => 0;

    public IReadOnlyList<ScreenDescriptor> Screens { get; } =
    [
        new ScreenDescriptor(
            "Diagnostics",
            services => new DiagnosticsViewModel(
                services.GetRequiredService<ICompanyService>(),
                services.GetRequiredService<ISqlQueryExecutor>()),
            typeof(DiagnosticsView)),
    ];
}
