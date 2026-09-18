using VinayakaAgencies.App.Presentation.Controls;

namespace VinayakaAgencies.App.Tests.Presentation;

/// <summary>
/// What a lookup field offers for what has been typed (ADR-0007).
/// </summary>
/// <remarks>
/// This is the rule an operator feels on every voucher line, and it is the reason the logic was kept
/// out of <c>LookupBox</c>: the control needs a window, and these assertions do not.
/// </remarks>
public sealed class LookupFilterTests
{
    private static readonly string[] Ledgers =
    [
        "Cash",
        "Petty Cash",
        "Cash in Hand",
        "Capital Account",
        "Sundry Debtors",
    ];

    [Fact]
    public void Rank_WithNoText_OffersEverythingInTheCallersOrder()
    {
        // An empty field shows the whole list, exactly as pressing the key in Tally does.
        var ranked = LookupFilter.Rank(Ledgers, string.Empty, name => name);

        ranked.ShouldBe(Ledgers);
    }

    [Fact]
    public void Rank_WithBlankText_IsTreatedAsNoText()
    {
        LookupFilter.Rank(Ledgers, "   ", name => name).Count.ShouldBe(Ledgers.Length);
    }

    [Fact]
    public void Rank_PutsPrefixMatchesBeforeInteriorOnes()
    {
        // "Petty Cash" contains "ca" but does not start with it, so blind typing must not land on
        // it — this is the whole reason a ComboBox was not good enough.
        var ranked = LookupFilter.Rank(Ledgers, "ca", name => name);

        ranked.ShouldBe(["Cash", "Cash in Hand", "Capital Account", "Petty Cash"]);
    }

    [Fact]
    public void Rank_MatchesAnywhereInTheName_NotOnlyTheFirstLetter()
    {
        LookupFilter.Rank(Ledgers, "debtors", name => name).ShouldBe(["Sundry Debtors"]);
    }

    [Fact]
    public void Rank_IgnoresCase()
    {
        LookupFilter.Rank(Ledgers, "CASH", name => name)
            .ShouldBe(["Cash", "Cash in Hand", "Petty Cash"]);
    }

    [Fact]
    public void Rank_PreservesTheCallersOrderWithinEachBand()
    {
        // The caller has already sorted; re-sorting here would silently override a deliberate order.
        string[] source = ["Zebra Motors", "Alpha Motors"];

        LookupFilter.Rank(source, "motors", name => name).ShouldBe(source);
    }

    [Fact]
    public void Rank_SkipsItemsWithNoDisplayText()
    {
        string?[] source = ["Cash", null, string.Empty];

        LookupFilter.Rank(source, "cash", name => name).ShouldBe(["Cash"]);
    }

    [Fact]
    public void Rank_WithNullSource_IsEmptyRatherThanThrowing()
    {
        // Called on every keystroke, including ones that arrive before the list has loaded.
        LookupFilter.Rank<string>(null, "cash", name => name).ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_PrefersAnExactMatchOverALongerCandidate()
    {
        // Typing "Cash" and tabbing out must select Cash, not leave the field ambiguous because
        // "Cash in Hand" also starts with it.
        LookupFilter.Resolve(Ledgers, "Cash", name => name).ShouldBe("Cash");
    }

    [Fact]
    public void Resolve_TakesTheOnlyCandidateWhenExactlyOneMatches()
    {
        LookupFilter.Resolve(Ledgers, "sundry", name => name).ShouldBe("Sundry Debtors");
    }

    [Fact]
    public void Resolve_RefusesToChooseBetweenSeveralCandidates()
    {
        // "Ca" names four ledgers. Picking one for the operator would put a ledger on a voucher that
        // they never named.
        LookupFilter.Resolve(Ledgers, "Ca", name => name).ShouldBeNull();
    }

    [Fact]
    public void Resolve_WithNoText_SelectsNothing()
    {
        LookupFilter.Resolve(Ledgers, string.Empty, name => name).ShouldBeNull();
        LookupFilter.Resolve(Ledgers, null, name => name).ShouldBeNull();
    }

    [Fact]
    public void Resolve_WithNoMatch_SelectsNothing()
    {
        LookupFilter.Resolve(Ledgers, "Payroll", name => name).ShouldBeNull();
    }
}
