using System.Windows.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.App.Tests.Presentation;

/// <summary>
/// Which function key means which voucher (ADR-0007).
/// </summary>
/// <remarks>
/// These are TallyPrime's published assignments. They are pinned by display string rather than by
/// key-plus-modifier so that a change to either half of a gesture fails here, and because the display
/// string is what the operator reads off the button bar.
/// </remarks>
public sealed class VoucherTypeKeyMapTests
{
    [Theory]
    [InlineData(VoucherKind.Contra, "F4")]
    [InlineData(VoucherKind.Payment, "F5")]
    [InlineData(VoucherKind.Receipt, "F6")]
    [InlineData(VoucherKind.Journal, "F7")]
    [InlineData(VoucherKind.Sales, "F8")]
    [InlineData(VoucherKind.Purchase, "F9")]
    [InlineData(VoucherKind.DebitNote, "Alt+F5")]
    [InlineData(VoucherKind.CreditNote, "Alt+F6")]
    public void For_VoucherKind_MatchesTallysPublishedKey(VoucherKind kind, string gesture)
    {
        VoucherTypeKeyMap.For(kind)?.Display.ShouldBe(gesture);
    }

    [Theory]
    [InlineData(TradeDocumentKind.TaxInvoice, "F8")]
    [InlineData(TradeDocumentKind.PurchaseInvoice, "F9")]
    [InlineData(TradeDocumentKind.SalesOrder, "Ctrl+F8")]
    [InlineData(TradeDocumentKind.PurchaseOrder, "Ctrl+F9")]
    [InlineData(TradeDocumentKind.DeliveryChallan, "Alt+F8")]
    [InlineData(TradeDocumentKind.GoodsReceipt, "Alt+F9")]
    public void For_DocumentKind_MatchesTallysPublishedKey(TradeDocumentKind kind, string gesture)
    {
        VoucherTypeKeyMap.For(kind)?.Display.ShouldBe(gesture);
    }

    [Fact]
    public void For_Quotation_HasNoKey_BecauseTallyPublishesNone()
    {
        // Inventing one would teach a key that does not transfer and would spend a gesture a later
        // phase may need for something Tally does define. F10 covers it instead.
        VoucherTypeKeyMap.For(TradeDocumentKind.Quotation).ShouldBeNull();
        VoucherTypeKeyMap.UnkeyedDocumentKinds.ShouldBe([TradeDocumentKind.Quotation]);
    }

    [Theory]
    [InlineData(StockVoucherPurpose.Receipt, "Alt+F9")]
    [InlineData(StockVoucherPurpose.Issue, "Alt+F8")]
    [InlineData(StockVoucherPurpose.Transfer, "Alt+F7")]
    [InlineData(StockVoucherPurpose.OpeningBalance, "F10")]
    public void For_StockPurpose_KeepsGoodsInOnAltF9AndGoodsOutOnAltF8(
        StockVoucherPurpose purpose,
        string gesture)
    {
        // Same keys as the delivery challan and goods receipt on the Sales & Purchase screen, and
        // deliberately so: Alt+F8 is "goods going out" on both, Alt+F9 is "goods coming in" on both.
        // ADR-0006 forbids a gesture meaning two *different* things, not one idea appearing twice.
        VoucherTypeKeyMap.For(purpose)?.Display.ShouldBe(gesture);
    }

    [Fact]
    public void EveryMappedKey_IsDeclaredInTheApplicationKeyMap()
    {
        // A key map entry that AppShortcuts does not know about would escape the duplicate-gesture
        // guard, which is the only thing stopping one gesture acquiring two meanings.
        var mapped = Enum.GetValues<VoucherKind>().Select(VoucherTypeKeyMap.For)
            .Concat(Enum.GetValues<TradeDocumentKind>().Select(VoucherTypeKeyMap.For))
            .Concat(Enum.GetValues<StockVoucherPurpose>().Select(VoucherTypeKeyMap.For))
            .Where(shortcut => shortcut is not null)
            .Distinct();

        foreach (var shortcut in mapped)
        {
            AppShortcuts.All.ShouldContain(shortcut!);
        }
    }

    [Fact]
    public void KeyedVoucherKinds_AndKeyedDocumentKinds_AllHaveKeys()
    {
        // The bars are built by walking these lists and dereferencing For(...) — a member without a
        // key would throw when the screen opened.
        VoucherTypeKeyMap.KeyedVoucherKinds.ShouldAllBe(kind => VoucherTypeKeyMap.For(kind) != null);
        VoucherTypeKeyMap.KeyedDocumentKinds.ShouldAllBe(kind => VoucherTypeKeyMap.For(kind) != null);
        VoucherTypeKeyMap.KeyedStockPurposes.ShouldAllBe(purpose => VoucherTypeKeyMap.For(purpose) != null);
    }

    [Fact]
    public void CreateOnTheFly_IsAltC_WhichTheLookupFieldHardCodes()
    {
        // LookupBox spells Alt+C out rather than referencing AppShortcuts, so that a widget does not
        // depend on application key policy. This is the test that stops the two drifting apart.
        AppShortcuts.CreateOnTheFly.Display.ShouldBe("Alt+C");
        AppShortcuts.CreateOnTheFly.Key.ShouldBe(Key.C);
        AppShortcuts.CreateOnTheFly.Modifiers.ShouldBe(ModifierKeys.Alt);
    }

    [Fact]
    public void LabelFor_ReadsAsAKeyLabelRatherThanAnEnumName()
    {
        VoucherTypeKeyMap.LabelFor(TradeDocumentKind.TaxInvoice).ShouldBe("Sales");
        VoucherTypeKeyMap.LabelFor(TradeDocumentKind.GoodsReceipt).ShouldBe("Receipt note");
        VoucherTypeKeyMap.LabelFor(StockVoucherPurpose.OpeningBalance).ShouldBe("Opening");
    }
}
