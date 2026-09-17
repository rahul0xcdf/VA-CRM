using VinayakaAgencies.App.Presentation.Keyboard;

namespace VinayakaAgencies.App.Tests.Presentation;

/// <summary>
/// Menu accelerator allocation (ADR-0006).
/// </summary>
public sealed class MenuHotkeyAllocatorTests
{
    [Fact]
    public void Allocate_TakesTheFirstFreeLetter_WalkingEachLabelLeftToRight()
    {
        // The worked example from the ADR: C is taken by Create, so Chart of Accounts falls through to
        // the H — which is why Tally shows accelerators mid-word.
        var hotkeys = MenuHotkeyAllocator.Allocate(["Create", "Alter", "Chart of Accounts"]);

        hotkeys[0].Key.ShouldBe('C');
        hotkeys[1].Key.ShouldBe('A');
        hotkeys[2].Key.ShouldBe('H');
    }

    [Fact]
    public void Allocate_SplitsTheLabelAroundTheAccelerator()
    {
        var hotkeys = MenuHotkeyAllocator.Allocate(["Create", "Chart of Accounts"]);

        var chart = hotkeys[1];

        chart.Before.ShouldBe("C");
        chart.Letter.ShouldBe("h");
        chart.After.ShouldBe("art of Accounts");

        // The three runs must reassemble into the original, or the menu would render altered text.
        (chart.Before + chart.Letter + chart.After).ShouldBe("Chart of Accounts");
    }

    [Fact]
    public void Allocate_NeverRepeatsALetterAcrossTheMenu()
    {
        var labels = new[]
        {
            "Create", "Chart of Accounts", "Customers", "Customer categories", "Category price lists",
        };

        var keys = MenuHotkeyAllocator.Allocate(labels)
            .Where(hotkey => hotkey.HasKey)
            .Select(hotkey => hotkey.Key)
            .ToList();

        keys.Distinct().Count().ShouldBe(keys.Count);
    }

    [Fact]
    public void Allocate_IsCaseInsensitive_SoAAndLowerADoNotBothGetTaken()
    {
        var hotkeys = MenuHotkeyAllocator.Allocate(["Alter", "account groups"]);

        hotkeys[0].Key.ShouldBe('A');
        hotkeys[1].Key.ShouldNotBe('A');
    }

    [Fact]
    public void Allocate_SkipsPunctuationAndSpaces()
    {
        var hotkeys = MenuHotkeyAllocator.Allocate(["& — Profit"]);

        hotkeys[0].Key.ShouldBe('P');
    }

    [Fact]
    public void Allocate_UsesDigitsWhenLettersRunOut()
    {
        var hotkeys = MenuHotkeyAllocator.Allocate(["GST", "G2"]);

        hotkeys[0].Key.ShouldBe('G');
        hotkeys[1].Key.ShouldBe('2');
    }

    [Fact]
    public void Allocate_WhenEveryLetterIsTaken_LeavesTheItemWithoutAKey()
    {
        // Better than stealing a key from an item that already advertises it. The entry stays
        // reachable by arrow keys and by Go To.
        var hotkeys = MenuHotkeyAllocator.Allocate(["ab", "ba", "ab"]);

        hotkeys[2].HasKey.ShouldBeFalse();
        hotkeys[2].Key.ShouldBe('\0');
        hotkeys[2].Before.ShouldBe("ab");
        hotkeys[2].Letter.ShouldBeEmpty();
    }

    [Fact]
    public void Allocate_HonoursAClaimEvenWhenTheLetterIsNotFirst()
    {
        var claims = new Dictionary<string, char> { ["Day Book"] = 'K' };

        var hotkeys = MenuHotkeyAllocator.Allocate(["Day Book", "Documents"], claims);

        hotkeys[0].Key.ShouldBe('K');

        // The claim is taken before anything is computed, so D remains free for the next item.
        hotkeys[1].Key.ShouldBe('D');
    }

    [Fact]
    public void Allocate_ClaimsWin_SoInsertingAScreenCannotReletterAMemorisedKey()
    {
        var claims = new Dictionary<string, char> { ["Vouchers"] = 'V' };

        // "Voucher entry" would otherwise take V simply by appearing first.
        var hotkeys = MenuHotkeyAllocator.Allocate(["Voucher entry", "Vouchers"], claims);

        hotkeys[1].Key.ShouldBe('V');
        hotkeys[0].Key.ShouldNotBe('V');
    }

    [Fact]
    public void Allocate_WithAClaimForALetterTheLabelLacks_FallsBackToComputing()
    {
        // A typo in a claim degrades rather than silently stealing another screen's key.
        var claims = new Dictionary<string, char> { ["Ledgers"] = 'Z' };

        var hotkeys = MenuHotkeyAllocator.Allocate(["Ledgers"], claims);

        hotkeys[0].Key.ShouldBe('L');
    }

    [Fact]
    public void Allocate_WithNoLabels_ReturnsNothing()
    {
        MenuHotkeyAllocator.Allocate([]).ShouldBeEmpty();
    }
}
