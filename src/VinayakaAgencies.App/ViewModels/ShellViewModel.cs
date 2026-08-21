using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using VinayakaAgencies.App.Branding;
using VinayakaAgencies.App.Navigation;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Backs the shell window: the navigation pane and the content host.
/// </summary>
/// <remarks>
/// Phase 0's shell existed to prove the composition root; that proof now lives on the Diagnostics
/// screen (<see cref="DiagnosticsViewModel"/>, checkpoint C0.7) and the shell only navigates.
/// Screens come from <see cref="IScreenModule"/> implementations discovered by reflection, so a
/// new screen is one file and no registration edit. The service provider is the shell's scope —
/// screen view models are built through it, so they share the shell's unit-of-work lifetime.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private string _title = "Vinayaka Agencies";

    [ObservableProperty]
    private string _tagline = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<NavigationItem> _screens = [];

    [ObservableProperty]
    private NavigationItem? _selectedScreen;

    /// <summary>The content-host payload: the selected screen's view, bound to its view model.</summary>
    [ObservableProperty]
    private object? _activeView;

    public ShellViewModel(
        IServiceProvider services,
        IOptions<BrandingOptions> branding,
        IReadOnlyList<IScreenModule>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(branding);

        _services = services;

        Title = branding.Value.DisplayName;
        Tagline = branding.Value.Tagline;

        // Caller-supplied module lists get the same ordering the catalog applies, so the pane
        // never depends on how modules happen to arrive.
        Screens = (modules ?? ScreenModuleCatalog.Discover())
            .OrderBy(module => module.Order)
            .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(module => module.Screens.Select(screen => new NavigationItem(module.Name, screen)))
            .ToList();

        if (Screens.Count > 0)
        {
            SelectedScreen = Screens[0];
        }
    }

    partial void OnSelectedScreenChanged(NavigationItem? value)
    {
        if (value is null)
        {
            ActiveView = null;
            return;
        }

        var viewModel = value.Screen.CreateViewModel(_services);
        var view = Activator.CreateInstance(value.Screen.ViewType);

        // A real view is a FrameworkElement; test doubles for the view type are not, and simply
        // skip the data-context wiring.
        if (view is FrameworkElement element)
        {
            element.DataContext = viewModel;
        }

        ActiveView = view;
    }
}
