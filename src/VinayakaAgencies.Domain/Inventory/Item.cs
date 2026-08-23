using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// An item the business buys, stocks and sells (phase-2-inventory.md §2 as amended on approval).
/// </summary>
/// <remarks>
/// Approved as a <b>name-and-code master</b>: the motor/pump attribute list in the draft spec
/// (brand, model, HP, phase, …) was dropped by owner decision (<c>open-questions.md</c> #5), and
/// HSN/SAC moves to Phase 5 where GST first needs it, as data-driven config — never hardcoded.
/// Attributes remain addable later as plain nullable columns.
/// <para>
/// There is deliberately no quantity column of any kind: on-hand is always derived from the unified
/// stock ledger, exactly as ledger balances are derived from journal lines.
/// </para>
/// </remarks>
public sealed class Item : Entity, IAuditStamped
{
    private Item()
    {
        // EF Core materialisation.
    }

    public Item(
        Guid companyId,
        string name,
        Guid baseUnitId,
        Guid? stockGroupId = null,
        string? code = null,
        string? barcode = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (baseUnitId == Guid.Empty)
        {
            throw new ArgumentException("An item must have a base unit.", nameof(baseUnitId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CompanyId = companyId;
        SetName(name);
        SetCode(code);
        SetBarcode(barcode);
        StockGroupId = stockGroupId;
        BaseUnitId = baseUnitId;
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Name"/>, over which the unique <c>(CompanyId, NameNormalised)</c>
    /// index is built. See <see cref="NameNormaliser"/>.
    /// </summary>
    public string NameNormalised { get; private set; } = string.Empty;

    /// <summary>Operator-facing stock code ("SKU"), unique per company when present.</summary>
    public string? Code { get; private set; }

    /// <summary>
    /// Optional scan target. Barcode support exists but is never required — barcode-free billing is
    /// the default (phase-2-inventory.md §2).
    /// </summary>
    public string? Barcode { get; private set; }

    public Guid? StockGroupId { get; private set; }

    /// <summary>The unit every stored stock quantity is denominated in.</summary>
    public Guid BaseUnitId { get; private set; }

    /// <summary>
    /// Alternate (sourcing / selling) unit, or null when the item has only one unit.
    /// </summary>
    public Guid? AlternateUnitId { get; private set; }

    /// <summary>
    /// How many base units one alternate unit contains — as a domain decimal, computed from
    /// <see cref="ConversionFactorMinor"/> and never a column (ADR-0003). Meaningful only together
    /// with <see cref="AlternateUnitId"/>.
    /// </summary>
    public decimal? ConversionFactor => ConversionFactorMinor.HasValue
        ? MoneyScale.FromMinor(ConversionFactorMinor.Value)
        : null;

    /// <summary>The conversion factor in minor units of 1/10,000 (ADR-0003).</summary>
    public long? ConversionFactorMinor { get; private set; }

    /// <summary>
    /// Whether each physical unit carries a serial number (per-item flag,
    /// <c>open-questions.md</c> #6). Serial quantities must be whole numbers.
    /// </summary>
    public bool IsSerialTracked { get; private set; }

    /// <summary>Whether receipts of this item carry batch numbers for aging and traceability.</summary>
    public bool IsBatchTracked { get; private set; }

    /// <summary>
    /// Per-item valuation override; null means "use the company default"
    /// (<see cref="InventorySettings.DefaultValuationMethod"/>, FIFO at launch).
    /// </summary>
    public ValuationMethod? ValuationOverride { get; private set; }

    /// <summary>
    /// On-hand below this level raises a low-stock alert. Zero disables alerting for the item.
    /// Persisted in minor units of 1/10,000 (ADR-0003) so the reorder report can compare against
    /// summed ledger quantities in exact integer arithmetic.
    /// </summary>
    public decimal ReorderLevel => MoneyScale.FromMinor(ReorderLevelMinor);

    public long ReorderLevelMinor { get; private set; }

    /// <summary>
    /// Kit item: not stocked itself; issuing it depletes its components
    /// (phase-2-inventory.md §2, basic BOM only).
    /// </summary>
    public bool IsKit { get; private set; }

    /// <summary>
    /// The item's default sale price — computed from minor units, never a column.
    /// </summary>
    public decimal? DefaultSalePrice => DefaultSalePriceMinor.HasValue
        ? MoneyScale.FromMinor(DefaultSalePriceMinor.Value)
        : null;

    /// <summary>
    /// Default sale price, in minor units of 1/10,000 (ADR-0003); null until priced.
    /// Added in Phase 4 (phase-4-customer-pricing.md §2, §4a): the MRP / sale-price basis for
    /// margin price lines and the final fallback when neither a customer special price nor a
    /// category price-list line exists.
    /// </summary>
    public long? DefaultSalePriceMinor { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SetName(name);
    }

    public void SetCode(string? code) =>
        Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    public void SetBarcode(string? barcode) =>
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

    public void MoveToGroup(Guid? stockGroupId) =>
        StockGroupId = stockGroupId;

    /// <summary>
    /// Sets the alternate unit pair. A null alternate clears both; otherwise the conversion factor
    /// must be positive and the two units must differ.
    /// </summary>
    public void SetUnits(Guid baseUnitId, Guid? alternateUnitId, decimal? conversionFactor)
    {
        if (baseUnitId == Guid.Empty)
        {
            throw new ArgumentException("An item must have a base unit.", nameof(baseUnitId));
        }

        if (alternateUnitId.HasValue)
        {
            if (alternateUnitId.Value == baseUnitId)
            {
                throw new InvalidOperationException(
                    "The alternate unit must differ from the base unit.");
            }

            if (conversionFactor is not > 0m)
            {
                throw new InvalidOperationException(
                    "A conversion factor greater than zero is required when an alternate unit is set.");
            }
        }
        else if (conversionFactor.HasValue)
        {
            throw new InvalidOperationException(
                "A conversion factor without an alternate unit has no meaning.");
        }

        BaseUnitId = baseUnitId;
        AlternateUnitId = alternateUnitId;

        if (alternateUnitId.HasValue)
        {
            var rounded = MoneyScale.Round(conversionFactor!.Value);

            if (rounded != conversionFactor!.Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(conversionFactor),
                    conversionFactor,
                    $"A conversion factor carries more than {MoneyScale.Places} decimal places.");
            }

            ConversionFactorMinor = MoneyScale.ToMinor(rounded);
        }
        else
        {
            ConversionFactorMinor = null;
        }
    }

    public void SetTracking(bool isSerialTracked, bool isBatchTracked)
    {
        if (isSerialTracked && isBatchTracked)
        {
            throw new InvalidOperationException(
                $"Item '{Name}' cannot be both serial-tracked and batch-tracked in this phase; " +
                "a serial already identifies the individual unit more precisely than any batch.");
        }

        IsSerialTracked = isSerialTracked;
        IsBatchTracked = isBatchTracked;
    }

    /// <summary>
    /// Clears the per-item valuation override (null) or selects an explicit method.
    /// </summary>
    public void SetValuationOverride(ValuationMethod? method) =>
        ValuationOverride = method;

    public void SetReorderLevel(decimal reorderLevel)
    {
        if (decimal.IsNegative(reorderLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reorderLevel),
                reorderLevel,
                "The reorder level cannot be negative. Zero disables alerting.");
        }

        var rounded = MoneyScale.Round(reorderLevel);

        if (rounded != reorderLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reorderLevel),
                reorderLevel,
                $"The reorder level carries more than {MoneyScale.Places} decimal places.");
        }

        ReorderLevelMinor = MoneyScale.ToMinor(rounded);
    }

    /// <summary>
    /// Sets the default sale price (null clears it). Zero is allowed — a free-issue item is a
    /// legitimate price — but a negative price and more precision than the store holds are not.
    /// </summary>
    public void SetDefaultSalePrice(decimal? defaultSalePrice)
    {
        if (!defaultSalePrice.HasValue)
        {
            DefaultSalePriceMinor = null;
            return;
        }

        if (decimal.IsNegative(defaultSalePrice.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultSalePrice),
                defaultSalePrice,
                "A selling price cannot be negative.");
        }

        var rounded = MoneyScale.Round(defaultSalePrice.Value);

        if (rounded != defaultSalePrice.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultSalePrice),
                defaultSalePrice,
                $"A price carries more than {MoneyScale.Places} decimal places.");
        }

        DefaultSalePriceMinor = MoneyScale.ToMinor(rounded);
    }

    public void MarkAsKit() => IsKit = true;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private void SetName(string name)
    {
        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }
}
