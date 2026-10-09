using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Customers;

/// <summary>
/// One row of a category's price list: the price or margin at which an item sells to every customer
/// in a <see cref="CustomerCategory"/> (phase-4-customer-pricing.md §2, §4a).
/// </summary>
/// <remarks>
/// The line carries exactly one of its two numbers, selected by <see cref="Basis"/>: a fixed
/// selling price, or a margin percent over cost or over the item's MRP / default sale price. The
/// guard makes a mis-shaped line unrepresentable — a row with both values, or with neither, cannot
/// exist even in memory, so the pricing service never has to guess.
/// <para>
/// Resolution order for a sale line is customer special price → this category line → the item
/// master's default sale price (§4a).
/// </para>
/// </remarks>
public sealed class CategoryPriceListLine : Entity, IAuditStamped
{
    private CategoryPriceListLine()
    {
        // EF Core materialisation.
    }

    public CategoryPriceListLine(
        Guid companyId,
        Guid customerCategoryId,
        Guid itemId,
        PriceBasis basis,
        decimal? fixedPrice = null,
        decimal? marginPercent = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (customerCategoryId == Guid.Empty)
        {
            throw new ArgumentException(
                "A price list line must name its customer category.", nameof(customerCategoryId));
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("A price list line must name its item.", nameof(itemId));
        }

        CompanyId = companyId;
        CustomerCategoryId = customerCategoryId;
        ItemId = itemId;
        SetPrice(basis, fixedPrice, marginPercent);
    }

    public Guid CompanyId { get; private set; }

    public Guid CustomerCategoryId { get; private set; }

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
    /// Margin percent, in minor units of 1/10,000 (ADR-0003) after rounding to two decimal places —
    /// the same treatment as <c>TradeDocumentLine.DiscountPercentMinor</c>. Set only when
    /// <see cref="Basis"/> is a margin basis.
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
