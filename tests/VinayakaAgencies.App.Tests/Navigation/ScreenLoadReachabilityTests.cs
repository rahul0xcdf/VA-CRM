using System.IO;
using System.Text.RegularExpressions;
using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.Tests.TestSupport;

namespace VinayakaAgencies.App.Tests.Navigation;

/// <summary>
/// A screen that can load a list must have some way to be asked to.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that was missing when the Ledgers master screen shipped with
/// <c>RefreshCommand</c> bound to nothing at all — no button in its XAML, no entry in its shortcut
/// bars, and no internal caller. Every ledger was in the database and the screen could never show
/// one; worse, its empty state read "No ledger matches this filter", blaming a filter the operator
/// had not set.
/// </para>
/// <para>
/// The screen sweep in <see cref="EveryScreenAgainstARealDatabaseTests"/> did not catch it, and
/// could not have: that test finds the load command by reflection and invokes it directly, which
/// proves the command works while saying nothing about whether the product ever calls it. Testing
/// through a door the application does not have is its own kind of blindness.
/// </para>
/// <para>
/// "Reachable" is deliberately generous — a XAML binding, a shortcut-bar button, or a call from
/// another command on the same view model all count. The bar is only that <i>some</i> path exists.
/// </para>
/// </remarks>
public sealed class ScreenLoadReachabilityTests
{
    [Fact]
    public void EveryScreenWithALoadCommand_HasSomeWayToInvokeIt()
    {
        var root = RepoRoot.Find();
        var unreachable = new List<string>();
        var checkedScreens = 0;

        foreach (var viewModelFile in Directory.EnumerateFiles(
            Path.Combine(root, "src", "VinayakaAgencies.App", "ViewModels"),
            "*ViewModel.cs"))
        {
            var viewModelSource = File.ReadAllText(viewModelFile);
            var viewModelName = Path.GetFileNameWithoutExtension(viewModelFile);

            var loadMethods = LoadMethodsIn(viewModelSource);

            if (loadMethods.Count == 0)
            {
                continue;
            }

            var viewSource = ViewSourceFor(root, viewModelName);

            if (viewSource is null)
            {
                continue;
            }

            checkedScreens++;

            var reachable = loadMethods.Any(method =>
                IsReachable(method, viewModelSource, viewSource));

            if (!reachable)
            {
                unreachable.Add(
                    $"{viewModelName} ({string.Join(", ", loadMethods.Select(m => m + "Async"))})");
            }
        }

        unreachable.ShouldBeEmpty(
            "these screens can load a list but nothing can ask them to — no XAML binding, no "
            + "shortcut-bar button and no internal caller, so the list stays empty forever: "
            + string.Join(" | ", unreachable));

        checkedScreens.ShouldBeGreaterThan(20, "too few screens were examined to prove much");
    }

    [Fact]
    public void LedgersScreen_CanBeAskedToLoadItsList()
    {
        // Named on its own because it is the defect this file exists for: a general guard can be
        // weakened by a future edit to its matching rules without anyone noticing, and this one
        // cannot be satisfied by anything except the Ledgers screen having a working path again.
        var root = RepoRoot.Find();

        var view = File.ReadAllText(
            Path.Combine(root, "src", "VinayakaAgencies.App", "Views", "LedgersView.xaml"));

        view.ShouldContain(
            "RefreshCommand",
            Case.Sensitive,
            "the ledger list is unreachable without it");
    }

    [Fact]
    public void EveryViewFileReferencedByAScreen_ExistsOnDisk()
    {
        // The reachability sweep silently skips a screen whose view file it cannot find, so the
        // naming convention it relies on has to hold.
        var root = RepoRoot.Find();

        var missing = ScreenModuleCatalog.Discover()
            .SelectMany(module => module.Screens)
            .Select(screen => screen.ViewType.Name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !File.Exists(Path.Combine(
                root, "src", "VinayakaAgencies.App", "Views", $"{name}.xaml")))
            .ToList();

        missing.ShouldBeEmpty(
            $"every screen's view must live at Views/<ViewType>.xaml; missing: {string.Join(", ", missing)}");
    }

    // ------------------------------------------------------------------------------- helpers

    /// <summary>Method names (without the <c>Async</c> suffix) that load or refresh a list.</summary>
    private static List<string> LoadMethodsIn(string viewModelSource) =>
        Regex.Matches(viewModelSource, @"private\s+async\s+Task\s+((?:Load|Refresh)\w*)Async\s*\(")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static bool IsReachable(string method, string viewModelSource, string viewSource)
    {
        var command = method + "Command";

        // A button or other binding in the screen's own XAML.
        if (viewSource.Contains(command, StringComparison.Ordinal))
        {
            return true;
        }

        // A shortcut-bar entry, which appears in the view model as the command property.
        if (viewModelSource.Contains(command, StringComparison.Ordinal))
        {
            return true;
        }

        // Another command on the same view model calling it — "reload after saving" is a perfectly
        // good way for a list to fill, so long as something fills it.
        var calls = Regex.Matches(viewModelSource, Regex.Escape(method + "Async") + @"\s*\(");
        var declarations = Regex.Matches(
            viewModelSource, @"private\s+async\s+Task\s+" + Regex.Escape(method) + @"Async\s*\(");

        return calls.Count > declarations.Count;
    }

    private static string? ViewSourceFor(string root, string viewModelName)
    {
        var viewName = viewModelName.EndsWith("ViewModel", StringComparison.Ordinal)
            ? viewModelName[..^"Model".Length]
            : viewModelName;

        var path = Path.Combine(root, "src", "VinayakaAgencies.App", "Views", $"{viewName}.xaml");

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
