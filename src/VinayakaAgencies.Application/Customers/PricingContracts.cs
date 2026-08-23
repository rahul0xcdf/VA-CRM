using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// The price the pricing engine resolved for one sale line, with everything needed to explain it
/// on screen and to prove an override against (checkpoint C4.2).
/// </summary>
/// <param name="Source">Which tier won: customer special price, category line, or item default.</param>
/// <param name="Basis">The winning line's pricing basis.</param>
/// <param name="RateMinor">
/// The rate per unit to apply, in minor units of 1/10,000 — rounded once, ready for a
/// <c>TradeDocumentLine</c>.
/// </param>
/// <param name="MarginPercentMinor">The margin percent applied, when the basis is a margin.</param>
/// <param name="BasisValueMinor">
/// What the margin was computed over — the item's current unit cost for
/// <see cref="PriceBasis.MarginOnCost"/>, its default sale price for
/// <see cref="PriceBasis.MarginOnSalePrice"/>; null for a fixed price.
/// </param>
public sealed record ResolvedSalePrice(
    SalePriceSource Source,
    PriceBasis Basis,
    long RateMinor,
    long? MarginPercentMinor,
    long? BasisValueMinor);

/// <summary>Read model for one category price-list line or customer special price.</summary>
public sealed record PriceLineSummary(
    Guid Id,
    Guid OwnerId,
    Guid ItemId,
    string ItemName,
    PriceBasis Basis,
    decimal? FixedPrice,
    decimal? MarginPercent);

/// <summary>
/// Creates the line when <see cref="Id"/> is null, otherwise re-points that line. Exactly one of
/// <paramref name="FixedPrice"/> / <paramref name="MarginPercent"/> must be supplied, matched to
/// the basis — the domain guard enforces the pairing.
/// </summary>
public sealed record SavePriceLineRequest(
    Guid? Id,
    Guid OwnerId,
    Guid ItemId,
    PriceBasis Basis,
    decimal? FixedPrice = null,
    decimal? MarginPercent = null);

/// <summary>Resolves sale prices (phase-4-customer-pricing.md §2, §4a).</summary>
/// <remarks>
/// Resolution order per §4a: the customer's special price → their category's price-list line →
/// the item master's default sale price → nothing (null). A margin line whose basis value is
/// missing or zero — an item never costed, or never given a default sale price — cannot produce a
/// sane rate, so that tier declines and resolution falls through rather than silently selling at
/// zero. Conversion never calls this service: quoted prices travel forward unchanged.
/// </remarks>
public interface IPricingService
{
    /// <summary>
    /// Resolves the price for one sale line of <paramref name="itemId"/> to
    /// <paramref name="customerId"/>, or null when no configured price exists anywhere.
    /// </summary>
    Task<ResolvedSalePrice?> ResolveForSaleAsync(
        Guid companyId,
        Guid? customerId,
        Guid itemId,
        CancellationToken cancellationToken = default);
}

/// <summary>Maintains both price tables: per-category lines and per-customer special prices.</summary>
public interface IPriceListService
{
    Task<IReadOnlyList<PriceLineSummary>> ListCategoryPricesAsync(
        Guid customerCategoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PriceLineSummary>> ListCustomerSpecialPricesAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (creating or updating) the single price line for one item in a category — there is at
    /// most one per item per category, and at most one per item per customer.
    /// </summary>
    Task<PriceLineSummary> SetCategoryPriceAsync(
        SavePriceLineRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>The per-customer mirror of <see cref="SetCategoryPriceAsync"/>.</summary>
    Task<PriceLineSummary> SetCustomerSpecialPriceAsync(
        SavePriceLineRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveCategoryPriceAsync(Guid id, CancellationToken cancellationToken = default);

    Task RemoveCustomerSpecialPriceAsync(Guid id, CancellationToken cancellationToken = default);
}
