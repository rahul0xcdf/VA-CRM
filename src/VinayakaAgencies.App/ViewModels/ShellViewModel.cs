using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using VinayakaAgencies.App.Branding;
using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The shell: Gateway menu, navigation stack, and the two shortcut bars (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// The shell opens on the <b>Gateway</b> rather than on a screen, and that is the central change from
/// the sidebar it replaces. A permanently visible list of thirty screens asks the operator to read
/// before every action; a menu that is entered, acted on and escaped from lets them learn a path once
/// and then run it blind — which is how an experienced Tally user actually works.
/// </para>
/// <para>
/// <c>Esc</c> therefore walks back toward the Gateway and stops there. It never exits the application:
/// an accounting operator hits Esc reflexively, and an Esc that could close the program over a
/// half-typed voucher would be a trap. Leaving is an explicit act.
/// </para>
/// <para>
/// Both bars are recomposed on navigation from the shell's own globals plus whatever the screen
/// declares through <see cref="IScreenShortcuts"/>. Screens never draw chrome themselves, so a key can
/// never be advertised in one place and bound in another.
/// </para>
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    public const string DefaultHints = "Alt+G Go To · Esc Back · F1 Help";

    /// <summary>Gateway section headings, which match the module names but read as menu headers.</summary>
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
    private IReadOnlyList<GatewaySection> _gatewaySections = [];

    [ObservableProperty]
    private NavigationItem? _selectedScreen;

    [ObservableProperty]
    private object? _activeView;

    [ObservableProperty]
    private bool _isGatewayVisible = true;

    [ObservableProperty]
    private string _companyLegalName = string.Empty;

    [ObservableProperty]
    private string _companyGstin = "Not registered";

    [ObservableProperty]
    private string _activePeriod = string.Empty;

    [ObservableProperty]
    private string _currentMode = "Gateway";

    [ObservableProperty]
    private string _shortcutHints = DefaultHints;

    [ObservableProperty]
    private IReadOnlyList<ShortcutButton> _buttonBar = [];

    [ObservableProperty]
    private IReadOnlyList<ShortcutButton> _actionBar = [];

    [ObservableProperty]
    private IReadOnlyList<ShortcutButton> _topMenu = [];

    /// <summary>
    /// Raised when the operator asks for the Go To palette.
    /// </summary>
    /// <remarks>
    /// An event rather than the view model opening a window itself. Creating a <c>Window</c> here would
    /// put WPF lifetime management inside the one class the App tests exercise without a WPF
    /// <c>Application</c> — the shell would stop being testable to save one indirection.
    /// </remarks>
    public event EventHandler? GoToRequested;

    /// <summary>Raised when the operator asks to switch to a related screen.</summary>
    public event EventHandler? SwitchToRequested;

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

        var orderedModules = (modules ?? ScreenModuleCatalog.Discover())
            .OrderBy(module => module.Order)
            .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Screens = Flatten(orderedModules);
        GatewaySections = BuildGateway(orderedModules, Screens);

        TopMenu =
        [
            new ShortcutButton(AppShortcuts.GoTo, "Go To", GoToCommand),
            new ShortcutButton(AppShortcuts.SwitchTo, "Switch To", SwitchToCommand),
            new ShortcutButton(AppShortcuts.Back, "Gateway", ShowGatewayCommand),
        ];

        ComposeBars(screen: null);
    }

    [RelayCommand]
    private void GoTo() => GoToRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SwitchTo() => SwitchToRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The Gateway's own accelerators, for the window to route single-key presses to.</summary>
    public IReadOnlyList<GatewayItem> GatewayItems =>
        [.. GatewaySections.SelectMany(section => section.Items)];

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

    /// <summary>
    /// Opens the Gateway entry whose accelerator is <paramref name="letter"/>. Returns false when no
    /// entry claims that key, so the window can let the keystroke fall through.
    /// </summary>
    public bool TryOpenByHotkey(char letter)
    {
        var upper = char.ToUpperInvariant(letter);

        var match = GatewayItems.FirstOrDefault(item => item.Hotkey.HasKey && item.Hotkey.Key == upper);

        if (match is null)
        {
            return false;
        }

        OpenScreen(match.Target);

        return true;
    }

    /// <summary>Opens a screen, pushing the current one onto the back stack.</summary>
    public void OpenScreen(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        SelectedScreen = item;
    }

    [RelayCommand]
    private void OpenGatewayItem(GatewayItem? item)
    {
        if (item is not null)
        {
            OpenScreen(item.Target);
        }
    }

    /// <summary>
    /// Back one level: to the previous screen, or to the Gateway. At the Gateway this does nothing —
    /// see the class remarks on why Esc must never exit.
    /// </summary>
    [RelayCommand]
    private void NavigateBack()
    {
        if (IsGatewayVisible)
        {
            return;
        }

        if (_history.Count == 0)
        {
            ShowGateway();
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

    /// <summary>Returns to the Gateway and clears the trail, as Tally's own Gateway entry does.</summary>
    [RelayCommand]
    private void ShowGateway()
    {
        _history.Clear();
        _suppressHistory = true;

        try
        {
            SelectedScreen = null;
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

        if (value is null)
        {
            ActiveView = null;
            IsGatewayVisible = true;
            CurrentMode = "Gateway";
            ShortcutHints = DefaultHints;
            ComposeBars(screen: null);
            return;
        }

        CurrentMode = value.Screen.Header;

        var viewModel = value.Screen.CreateViewModel(_services);
        var view = Activator.CreateInstance(value.Screen.ViewType);

        if (view is FrameworkElement element)
        {
            element.DataContext = viewModel;
        }

        var shortcuts = viewModel as IScreenShortcuts;

        ShortcutHints = shortcuts is { StatusBarHints: { Length: > 0 } hints } ? hints : DefaultHints;

        ActiveView = view;
        IsGatewayVisible = false;

        ComposeBars(shortcuts);
    }

    /// <summary>
    /// Rebuilds both bars for the screen now showing.
    /// </summary>
    /// <remarks>
    /// Global entries come first and in a fixed order, so their position never moves between screens —
    /// a bar whose items shuffle is a bar nobody learns. Only keys that actually do something are
    /// rendered: F2, F3, F11 and F12 are reserved in <see cref="AppShortcuts"/> for later phases and
    /// are deliberately absent until they work, because a button that does nothing teaches an operator
    /// that the bar lies.
    /// </remarks>
    private void ComposeBars(IScreenShortcuts? screen)
    {
        var buttons = new List<ShortcutButton>
        {
            new(AppShortcuts.Help, "Help", ShowHelpCommand),
            new(AppShortcuts.GoTo, "Go To", null),
        };

        if (screen is not null)
        {
            buttons.AddRange(screen.ButtonBar);
        }

        ButtonBar = buttons;

        var actions = new List<ShortcutButton>();

        if (screen?.AcceptCommand is { } accept)
        {
            actions.Add(new ShortcutButton(AppShortcuts.Accept, "Accept", accept));
        }

        if (screen?.CancelCommand is { } cancel)
        {
            actions.Add(new ShortcutButton(AppShortcuts.Cancel, "Cancel", cancel));
        }

        if (screen is not null)
        {
            actions.AddRange(screen.ActionBar);
        }

        if (!IsGatewayVisible)
        {
            actions.Add(new ShortcutButton(AppShortcuts.Exit, "Exit", NavigateBackCommand));
        }

        ActionBar = actions;
    }

    /// <summary>The accept command of the screen now showing, if it offers one.</summary>
    public System.Windows.Input.ICommand? ActiveAcceptCommand =>
        ActionBar.FirstOrDefault(button => button.Shortcut == AppShortcuts.Accept)?.Command;

    /// <summary>The cancel command of the screen now showing, if it offers one.</summary>
    public System.Windows.Input.ICommand? ActiveCancelCommand =>
        ActionBar.FirstOrDefault(button => button.Shortcut == AppShortcuts.Cancel)?.Command;

    [RelayCommand]
    private void ShowHelp() =>
        ShortcutHints = "Alt+G Go To · Ctrl+G Switch To · Esc Back · Ctrl+A Accept · Alt+X Cancel · Ctrl+Q Exit";

    private static List<NavigationItem> Flatten(IReadOnlyList<IScreenModule> modules)
    {
        var items = new List<NavigationItem>();

        for (var moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
        {
            var module = modules[moduleIndex];

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

        return items;
    }

    /// <summary>
    /// Builds the Gateway, allocating accelerators across the whole menu at once.
    /// </summary>
    private static List<GatewaySection> BuildGateway(
        IReadOnlyList<IScreenModule> modules,
        IReadOnlyList<NavigationItem> screens)
    {
        // One allocation pass over every label, so no two entries anywhere on the Gateway share a key.
        var hotkeys = MenuHotkeyAllocator
            .Allocate(screens.Select(screen => screen.Screen.Header))
            .ToList();

        var sections = new List<GatewaySection>(modules.Count);
        var cursor = 0;

        foreach (var module in modules)
        {
            var items = new List<GatewayItem>(module.Screens.Count);

            for (var i = 0; i < module.Screens.Count; i++, cursor++)
            {
                items.Add(new GatewayItem(hotkeys[cursor], screens[cursor]));
            }

            if (items.Count > 0)
            {
                sections.Add(new GatewaySection(module.Name.ToUpperInvariant(), items));
            }
        }

        return sections;
    }

    private static string BuildPeriodLabel(int financialYearStartMonth, DateTime today)
    {
        var fyStart = today.Month >= financialYearStartMonth ? today.Year : today.Year - 1;

        return $"FY {fyStart}-{(fyStart + 1) % 100:00} · {today:MMM yyyy}";
    }
}
