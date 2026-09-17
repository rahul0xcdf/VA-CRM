using VinayakaAgencies.App.Presentation.Keyboard;

namespace VinayakaAgencies.App.Tests.Presentation;

/// <summary>
/// The application's key map (ADR-0006).
/// </summary>
/// <remarks>
/// The map is the contract an operator memorises. These tests exist so that a key gaining a second
/// meaning breaks the build rather than surprising somebody mid-voucher.
/// </remarks>
public sealed class AppShortcutsTests
{
    [Fact]
    public void All_ContainsNoDuplicateGesture()
    {
        var duplicates = AppShortcuts.All
            .GroupBy(shortcut => (shortcut.Key, shortcut.Modifiers))
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(" / ", group.Select(shortcut => shortcut.Display)))
            .ToList();

        duplicates.ShouldBeEmpty();
    }

    [Fact]
    public void All_DisplayLabelsAreUnique_SoNoTwoBarsCanShowTheSameKeyForDifferentActions()
    {
        var labels = AppShortcuts.All.Select(shortcut => shortcut.Display).ToList();

        labels.Distinct(StringComparer.Ordinal).Count().ShouldBe(labels.Count);
    }

    [Fact]
    public void Accept_IsControlA_MatchingTally()
    {
        AppShortcuts.Accept.Display.ShouldBe("Ctrl+A");
        AppShortcuts.Accept.Matches(System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.Control)
            .ShouldBeTrue();
    }

    [Fact]
    public void Cancel_IsAltX_NotControlQ()
    {
        // Ctrl+Q exits the screen in Tally; Alt+X cancels the thing being edited. The pre-ADR-0006
        // shell had Ctrl+Q doing both, which is why this is pinned.
        AppShortcuts.Cancel.Display.ShouldBe("Alt+X");
        AppShortcuts.Exit.Display.ShouldBe("Ctrl+Q");
        AppShortcuts.Cancel.ShouldNotBe(AppShortcuts.Exit);
    }

    [Theory]
    [InlineData("F4")]
    [InlineData("F5")]
    [InlineData("F6")]
    [InlineData("F7")]
    [InlineData("F8")]
    [InlineData("F9")]
    public void VoucherTypeKeys_AreReservedOnTheBareFunctionKeys(string gesture)
    {
        AppShortcuts.All.ShouldContain(shortcut => shortcut.Display == gesture);
    }

    [Fact]
    public void GoToAndSwitchTo_AreDistinctGestures()
    {
        AppShortcuts.GoTo.Display.ShouldBe("Alt+G");
        AppShortcuts.SwitchTo.Display.ShouldBe("Ctrl+G");
    }

    [Fact]
    public void Matches_RequiresTheExactModifierSet()
    {
        // Ctrl+Shift+A must not fire Accept.
        AppShortcuts.Accept.Matches(
                System.Windows.Input.Key.A,
                System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)
            .ShouldBeFalse();
    }
}
