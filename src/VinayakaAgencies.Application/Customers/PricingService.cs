using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// Resolves the price for a sale line through the §4a order — customer special price, category
/// price list, item master default — computing margin lines from their basis values
/// (phase-4-customer-pricing.md §2, checkpoint C4.4).
/// </summary>
/// <remarks>
/// Margin arithmetic follows the codebase idiom (ADR-0003): minor units come back to decimal once,
/// the factor multiplies exactly, and the result is rounded once and converted back to minor
/// units once — never floating point, never two roundings.
/// <para>
/// A tier declines rather than guesses: a fixed price of zero prices free, but a margin line over
/// a missing cost or a missing MRP cannot produce a sane rate, so resolution falls through to the
/// next tier, and null when nothing anywhere can price the line.
/// </para>
/// </remarks>
public sealed class PricingService(
    IUnitOfWork unitOfWork,
    IStockValuationService valuationService,
    IClock clock) : IPricingService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly IStockValuationService _valuationService = valuationService
        ?? throw new ArgumentNullException(nameof(valuationService));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async Task<ResolvedSalePrice?> ResolveForSaleAsync(
        Guid companyId,
        Guid? customerId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var item = await RequireItemAsync(companyId, itemId, cancellationToken).ConfigureAwait(false);

        if (customerId.HasValue)
        {
            var customer = await _unitOfWork.Repository<Customer>()
                .GetByIdAsync(customerId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (customer is not null && customer.CompanyId == companyId)
            {
                var special = await FindSpecialPriceAsync(customerId.Value, itemId, cancellationToken)
                    .ConfigureAwait(false);

                var resolved = await TryResolveLineAsync(
                        SalePriceSource.CustomerSpecialPrice,
                        special?.Basis,
                        special?.FixedPriceMinor,
                        special?.MarginPercentMinor,
                        item,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (resolved is not null)
                {
                    return resolved;
                }

                var categoryLine = await FindCategoryLineAsync(
                        customer.CustomerCategoryId, itemId, cancellationToken)
                    .ConfigureAwait(false);

                resolved = await TryResolveLineAsync(
                        SalePriceSource.CategoryPriceList,
                        categoryLine?.Basis,
                        categoryLine?.FixedPriceMinor,
                        categoryLine?.MarginPercentMinor,
                        item,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (resolved is not null)
                {
                    return resolved;
                }
            }
        }

        // The item master's default sale price: the final fallback, at its face value.
        return item.DefaultSalePriceMinor is > 0
            ? new ResolvedSalePrice(
                SalePriceSource.ItemDefault,
                PriceBasis.FixedPrice,
                RateMinor: item.DefaultSalePriceMinor.Value,
                MarginPercentMinor: null,
                BasisValueMinor: null)
            : null;
    }

    /// <summary>
    /// Turns one price line into a resolved price, declining when the line's own basis value is
    /// unavailable — so a misconfigured tier hides nothing but also blocks nothing below it.
    /// </summary>
    private async Task<ResolvedSalePrice?> TryResolveLineAsync(
        SalePriceSource source,
        PriceBasis? basis,
        long? fixedPriceMinor,
        long? marginPercentMinor,
        Item item,
        CancellationToken cancellationToken)
    {
        if (basis is null)
        {
            return null;
        }

        switch (basis)
        {
            case PriceBasis.FixedPrice:
                return fixedPriceMinor is > 0
                    ? new ResolvedSalePrice(
                        source, basis.Value, fixedPriceMinor.Value, null, null)
                    : null;

            case PriceBasis.MarginOnCost:
            {
                if (marginPercentMinor is null)
                {
                    return null;
                }

                // The item's current unit cost as of today, one base unit, from the Phase 2
                // costing engine — never recomputed here.
                var cost = await _valuationService
                    .ValueIssueAsync(item.Id, 1m, _clock.Today, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return cost.UnitCostMinor <= 0
                    ? null
                    : new ResolvedSalePrice(
                        source,
                        basis.Value,
                        ApplyMargin(cost.UnitCostMinor, marginPercentMinor.Value, subtract: false),
                        marginPercentMinor,
                        cost.UnitCostMinor);
            }

            case PriceBasis.MarginOnSalePrice:
            {
                if (marginPercentMinor is null || item.DefaultSalePriceMinor is not > 0)
                {
                    return null;
                }

                return new ResolvedSalePrice(
                    source,
                    basis.Value,
                    ApplyMargin(item.DefaultSalePriceMinor.Value, marginPercentMinor.Value, subtract: true),
                    marginPercentMinor,
                    item.DefaultSalePriceMinor);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown price basis.");
        }
    }

    /// <summary>
    /// Applies a margin percent to a basis value in minor units: percent ×10⁴ over 10⁴, so 20%
    /// adds a fifth and, on the sale-price basis, subtracts a tenth for −10%.
    /// </summary>
    private static long ApplyMargin(long basisValueMinor, long marginPercentMinor, bool subtract) =>
        MoneyScale.ToMinor(MoneyScale.Round(
            MoneyScale.FromMinor(basisValueMinor)
            * (subtract
                ? 1m - MoneyScale.FromMinor(marginPercentMinor) / 100m
                : 1m + MoneyScale.FromMinor(marginPercentMinor) / 100m)));

    private async Task<Item> RequireItemAsync(
        Guid companyId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Repository<Item>()
            .GetByIdAsync(itemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null || item.CompanyId != companyId)
        {
            throw new InvalidOperationException("This item does not exist in this company's books.");
        }

        return item;
    }

    private async Task<CustomerSpecialPrice?> FindSpecialPriceAsync(
        Guid customerId,
        Guid itemId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<CustomerSpecialPrice>()
                .ListAsync(new SpecialPriceOf(customerId, itemId), cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault();

    private async Task<CategoryPriceListLine?> FindCategoryLineAsync(
        Guid categoryId,
        Guid itemId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<CategoryPriceListLine>()
                .ListAsync(new CategoryLineOf(categoryId, itemId), cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault();

    private sealed class SpecialPriceOf : Specification<CustomerSpecialPrice>
    {
        public SpecialPriceOf(Guid customerId, Guid itemId)
        {
            Where(line => line.CustomerId == customerId && line.ItemId == itemId);
            ApplyNoTracking();
        }
    }

    private sealed class CategoryLineOf : Specification<CategoryPriceListLine>
    {
        public CategoryLineOf(Guid categoryId, Guid itemId)
        {
            Where(line => line.CustomerCategoryId == categoryId && line.ItemId == itemId);
            ApplyNoTracking();
        }
    }
}
