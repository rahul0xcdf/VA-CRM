using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Customers;

/// <summary>
/// An optional customer-level price for one item, which wins over the customer's category price
/// when present (phase-4-customer-pricing.md §2 as added at approval, §4a).
/// </summary>
/// <remarks>
/// Same shape and guard as <see cref="CategoryPriceListLine"/> — exactly one of a fixed price or a
/// margin percent, selected by <see cref="Basis"/>. Resolution order for a sale line is this row →
/// the category price-list line → the item master's default sale price.
/// </remarks>
public sealed class CustomerSpecialPrice : Entity, IAuditStamped
{
    private CustomerSpecialPrice()
    {
        // EF Core materialisation.
    }

    public CustomerSpecialPrice(
        Guid companyId,
        Guid customerId,
        Guid itemId,
        PriceBasis basis,
        decimal? fixedPrice = null,
        decimal? marginPercent = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A special price must name its customer.", nameof(customerId));
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("A special price must name its item.", nameof(itemId));
        }

        CompanyId = companyId;
        CustomerId = customerId;
        ItemId = itemId;
        SetPrice(basis, fixedPrice, marginPercent);
    }

    public Guid CompanyId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>Which of the two values below prices this line.</summary>
    public PriceBasis Basis { get; private set; }

    /// <summary>The fixed selling price — computed from minor units, never a column.</summary>
    public decimal? FixedPrice => FixedPriceMinor.HasValue
        ? MoneyScale.FromMinor(FixedPriceMinor.Value)
        : null;

    /// <summary>Fixed selling price, in minor units of 1/10,000 (ADR-0003). Set only when
    /// <see cref="Basis"/> is <see cref="PriceBasis.FixedPrice"/>.</summary>
    public long? FixedPriceMinor { get; private set; }

    /// <summary>The margin percent, two decimal places — computed from minor units.</summary>
    public decimal? MarginPercent => MarginPercentMinor.HasValue
        ? MoneyScale.FromMinor(MarginPercentMinor.Value)
        : null;

    /// <summary>
    /// Margin percent, in minor units of 1/10,000 (ADR-0003) after rounding to two decimal places.
    /// Set only when <see cref="Basis"/> is a margin basis.
    /// </summary>
    public long? MarginPercentMinor { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>
    /// Re-points the line at a new basis and value. Exactly one of the two values must be supplied,
    /// matched to the basis; the other is cleared, so an edit can never leave both behind.
    /// </summary>
    public void SetPrice(
        PriceBasis basis,
        decimal? fixedPrice = null,
        decimal? marginPercent = null)
    {
        if (!Enum.IsDefined(basis))
        {
            throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown price basis.");
        }

        if (basis == PriceBasis.FixedPrice)
        {
            if (marginPercent.HasValue)
            {
                throw new ArgumentException(
                    "A fixed-price line cannot also carry a margin percent.", nameof(marginPercent));
            }

            FixedPriceMinor = RequireScale(fixedPrice, nameof(fixedPrice));
            MarginPercentMinor = null;
        }
        else
        {
            if (fixedPrice.HasValue)
            {
                throw new ArgumentException(
                    $"A {basis} line is priced by its margin percent; clear the fixed price.",
                    nameof(fixedPrice));
            }

            if (!marginPercent.HasValue)
            {
                throw new ArgumentException(
                    $"A {basis} line requires a margin percent.", nameof(marginPercent));
            }

            if (marginPercent.Value < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(marginPercent),
                    marginPercent,
                    "A margin percent cannot be negative; line discounts handle reductions.");
            }

            // Percentages store as minor units too (ADR-0003): a decimal here would land on a TEXT
            // column, and any future SUM over one aggregates in floating point. Rounded to two
            // decimal places first, exactly like TradeDocumentLine.DiscountPercentMinor.
            MarginPercentMinor = MoneyScale.ToMinor(
                Math.Round(marginPercent.Value, 2, MidpointRounding.ToEven));
            FixedPriceMinor = null;
        }

        Basis = basis;
    }

    /// <summary>Validates a non-negative price at the storable scale and converts it to minor units.</summary>
    private static long RequireScale(decimal? value, string paramName)
    {
        if (!value.HasValue)
        {
            throw new ArgumentException(
                "A fixed-price line requires a fixed selling price.", paramName);
        }

        if (value.Value < 0m)
        {
            throw new ArgumentOutOfRangeException(
                paramName, value, "A selling price cannot be negative.");
        }

        var rounded = MoneyScale.Round(value.Value);

        if (rounded != value.Value)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"A price carries more than {MoneyScale.Places} decimal places.");
        }

        return MoneyScale.ToMinor(rounded);
    }
}
