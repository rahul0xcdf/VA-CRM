namespace VinayakaAgencies.Domain.Customers;

/// <summary>
/// Which of a price line's two numbers applies
/// (phase-4-customer-pricing.md §2 as closed on approval, §4a).
/// </summary>
/// <remarks>
/// All three approaches exist side by side, chosen per item line: different item lines need
/// different treatment (§4a). The price-line guard enforces that exactly one value is present —
/// a fixed price for <see cref="FixedPrice"/>, a margin percent otherwise — so a row can never be
/// ambiguous about how its price is reached.
/// <para>
/// Persisted by name, never by number, so reordering the enum cannot silently change what an
/// existing row means.
/// </para>
/// </remarks>
public enum PriceBasis
{
    /// <summary>A fixed selling price per item for this customer or category.</summary>
    FixedPrice = 1,

    /// <summary>
    /// The margin percent computes the sale price over the item's cost.
    /// </summary>
    MarginOnCost = 2,

    /// <summary>
    /// The margin percent computes the sale price over the item's MRP / default sale price.
    /// </summary>
    MarginOnSalePrice = 3,
}
