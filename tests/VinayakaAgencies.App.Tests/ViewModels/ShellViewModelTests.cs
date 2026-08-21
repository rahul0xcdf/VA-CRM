using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using CommunityToolkit.Mvvm.ComponentModel;
using VinayakaAgencies.App.Branding;
using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.ViewModels;

namespace VinayakaAgencies.App.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    [Fact]
    public void Constructor_WithModules_FlattensScreensInModuleOrder()
    {
        var viewModel = new ShellViewModel(
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(new BrandingOptions()),
            new IScreenModule[] { new SecondModule(), new FirstModule() });

        viewModel.Screens.Count.ShouldBe(2);
        viewModel.Screens[0].Group.ShouldBe("First");
        viewModel.Screens[1].Group.ShouldBe("Second");
    }

    [Fact]
    public void Constructor_SelectsTheFirstScreen()
    {
        var viewModel = new ShellViewModel(
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(new BrandingOptions()),
            new IScreenModule[] { new FirstModule() });

        viewModel.SelectedScreen.ShouldNotBeNull();
        viewModel.SelectedScreen.Screen.Header.ShouldBe("First screen");
    }

    [Fact]
    public void SelectScreen_BuildsViewModelThroughTheDescriptorFactory()
    {
        var viewModel = new ShellViewModel(
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(new BrandingOptions()),
            new IScreenModule[] { new FirstModule() });

        // The stub view type is not a FrameworkElement, so the shell skips data-context wiring —
        // which is exactly what lets this test run without a WPF Application.
        viewModel.ActiveView.ShouldBeOfType<StubViewModel>();
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

    private sealed class FirstModule : IScreenModule
    {
        public string Name => "First";

        public int Order => 1;

        public IReadOnlyList<ScreenDescriptor> Screens { get; } =
        [
            new ScreenDescriptor(
                "First screen",
                _ => new StubViewModel(),
                typeof(StubViewModel)),
        ];
    }

    private sealed class SecondModule : IScreenModule
    {
        public string Name => "Second";

        public int Order => 2;

        public IReadOnlyList<ScreenDescriptor> Screens { get; } =
        [
            new ScreenDescriptor(
                "Second screen",
                _ => new StubViewModel(),
                typeof(StubViewModel)),
        ];
    }

    private sealed class StubViewModel : ObservableObject;
}
