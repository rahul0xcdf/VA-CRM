using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.App.Presentation.Keyboard;

/// <summary>
/// Which function key selects which kind of voucher or trade document (ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppShortcuts"/> declares the gestures; this says what they mean. The two are kept apart
/// so the duplicate-gesture guard stays a statement about keys alone, and so this mapping — the part
/// an operator's fingers actually learn — can be asserted against TallyPrime's published assignments
/// without dragging a window into a test.
/// </para>
/// <para>
/// Not every kind gets a key, and that is deliberate. TallyPrime publishes no shortcut for a
/// quotation, so inventing one here would teach a key that does not transfer and would spend a
/// gesture a later phase may need for something Tally does define. Kinds without a key are reached
/// through <see cref="AppShortcuts.OtherVouchers"/> (F10), which is precisely what F10 is for.
/// </para>
/// </remarks>
public static class VoucherTypeKeyMap
{
    private static readonly Dictionary<VoucherKind, AppShortcut> VoucherKeys = new()
    {
        [VoucherKind.Contra] = AppShortcuts.Contra,
        [VoucherKind.Payment] = AppShortcuts.Payment,
        [VoucherKind.Receipt] = AppShortcuts.Receipt,
        [VoucherKind.Journal] = AppShortcuts.Journal,
        [VoucherKind.Sales] = AppShortcuts.Sales,
        [VoucherKind.Purchase] = AppShortcuts.Purchase,
        [VoucherKind.DebitNote] = AppShortcuts.DebitNote,
        [VoucherKind.CreditNote] = AppShortcuts.CreditNote,
    };

    private static readonly Dictionary<TradeDocumentKind, AppShortcut> DocumentKeys = new()
    {
        [TradeDocumentKind.TaxInvoice] = AppShortcuts.Sales,
        [TradeDocumentKind.PurchaseInvoice] = AppShortcuts.Purchase,
        [TradeDocumentKind.SalesOrder] = AppShortcuts.SalesOrder,
        [TradeDocumentKind.PurchaseOrder] = AppShortcuts.PurchaseOrder,
        [TradeDocumentKind.DeliveryChallan] = AppShortcuts.DeliveryNote,
        [TradeDocumentKind.GoodsReceipt] = AppShortcuts.ReceiptNote,
    };

    /// <summary>
    /// Stock-voucher purposes, on the keys whose meaning already matches.
    /// </summary>
    /// <remarks>
    /// Alt+F8 and Alt+F9 also select the delivery challan and the goods receipt on the Sales &amp;
    /// Purchase screen. That is the same meaning, not a second one: on both screens Alt+F8 is "goods
    /// going out" and Alt+F9 is "goods coming in". ADR-0006's rule forbids a gesture that means two
    /// <em>different</em> things depending on where the operator is; keeping one idea on one key
    /// across screens is what the rule is for.
    /// </remarks>
    private static readonly Dictionary<StockVoucherPurpose, AppShortcut> StockPurposeKeys = new()
    {
        [StockVoucherPurpose.Receipt] = AppShortcuts.ReceiptNote,
        [StockVoucherPurpose.Issue] = AppShortcuts.DeliveryNote,
        [StockVoucherPurpose.Transfer] = AppShortcuts.StockJournal,
        [StockVoucherPurpose.OpeningBalance] = AppShortcuts.OtherVouchers,
    };

    /// <summary>The key that selects <paramref name="kind"/>, or null when it has none.</summary>
    public static AppShortcut? For(VoucherKind kind) =>
        VoucherKeys.TryGetValue(kind, out var shortcut) ? shortcut : null;

    /// <summary>The key that selects <paramref name="kind"/>, or null when it has none.</summary>
    public static AppShortcut? For(TradeDocumentKind kind) =>
        DocumentKeys.TryGetValue(kind, out var shortcut) ? shortcut : null;

    /// <summary>The key that selects <paramref name="purpose"/>, or null when it has none.</summary>
    public static AppShortcut? For(StockVoucherPurpose purpose) =>
        StockPurposeKeys.TryGetValue(purpose, out var shortcut) ? shortcut : null;

    /// <summary>The stock-voucher purposes, in the order the button bar shows them.</summary>
    public static IReadOnlyList<StockVoucherPurpose> KeyedStockPurposes { get; } =
    [
        StockVoucherPurpose.Receipt,
        StockVoucherPurpose.Issue,
        StockVoucherPurpose.Transfer,
        StockVoucherPurpose.OpeningBalance,
    ];

    /// <summary>The voucher kinds that answer to a key, in the order the button bar shows them.</summary>
    public static IReadOnlyList<VoucherKind> KeyedVoucherKinds { get; } =
    [
        VoucherKind.Contra,
        VoucherKind.Payment,
        VoucherKind.Receipt,
        VoucherKind.Journal,
        VoucherKind.Sales,
        VoucherKind.Purchase,
        VoucherKind.DebitNote,
        VoucherKind.CreditNote,
    ];

    /// <summary>The document kinds that answer to a key, in the order the button bar shows them.</summary>
    public static IReadOnlyList<TradeDocumentKind> KeyedDocumentKinds { get; } =
    [
        TradeDocumentKind.TaxInvoice,
        TradeDocumentKind.PurchaseInvoice,
        TradeDocumentKind.SalesOrder,
        TradeDocumentKind.PurchaseOrder,
        TradeDocumentKind.DeliveryChallan,
        TradeDocumentKind.GoodsReceipt,
    ];

    /// <summary>The document kinds reached through F10 because no key is published for them.</summary>
    public static IReadOnlyList<TradeDocumentKind> UnkeyedDocumentKinds { get; } =
        [.. Enum.GetValues<TradeDocumentKind>().Where(kind => !DocumentKeys.ContainsKey(kind))];

    /// <summary>A short label for the bar — "Sales" reads better on a key than "TaxInvoice".</summary>
    public static string LabelFor(TradeDocumentKind kind) => kind switch
    {
        TradeDocumentKind.Quotation => "Quotation",
        TradeDocumentKind.SalesOrder => "Sales order",
        TradeDocumentKind.DeliveryChallan => "Delivery note",
        TradeDocumentKind.TaxInvoice => "Sales",
        TradeDocumentKind.PurchaseOrder => "Purchase order",
        TradeDocumentKind.GoodsReceipt => "Receipt note",
        TradeDocumentKind.PurchaseInvoice => "Purchase",
        _ => kind.ToString(),
    };

    /// <summary>A short label for the bar; the enum name reads badly on a key.</summary>
    public static string LabelFor(StockVoucherPurpose purpose) => purpose switch
    {
        StockVoucherPurpose.OpeningBalance => "Opening",
        StockVoucherPurpose.Receipt => "Receipt",
        StockVoucherPurpose.Issue => "Issue",
        StockVoucherPurpose.Transfer => "Transfer",
        _ => purpose.ToString(),
    };
}
