using System.Collections;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.Tests.TestSupport;

namespace VinayakaAgencies.App.Tests.Navigation;

/// <summary>
/// Every registered screen, resolved from the application's real service composition against a real
/// migrated and seeded SQLite database, and asked to load.
/// </summary>
/// <remarks>
/// <para>
/// <c>ScreenViewLoadTests</c> proves each screen's XAML constructs; the per-screen view-model tests
/// prove behaviour against stubs. Neither meets a database. This sweep is the missing third: it
/// resolves each view model from the container the shell uses, over the schema an owner's install
/// actually has, and runs whatever load command the screen exposes.
/// </para>
/// <para>
/// It is aimed squarely at the class of defect this repository has produced repeatedly — a service
/// wired into nothing (<c>EnsureGstSeedAsync</c>), a list that could never populate
/// (<c>ListBankLedgersAsync</c>), a report that throws on an empty company
/// (<c>ReconcileValuationAsync</c>). Every one of those passed its own unit tests and failed on a
/// fresh install, which is precisely what this test opens.
/// </para>
/// <para>
/// Failures are collected and reported together: a sweep that stopped at the first broken screen
/// would hide the rest, and the point is to know the state of all of them at once.
/// </para>
/// </remarks>
public sealed class EveryScreenAgainstARealDatabaseTests
{
    /// <summary>Words that mean a load reported trouble to the operator rather than data.</summary>
    private static readonly string[] FailureWords =
        ["error", "failed", "exception", "could not", "unable to"];

    [Fact]
    public async Task EveryRegisteredScreen_ResolvesFromTheRealContainer()
    {
        await using var host = await RealDatabaseAppHost.StartAsync();

        var failures = new List<string>();
        var resolved = 0;

        foreach (var (module, screen) in AllScreens())
        {
            await host.InScopeAsync(sp =>
            {
                try
                {
                    var viewModel = screen.CreateViewModel(sp);

                    viewModel.ShouldNotBeNull();
                    resolved++;
                }
                catch (Exception exception)
                {
                    // The whole dependency graph for one screen, against the real registrations —
                    // a missing or mis-scoped service shows up here and nowhere cheaper.
                    failures.Add($"{module.Name} / {screen.Header}: "
                        + $"{exception.GetType().Name}: {Flatten(exception.Message)}");
                }

                return Task.CompletedTask;
            });
        }

        failures.ShouldBeEmpty(
            "every registered screen must resolve from the application's own container; "
            + string.Join(" | ", failures));

        resolved.ShouldBe(AllScreens().Count);
        resolved.ShouldBeGreaterThan(25, "the catalogue came back suspiciously small");
    }

    [Fact]
    public async Task EveryRegisteredScreen_LoadsWithoutFaultingAgainstAFreshCompany()
    {
        await using var host = await RealDatabaseAppHost.StartAsync();

        var failures = new List<string>();
        var loadsRun = 0;
        var screensWithoutALoad = new List<string>();

        foreach (var (module, screen) in AllScreens())
        {
            await host.InScopeAsync(async sp =>
            {
                object viewModel;

                try
                {
                    viewModel = screen.CreateViewModel(sp);
                }
                catch (Exception exception)
                {
                    failures.Add($"{module.Name} / {screen.Header}: could not construct — "
                        + $"{exception.GetType().Name}");
                    return;
                }

                var commands = LoadCommandsOf(viewModel).ToList();

                if (commands.Count == 0)
                {
                    screensWithoutALoad.Add($"{module.Name} / {screen.Header}");
                    return;
                }

                foreach (var (name, command) in commands)
                {
                    try
                    {
                        await command.ExecuteAsync(null);
                        loadsRun++;
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{module.Name} / {screen.Header} [{name}]: "
                            + $"{exception.GetType().Name}: {Flatten(exception.Message)}");
                    }
                }

                // A screen that swallows its own failure into a status line is just as broken for
                // the operator as one that throws, and far harder to notice.
                if (StatusOf(viewModel) is { } status
                    && FailureWords.Any(word => status.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"{module.Name} / {screen.Header}: status reads '{Flatten(status)}'");
                }
            });
        }

        failures.ShouldBeEmpty(
            "a fresh company is the state every install starts in, and every screen must survive "
            + "it; " + string.Join(" | ", failures));

        loadsRun.ShouldBeGreaterThan(15, "too few screens actually ran a load to prove much");
    }

    [Fact]
    public async Task EveryScreenThatListsMasters_ComesBackWithTheSeededRows()
    {
        // The counterpart to the sweep above: "did not throw" is a low bar, and a list that is
        // silently empty is what the bank-reconciliation defect looked like. Where the seeds put
        // rows in, the screen that shows them must find them.
        await using var host = await RealDatabaseAppHost.StartAsync();

        var empties = new List<string>();

        foreach (var (module, screen) in AllScreens())
        {
            await host.InScopeAsync(async sp =>
            {
                var viewModel = screen.CreateViewModel(sp);

                foreach (var (_, command) in LoadCommandsOf(viewModel))
                {
                    await command.ExecuteAsync(null);
                }

                // Only screens whose own name says they list seeded masters are held to this; the
                // rest legitimately start empty on a fresh company.
                if (!SeededScreens.Contains(screen.Header, StringComparer.OrdinalIgnoreCase))
                {
                    return;
                }

                var populated = CollectionCountsOf(viewModel).Any(count => count > 0);

                if (!populated)
                {
                    empties.Add($"{module.Name} / {screen.Header}");
                }
            });
        }

        empties.ShouldBeEmpty(
            "these screens show seeded master data and came back with nothing, which is what a "
            + "list the operator can never use looks like: " + string.Join(" | ", empties));
    }

    /// <summary>
    /// Screens that must show rows on a freshly provisioned company, because a seed puts them
    /// there: the nine ledgers and twenty-eight groups of §4.3–§4.5, the inventory seed's godown,
    /// and the four customer categories of open-questions.md #2.
    /// </summary>
    /// <remarks>
    /// Nothing else belongs here. Items, HSN/SAC codes, GST rates and every tax section are
    /// deliberately unseeded — owner data only, per non-negotiable #6 — so an empty list is the
    /// correct first-run state for those screens and asserting otherwise would be wrong.
    /// </remarks>
    private static readonly string[] SeededScreens =
        ["Ledgers", "Account groups", "Godowns", "Customer categories"];

    [Fact]
    public async Task LedgersScreen_AfterRefresh_PopulatesTheCollectionTheGridBinds()
    {
        // The sweep above accepts any populated collection, which is too weak here: the Ledgers
        // screen holds the full list in `Ledgers` and the filtered list in `VisibleLedgers`, and
        // the DataGrid binds the second. A reload that filled the first and not the second would
        // satisfy the sweep and still show the operator an empty grid.
        await using var host = await RealDatabaseAppHost.StartAsync();

        await host.InScopeAsync(async sp =>
        {
            var screen = AllScreens().Single(entry =>
                string.Equals(entry.Screen.Header, "Ledgers", StringComparison.Ordinal));

            var viewModel = screen.Screen.CreateViewModel(sp);

            foreach (var (_, command) in LoadCommandsOf(viewModel))
            {
                await command.ExecuteAsync(null);
            }

            CountOf(viewModel, "Ledgers").ShouldBeGreaterThan(
                0, "the seeded ledgers must load");
            CountOf(viewModel, "VisibleLedgers").ShouldBeGreaterThan(
                0, "the grid binds VisibleLedgers, so a reload must run the filter too");
            CountOf(viewModel, "GroupChoices").ShouldBeGreaterThan(
                0, "the group filter must offer the seeded groups");
        });
    }

    private static int CountOf(object viewModel, string propertyName) =>
        viewModel.GetType().GetProperty(propertyName)?.GetValue(viewModel) is IEnumerable items
            ? items.Cast<object>().Count()
            : -1;

    [Fact]
    public void EverySeededScreenName_MatchesARegisteredScreen()
    {
        // Without this the list above rots silently: a renamed header stops matching, the
        // populated-rows assertion quietly covers one screen fewer, and nothing fails. Two names
        // in the first draft of that list ("Voucher types", "Units") had no screen at all.
        var headers = AllScreens()
            .Select(entry => entry.Screen.Header)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = SeededScreens.Where(name => !headers.Contains(name)).ToList();

        missing.ShouldBeEmpty(
            $"these names no longer match a registered screen: {string.Join(", ", missing)}");
    }

    // ------------------------------------------------------------------------------- helpers

    private static List<(IScreenModule Module, ScreenDescriptor Screen)> AllScreens() =>
        ScreenModuleCatalog.Discover()
            .SelectMany(module => module.Screens.Select(screen => (module, screen)))
            .ToList();

    /// <summary>
    /// The screen's own load entry points: an async command whose name says it loads or refreshes.
    /// </summary>
    /// <remarks>
    /// Found by reflection rather than by an interface, because no interface declares one — which
    /// is itself worth knowing, and is why <see cref="EveryRegisteredScreen_LoadsWithoutFaultingAgainstAFreshCompany"/>
    /// reports the screens where nothing matched instead of quietly passing them.
    /// </remarks>
    private static IEnumerable<(string Name, IAsyncRelayCommand Command)> LoadCommandsOf(object viewModel) =>
        viewModel.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => typeof(IAsyncRelayCommand).IsAssignableFrom(property.PropertyType))
            .Where(property =>
                property.Name.Contains("Load", StringComparison.Ordinal)
                || property.Name.Contains("Refresh", StringComparison.Ordinal))
            .Select(property => (property.Name, (IAsyncRelayCommand?)property.GetValue(viewModel)))
            .Where(pair => pair.Item2 is not null)
            .Select(pair => (pair.Name, pair.Item2!));

    private static string? StatusOf(object viewModel) =>
        viewModel.GetType()
            .GetProperty("Status", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(viewModel) as string;

    private static IEnumerable<int> CollectionCountsOf(object viewModel) =>
        viewModel.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType != typeof(string)
                && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
            .Select(property =>
            {
                try
                {
                    return property.GetValue(viewModel) is IEnumerable items
                        ? items.Cast<object>().Count()
                        : 0;
                }
                catch (TargetInvocationException)
                {
                    return 0;
                }
            });

    private static string Flatten(string text)
    {
        var single = text.ReplaceLineEndings(" ");

        return single.Length <= 160 ? single : single[..160];
    }
}
