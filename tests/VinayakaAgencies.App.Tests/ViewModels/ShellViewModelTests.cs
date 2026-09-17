using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VinayakaAgencies.App.Branding;
using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.App.ViewModels;

namespace VinayakaAgencies.App.Tests.ViewModels;

/// <summary>
/// The shell's navigation model (ADR-0006).
/// </summary>
public sealed class ShellViewModelTests
{
    [Fact]
    public void Constructor_WithModules_FlattensScreensInModuleOrder()
    {
        var viewModel = Build(new SecondModule(), new FirstModule());

        viewModel.Screens.Count.ShouldBe(2);
        viewModel.Screens[0].Group.ShouldBe("First");
        viewModel.Screens[1].Group.ShouldBe("Second");
    }

    [Fact]
    public void Constructor_OpensOnTheGateway_NotOnAScreen()
    {
        // ADR-0006 replaced the always-visible sidebar with a menu that is entered and escaped from.
        // Landing on an arbitrary first screen would defeat that: the operator would start somewhere
        // they did not choose, and Esc would have nowhere sensible to go.
        var viewModel = Build(new FirstModule());

        viewModel.IsGatewayVisible.ShouldBeTrue();
        viewModel.SelectedScreen.ShouldBeNull();
        viewModel.ActiveView.ShouldBeNull();
        viewModel.CurrentMode.ShouldBe("Gateway");
    }

    [Fact]
    public void Gateway_GroupsScreensByModule()
    {
        var viewModel = Build(new FirstModule(), new SecondModule());

        viewModel.GatewaySections.Count.ShouldBe(2);
        viewModel.GatewaySections[0].Title.ShouldBe("FIRST");
        viewModel.GatewaySections[1].Title.ShouldBe("SECOND");
        viewModel.GatewaySections[0].Items[0].Header.ShouldBe("First screen");
    }

    [Fact]
    public void Gateway_AllocatesAUniqueAcceleratorAcrossEverySection()
    {
        // Accelerators are unique menu-wide, not section-wide: the same key must not mean two things
        // depending on where the eye happens to be.
        var viewModel = Build(new FirstModule(), new SecondModule());

        var keys = viewModel.GatewayItems
            .Where(item => item.Hotkey.HasKey)
            .Select(item => item.Hotkey.Key)
            .ToList();

        keys.Distinct().Count().ShouldBe(keys.Count);
    }

    [Fact]
    public void OpenScreen_BuildsTheViewModelThroughTheDescriptorFactory()
    {
        var viewModel = Build(new FirstModule());

        viewModel.OpenScreen(viewModel.Screens[0]);

        // The stub view type is not a FrameworkElement, so the shell skips data-context wiring —
        // which is what lets this test run without a WPF Application.
        viewModel.ActiveView.ShouldBeOfType<StubViewModel>();
        viewModel.IsGatewayVisible.ShouldBeFalse();
        viewModel.CurrentMode.ShouldBe("First screen");
    }

    [Fact]
    public void TryOpenByHotkey_OpensTheEntryClaimingThatLetter()
    {
        var viewModel = Build(new FirstModule());

        var expected = viewModel.GatewayItems[0];

        viewModel.TryOpenByHotkey(expected.Hotkey.Key).ShouldBeTrue();
        viewModel.SelectedScreen.ShouldBe(expected.Target);
    }

    [Fact]
    public void TryOpenByHotkey_WithAnUnclaimedLetter_ReportsFalseAndNavigatesNowhere()
    {
        // The window relies on this to let the keystroke fall through rather than swallowing it.
        var viewModel = Build(new FirstModule());

        viewModel.TryOpenByHotkey('Z').ShouldBeFalse();
        viewModel.IsGatewayVisible.ShouldBeTrue();
    }

    [Fact]
    public void NavigateBack_FromAScreen_ReturnsToTheGateway()
    {
        var viewModel = Build(new FirstModule());

        viewModel.OpenScreen(viewModel.Screens[0]);
        viewModel.NavigateBackCommand.Execute(null);

        viewModel.IsGatewayVisible.ShouldBeTrue();
        viewModel.ActiveView.ShouldBeNull();
    }

    [Fact]
    public void NavigateBack_WalksTheStackOneScreenAtATime()
    {
        var viewModel = Build(new FirstModule(), new SecondModule());

        viewModel.OpenScreen(viewModel.Screens[0]);
        viewModel.OpenScreen(viewModel.Screens[1]);

        viewModel.NavigateBackCommand.Execute(null);
        viewModel.SelectedScreen.ShouldBe(viewModel.Screens[0]);

        viewModel.NavigateBackCommand.Execute(null);
        viewModel.IsGatewayVisible.ShouldBeTrue();
    }

    [Fact]
    public void NavigateBack_AtTheGateway_DoesNothing()
    {
        // Esc must never exit the application — an operator hits it reflexively over a half-typed
        // voucher. Leaving is an explicit act.
        var viewModel = Build(new FirstModule());

        Should.NotThrow(() => viewModel.NavigateBackCommand.Execute(null));

        viewModel.IsGatewayVisible.ShouldBeTrue();
    }

    [Fact]
    public void ShowGateway_ClearsTheBackStack()
    {
        var viewModel = Build(new FirstModule(), new SecondModule());

        viewModel.OpenScreen(viewModel.Screens[0]);
        viewModel.OpenScreen(viewModel.Screens[1]);
        viewModel.ShowGatewayCommand.Execute(null);

        viewModel.IsGatewayVisible.ShouldBeTrue();

        // Back from the Gateway goes nowhere, rather than re-entering the trail that was cleared.
        viewModel.NavigateBackCommand.Execute(null);
        viewModel.IsGatewayVisible.ShouldBeTrue();
    }

    [Fact]
    public void ActionBar_OnAScreenWithShortcuts_OffersAcceptCancelAndExit()
    {
        var viewModel = Build(new ShortcutModule());

        viewModel.OpenScreen(viewModel.Screens[0]);

        var gestures = viewModel.ActionBar.Select(button => button.Gesture).ToList();

        gestures.ShouldContain(AppShortcuts.Accept.Display);
        gestures.ShouldContain(AppShortcuts.Cancel.Display);
        gestures.ShouldContain(AppShortcuts.Exit.Display);
    }

    [Fact]
    public void ActionBar_AtTheGateway_OffersNoExit()
    {
        // There is nothing to exit to from the Gateway, so advertising the key would be a lie.
        var viewModel = Build(new ShortcutModule());

        viewModel.ActionBar.ShouldNotContain(button => button.Gesture == AppShortcuts.Exit.Display);
    }

    [Fact]
    public void ButtonBar_CarriesTheScreensOwnContextualKeys()
    {
        var viewModel = Build(new ShortcutModule());

        viewModel.OpenScreen(viewModel.Screens[0]);

        viewModel.ButtonBar.ShouldContain(button => button.Label == "Sales");
    }

    [Fact]
    public void TopMenu_OffersGoToSwitchToAndGateway()
    {
        var viewModel = Build(new FirstModule());

        viewModel.TopMenu.Select(button => button.Gesture).ShouldBe(
            [AppShortcuts.GoTo.Display, AppShortcuts.SwitchTo.Display, AppShortcuts.Back.Display]);
    }

    [Fact]
    public void GoToCommand_RaisesTheRequestRatherThanOpeningAWindow()
    {
        // Keeping window creation out of the view model is what lets these tests run headless.
        var viewModel = Build(new FirstModule());
        var raised = false;

        viewModel.GoToRequested += (_, _) => raised = true;
        viewModel.GoToCommand.Execute(null);

        raised.ShouldBeTrue();
    }

    [Fact]
    public void Title_ComesFromBranding()
    {
        var branding = new BrandingOptions { DisplayName = "Test Dealer", Tagline = "Test tagline" };

        var viewModel = new ShellViewModel(
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(branding),
            Array.Empty<IScreenModule>());

        viewModel.Title.ShouldBe("Test Dealer");
        viewModel.Tagline.ShouldBe("Test tagline");
    }

    private static ShellViewModel Build(params IScreenModule[] modules) =>
        new(new ServiceCollection().BuildServiceProvider(),
            Options.Create(new BrandingOptions()),
            modules);

    private sealed class FirstModule : IScreenModule
    {
        public string Name => "First";

        public int Order => 1;

        public IReadOnlyList<ScreenDescriptor> Screens { get; } =
        [
            new ScreenDescriptor("First screen", _ => new StubViewModel(), typeof(StubViewModel)),
        ];
    }

    private sealed class SecondModule : IScreenModule
    {
        public string Name => "Second";

        public int Order => 2;

        public IReadOnlyList<ScreenDescriptor> Screens { get; } =
        [
            new ScreenDescriptor("Second screen", _ => new StubViewModel(), typeof(StubViewModel)),
        ];
    }

    private sealed class ShortcutModule : IScreenModule
    {
        public string Name => "Shortcuts";

        public int Order => 3;

        public IReadOnlyList<ScreenDescriptor> Screens { get; } =
        [
            new ScreenDescriptor("Shortcut screen", _ => new ShortcutViewModel(), typeof(ShortcutViewModel)),
        ];
    }

    private sealed class StubViewModel : ObservableObject;

    private sealed class ShortcutViewModel : ObservableObject, IScreenShortcuts
    {
        public System.Windows.Input.ICommand? AcceptCommand { get; } = new StubCommand();

        public System.Windows.Input.ICommand? CancelCommand { get; } = new StubCommand();

        public IReadOnlyList<ShortcutButton> ButtonBar { get; } =
            [new ShortcutButton(AppShortcuts.Sales, "Sales", new StubCommand())];
    }

    private sealed class StubCommand : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
