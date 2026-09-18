using System.Text;
using System.Windows;
using WpfApplication = System.Windows.Application;
using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.Presentation.Controls;

namespace VinayakaAgencies.App.Tests.Navigation;

/// <summary>
/// Every registered screen's view must actually load (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// This is the test the suite was missing. Thirty-one screens were registered and nothing checked
/// that any of them could be constructed: a view referencing a <c>StaticResource</c> that no longer
/// exists compiles perfectly and throws the moment an operator opens it. Nothing else in the suite
/// would catch that, because view models are tested in isolation and never meet their XAML.
/// </para>
/// <para>
/// The views are loaded with the application's real merged dictionaries, in the same order
/// <c>App.xaml</c> merges them, so a style that only resolves because of merge order is not
/// accidentally excused here.
/// </para>
/// <para>
/// WPF demands an STA thread and refuses to create a second <see cref="WpfApplication"/> in a process,
/// so the whole sweep runs once on one dedicated thread and reports every failure together — a test
/// that stopped at the first broken view would hide the other thirty.
/// </para>
/// </remarks>
public sealed class ScreenViewLoadTests
{
    [Fact]
    public void EveryRegisteredScreenView_LoadsWithTheApplicationResources()
    {
        var (attempted, failures, _) = LoadAllViewsOnStaThread();

        failures.ShouldBeEmpty(
            "every screen registered in a module must be constructible; these threw when loaded");

        // Without this the test would pass just as happily if the STA thread died before loading
        // anything, or if the catalogue came back empty — a sweep of nothing finds nothing wrong.
        attempted.ShouldBe(
            ScreenModuleCatalog.Discover().Sum(module => module.Screens.Count));
        attempted.ShouldBeGreaterThan(25);
    }

    [Fact]
    public void EveryRegisteredScreen_DeclaresAViewThatIsAUserControl()
    {
        // The shell sets DataContext on a FrameworkElement; a descriptor pointing at anything else
        // would silently show an unbound screen rather than fail.
        var offenders = ScreenModuleCatalog.Discover()
            .SelectMany(module => module.Screens)
            .Where(screen => !typeof(FrameworkElement).IsAssignableFrom(screen.ViewType))
            .Select(screen => $"{screen.Header} -> {screen.ViewType.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// The lookup field's default style must be reachable from the application's own dictionaries
    /// (ADR-0007).
    /// </summary>
    /// <remarks>
    /// The view sweep above cannot catch this. A <c>LookupBox</c> whose style is missing constructs
    /// perfectly happily — it simply renders as an empty rectangle with no text box and no list, so
    /// every voucher line would silently lose its ledger field. Asserting the style resolves is the
    /// only cheap way to know the control is actually wired to a template.
    /// </remarks>
    [Fact]
    public void LookupBox_ResolvesItsDefaultStyleFromTheApplicationDictionaries()
    {
        var (_, failures, lookupStyleFound) = LoadAllViewsOnStaThread();

        failures.ShouldBeEmpty();
        lookupStyleFound.ShouldBeTrue(
            "Entry.xaml must carry an implicit Style for LookupBox, or every lookup renders blank");
    }

    private static (int Attempted, List<string> Failures, bool LookupStyleFound) LoadAllViewsOnStaThread()
    {
        var failures = new List<string>();
        var attempted = 0;
        var lookupStyleFound = false;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationWithThemes();

                lookupStyleFound =
                    WpfApplication.Current?.TryFindResource(typeof(LookupBox)) is Style style
                    && style.TargetType == typeof(LookupBox);

                foreach (var screen in ScreenModuleCatalog.Discover().SelectMany(module => module.Screens))
                {
                    attempted++;

                    try
                    {
                        Activator.CreateInstance(screen.ViewType);
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{screen.Header} ({screen.ViewType.Name}): {Describe(exception)}");
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add($"could not prepare the WPF host: {Describe(exception)}");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromMinutes(2));

        return (attempted, failures, lookupStyleFound);
    }

    private static void EnsureApplicationWithThemes()
    {
        var application = WpfApplication.Current ?? new WpfApplication();

        // Same dictionaries, same order as App.xaml.
        foreach (var theme in new[] { "Colors", "Typography", "Controls", "Shell", "Pages", "Entry" })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/VinayakaAgencies.App;component/Presentation/Themes/{theme}.xaml",
                    UriKind.Absolute),
            });
        }
    }

    /// <summary>
    /// XAML load failures wrap the real cause, and the outer message is always the same unhelpful
    /// sentence. Walking the chain is what makes a failure here actionable.
    /// </summary>
    private static string Describe(Exception exception)
    {
        var message = new StringBuilder();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            message.Append(current.GetType().Name).Append(": ").Append(current.Message);

            if (current.InnerException is not null)
            {
                message.Append(" -> ");
            }
        }

        return message.ToString();
    }
}
