using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using VinayakaAgencies.App.Branding;
using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Backs the shell window: the top header, the home menu, the content host and the status bar.
/// </summary>
/// <remarks>
/// Phase 0's shell existed to prove the composition root; that proof now lives on the Diagnostics
/// screen (<see cref="DiagnosticsViewModel"/>, checkpoint C0.7) and the shell only navigates.
/// Screens come from <see cref="IScreenModule"/> implementations discovered by reflection, so a
/// new screen is one file and no registration edit. The service provider is the shell's scope —
/// screen view models are built through it, so they share the shell's unit-of-work lifetime.
/// ADR-0005 adds the Tally-familiar chrome: numbered module gateway, an Escape back-stack and a
/// status bar whose shortcut hints follow the focused screen's <see cref="IScreenShortcuts"/>.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    /// <summary>Status-bar hints shown when the focused screen declares none of its own.</summary>
    public const string DefaultHints = "Alt+G Go To · Esc Back";

    private readonly IServiceProvider _services;
    private readonly ICompanyService? _companies;
    private readonly ICompanyContext? _companyContext;

    private readonly Stack<NavigationItem> _history = new();
    private NavigationItem? _activeScreen;
    private bool _suppressHistory;

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

    [ObservableProperty]
    private string _companyLegalName = string.Empty;

    [ObservableProperty]
    private string _companyGstin = "Not registered";

    [ObservableProperty]
    private string _activePeriod = string.Empty;

    [ObservableProperty]
    private string _currentMode = string.Empty;

    [ObservableProperty]
    private string _shortcutHints = DefaultHints;

    public ShellViewModel(
        IServiceProvider services,
        IOptions<BrandingOptions> branding,
        IReadOnlyList<IScreenModule>? modules = null,
        ICompanyService? companies = null,
        ICompanyContext? companyContext = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(branding);

        _services = services;
        _companies = companies;
        _companyContext = companyContext;

        Title = branding.Value.DisplayName;
        Tagline = branding.Value.Tagline;

        // Caller-supplied module lists get the same ordering the catalog applies, so the pane
        // never depends on how modules happen to arrive. Modules number 1..n in display order
        // and screens letter A.. inside each module (ADR-0005).
        var orderedModules = (modules ?? ScreenModuleCatalog.Discover())
            .OrderBy(module => module.Order)
            .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<NavigationItem>();
        for (var moduleIndex = 0; moduleIndex < orderedModules.Count; moduleIndex++)
        {
            var module = orderedModules[moduleIndex];
            for (var screenIndex = 0; screenIndex < module.Screens.Count; screenIndex++)
            {
                items.Add(new NavigationItem(
                    module.Name,
                    module.Screens[screenIndex],
                    moduleIndex + 1,
                    screenIndex < 26
                        ? ((char)('A' + screenIndex)).ToString(CultureInfo.InvariantCulture)
                        : (screenIndex + 1).ToString(CultureInfo.InvariantCulture)));
            }
        }

        Screens = items;

        if (Screens.Count > 0)
        {
            SelectedScreen = Screens[0];
        }
    }

    /// <summary>
    /// Loads the header facts — legal name, GSTIN and active financial-year period — from the
    /// company master. Called once by the host before the shell opens; failures degrade to the
    /// neutral defaults rather than blocking startup.
    /// </summary>
    public async Task LoadCompanyHeaderAsync(CancellationToken cancellationToken = default)
    {
        if (_companies is null || _companyContext is null || !_companyContext.HasCompany)
        {
            return;
        }

        try
        {
            var company = await _companies.GetAsync(_companyContext.CompanyId, cancellationToken)
                .ConfigureAwait(true);

            if (company is null)
            {
                return;
            }

            CompanyLegalName = string.IsNullOrWhiteSpace(company.LegalName) ? company.Name : company.LegalName;
            CompanyGstin = string.IsNullOrWhiteSpace(company.Gstin) ? "Not registered" : company.Gstin;
            ActivePeriod = BuildPeriodLabel(company.FinancialYearStartMonth, DateTime.Today);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            CompanyGstin = "Not registered";
        }
    }

    /// <summary>Digit-key jump: selects the first screen of module number 1..9.</summary>
    public void SelectModuleByNumber(int number)
    {
        var target = Screens.FirstOrDefault(screen => screen.ModuleNumber == number);
        if (target is not null && !ReferenceEquals(target, SelectedScreen))
        {
            SelectedScreen = target;
        }
    }

    [RelayCommand]
    private void NavigateBack()
    {
        if (_history.Count == 0)
        {
            return;
        }

        var previous = _history.Pop();
        _suppressHistory = true;

        try
        {
            SelectedScreen = previous;
        }
        finally
        {
            _suppressHistory = false;
        }
    }

    partial void OnSelectedScreenChanged(NavigationItem? value)
    {
        if (!_suppressHistory && _activeScreen is not null && !ReferenceEquals(_activeScreen, value))
        {
            _history.Push(_activeScreen);
        }

        _activeScreen = value;
        CurrentMode = value?.Screen.Header ?? string.Empty;

        if (value is null)
        {
            ActiveView = null;
            ShortcutHints = DefaultHints;
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

        ShortcutHints = viewModel is IScreenShortcuts { StatusBarHints: { Length: > 0 } hints }
            ? hints
            : DefaultHints;

        ActiveView = view;
    }

    private static string BuildPeriodLabel(int financialYearStartMonth, DateTime today)
    {
        var fyStart = today.Month >= financialYearStartMonth ? today.Year : today.Year - 1;
        return $"FY {fyStart}-{(fyStart + 1) % 100:00} · {today:MMM yyyy}";
    }
}
