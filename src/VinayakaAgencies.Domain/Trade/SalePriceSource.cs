namespace VinayakaAgencies.Domain.Trade;

/// <summary>
/// Where a sale line's auto-applied price came from — the resolution order of
/// phase-4-customer-pricing.md §4a: customer special price → category price-list line → the item
/// master's default sale price.
/// </summary>
/// <remarks>
/// Recorded on <see cref="TradeDocumentLine"/> alongside <c>AppliedRateMinor</c> so checkpoint
/// C4.2's override evidence survives even when a price is overridden before the first save — the
/// audit trail's before-image alone cannot show a rate that never reached the database. A line
/// keyed by hand with no pricing behind it carries <see cref="Manual"/>; purchase-side lines carry
/// <see cref="None"/>, because the pricing engine prices sales only. Persisted by name, never by
/// number.
/// </remarks>
public enum SalePriceSource
{
    /// <summary>No pricing was applied to this line (purchase side, or nothing resolved).</summary>
    None = 0,

    /// <summary>The customer's own special price for the item.</summary>
    CustomerSpecialPrice = 1,

    /// <summary>The customer's category price-list line.</summary>
    CategoryPriceList = 2,

    /// <summary>The item master's default sale price.</summary>
    ItemDefault = 3,

    /// <summary>Keyed by hand, with no pricing engine involvement.</summary>
    Manual = 4,
}
