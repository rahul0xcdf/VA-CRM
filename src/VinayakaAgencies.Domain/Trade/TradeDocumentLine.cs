using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Trade;

/// <summary>
/// One line of a trade document: a quantity of one item at a rate, with an optional line discount
/// (phase-3-sales-purchase-pipeline.md §2).
/// </summary>
/// <remarks>
/// Godown rules are the document kind's business, not the line's: stock-moving kinds
/// (tax invoice, purchase invoice) and the staging kinds that feed them (delivery challan,
/// goods receipt) must name the godown every quantity moves through; pure commercial documents
/// (quotation, sales order, purchase order) must not carry one. Enforced on
/// <see cref="TradeDocument.AddLine"/> so a mis-shaped line cannot exist even in memory.
/// </remarks>
public sealed class TradeDocumentLine : Entity, ICreationStamped, IBelongsToAggregate
{
    private TradeDocumentLine()
    {
        // EF Core materialisation.
    }

    public TradeDocumentLine(
        Guid companyId,
        Guid tradeDocumentId,
        int lineNumber,
        Guid itemId,
        Guid? godownId,
        decimal quantity,
        decimal rate,
        decimal discountPercent,
        string? batchNumber = null,
        IReadOnlyList<string>? serialNumbers = null,
        long? appliedRateMinor = null,
        bool isPriceOverridden = false,
        SalePriceSource priceSource = SalePriceSource.Manual)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (tradeDocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "TradeDocumentId must not be empty.", nameof(tradeDocumentId));
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("ItemId must not be empty.", nameof(itemId));
        }

        if (godownId.HasValue && godownId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "GodownId must be null or a real id — never Guid.Empty.", nameof(godownId));
        }

        if (lineNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineNumber), lineNumber, "Line numbers start at 1.");
        }

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "An ordered or invoiced quantity must be positive.");
        }

        var roundedQuantity = MoneyScale.Round(quantity);

        if (roundedQuantity != quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"A line quantity carries more than {MoneyScale.Places} decimal places.");
        }

        if (rate < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate), rate, "A rate cannot be negative.");
        }

        var roundedRate = MoneyScale.Round(rate);

        if (roundedRate != rate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate),
                rate,
                $"A rate carries more than {MoneyScale.Places} decimal places.");
        }

        if (discountPercent < 0m || discountPercent > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discountPercent), discountPercent,
                "A discount is a percentage between 0 and 100.");
        }

        CompanyId = companyId;
        TradeDocumentId = tradeDocumentId;
        LineNumber = lineNumber;
        ItemId = itemId;
        GodownId = godownId;
        QuantityMinor = MoneyScale.ToMinor(roundedQuantity);
        RateMinor = MoneyScale.ToMinor(roundedRate);

        // Percentages store as minor units too (ADR-0003): a decimal here would land on a
        // TEXT column, and any future SUM over one aggregates in floating point.
        DiscountPercentMinor = MoneyScale.ToMinor(
            Math.Round(discountPercent, 2, MidpointRounding.ToEven));

        BatchNumber = string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber.Trim();

        if (serialNumbers is not null && serialNumbers.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A serial number list cannot carry a blank entry.", nameof(serialNumbers));
        }

        // Stored ';'-joined so a draft invoice's serial selection survives edit cycles; parsed and
        // availability-checked at posting time, exactly like manual stock vouchers.
        SerialNumbersText = serialNumbers is null || serialNumbers.Count == 0
            ? null
            : string.Join(";", serialNumbers.Select(serial => serial.Trim()));

        // Pricing evidence (phase-4 §4a, checkpoint C4.2). A line built without pricing — every
        // pre-existing construction path — is manual, not overridden, and applied what was keyed.
        if (!Enum.IsDefined(priceSource))
        {
            throw new ArgumentOutOfRangeException(
                nameof(priceSource), priceSource, "Unknown sale price source.");
        }

        if (appliedRateMinor.HasValue && appliedRateMinor.Value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appliedRateMinor),
                appliedRateMinor,
                "An applied rate cannot be negative.");
        }

        AppliedRateMinor = appliedRateMinor ?? RateMinor;
        IsPriceOverridden = isPriceOverridden;
        PriceSource = priceSource;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Denormalised aggregate root id, so edit-log rows group under their document.</summary>
    public Guid TradeDocumentId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>
    /// The godown this line's quantity moves through, where the kind carries stock intent.
    /// Null on purely commercial documents.
    /// </summary>
    public Guid? GodownId { get; private set; }

    /// <summary>Quantity in the item's base unit — computed from minor units, never a column.</summary>
    public decimal Quantity => MoneyScale.FromMinor(QuantityMinor);

    /// <summary>Quantity, in minor units of 1/10,000 (ADR-0003). Always positive.</summary>
    public long QuantityMinor { get; private set; }

    /// <summary>Rate per unit before discount — computed from minor units, never a column.</summary>
    public decimal Rate => MoneyScale.FromMinor(RateMinor);

    /// <summary>Rate per unit, in minor units of 1/10,000 (ADR-0003).</summary>
    public long RateMinor { get; private set; }

    /// <summary>Line discount as a percentage, two decimal places — computed from minor units.</summary>
    public decimal DiscountPercent => MoneyScale.FromMinor(DiscountPercentMinor);

    /// <summary>Discount percent, in minor units of 1/10,000 (ADR-0003). Never a TEXT column.</summary>
    public long DiscountPercentMinor { get; private set; }

    /// <summary>
    /// Batch name carried on receipt-side lines of batch-tracked items (purchase invoices, goods
    /// receipts); optional elsewhere.
    /// </summary>
    public string? BatchNumber { get; private set; }

    /// <summary>
    /// The serial numbers chosen for this line (';'-joined), on serial-tracked invoice lines.
    /// Null for everything else.
    /// </summary>
    public string? SerialNumbersText { get; private set; }

    /// <summary>
    /// The rate the pricing engine auto-applied at line creation, in minor units of 1/10,000
    /// (ADR-0003). Equals <see cref="RateMinor"/> on a hand-keyed line, and is the before-side of
    /// checkpoint C4.2's override evidence: kept even when the operator changes the rate before
    /// the first save, which an audit before-image alone could not show.
    /// </summary>
    public long AppliedRateMinor { get; private set; }

    /// <summary>Whether the final rate was moved off <see cref="AppliedRateMinor"/> by an
    /// authorised override.</summary>
    public bool IsPriceOverridden { get; private set; }

    /// <summary>Which pricing tier produced <see cref="AppliedRateMinor"/>.</summary>
    public SalePriceSource PriceSource { get; private set; }

    /// <summary>
    /// Taxable value at posting time, in minor units (ADR-0003) — the net line value the GST rate
    /// applied to. Written once by the posting-time computation; zero on lines that never posted.
    /// </summary>
    public long TaxableValueMinor { get; private set; }

    /// <summary>Resolved GST rate as a percent in minor units (18% = 180_000), snapshotted with
    /// the tax figures so returns never have to re-resolve schedules.</summary>
    public long GstRatePercentMinor { get; private set; }

    public long IgstMinor { get; private set; }

    public long CgstMinor { get; private set; }

    public long SgstMinor { get; private set; }

    public long CessMinor { get; private set; }

    /// <summary>The line's parsed serial list, or an empty list when none were staged.</summary>
    public IReadOnlyList<string> ParsedSerials =>
        SerialNumbersText?
            .Split([';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList()
        ?? [];

    /// <summary>
    /// Records a permission-gated price override: the final rate moves off the auto-applied one,
    /// and the evidence pair (<see cref="AppliedRateMinor"/>, <see cref="IsPriceOverridden"/>)
    /// keeps both values readable even before the first save reaches the audit trail.
    /// </summary>
    /// <remarks>
    /// The caller — the pricing service or the UI behind it — is responsible for the permission
    /// gate and for the audit-trail write of original and overridden values (phase-4 §2); this
    /// method only makes the line itself carry the truth.
    /// </remarks>
    public void RecordPriceOverride(decimal overriddenRate)
    {
        if (decimal.IsNegative(overriddenRate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(overriddenRate), overriddenRate, "A rate cannot be negative.");
        }

        var rounded = MoneyScale.Round(overriddenRate);

        if (rounded != overriddenRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overriddenRate),
                overriddenRate,
                $"A rate carries more than {MoneyScale.Places} decimal places.");
        }

        RateMinor = MoneyScale.ToMinor(rounded);
        IsPriceOverridden = true;
    }

    /// <summary>
    /// Records the posting-time tax computation for this line (phase-5-gst-returns.md §2):
    /// taxable value, the rate that resolved on the document date, and the four tax heads.
    /// Written exactly once — a line that already carries a snapshot is either posted or being
    /// re-posted, and neither may be silently overwritten.
    /// </summary>
    public void AttachTaxSnapshot(
        long taxableValueMinor,
        long ratePercentMinor,
        long igstMinor,
        long cgstMinor,
        long sgstMinor,
        long cessMinor)
    {
        if (TaxableValueMinor != 0L || IgstMinor != 0L || CgstMinor != 0L
            || SgstMinor != 0L || CessMinor != 0L)
        {
            throw new InvalidOperationException(
                "This invoice line already carries a GST snapshot; reverse the invoice instead of "
                + "overwriting its tax.");
        }

        GuardNonNegative(taxableValueMinor, nameof(taxableValueMinor));
        GuardNonNegative(ratePercentMinor, nameof(ratePercentMinor));
        GuardNonNegative(igstMinor, nameof(igstMinor));
        GuardNonNegative(cgstMinor, nameof(cgstMinor));
        GuardNonNegative(sgstMinor, nameof(sgstMinor));
        GuardNonNegative(cessMinor, nameof(cessMinor));

        TaxableValueMinor = taxableValueMinor;
        GstRatePercentMinor = ratePercentMinor;
        IgstMinor = igstMinor;
        CgstMinor = cgstMinor;
        SgstMinor = sgstMinor;
        CessMinor = cessMinor;
    }

    private static void GuardNonNegative(long value, string parameterName)
    {
        if (value < 0L)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, value, "A tax snapshot figure cannot be negative.");
        }
    }

    /// <summary>Value before discount, rounded once at birth (banker's, ADR-0003).</summary>
    public decimal GrossValue => MoneyScale.Round(Quantity * Rate);

    /// <summary>
    /// Value after discount: quantity × rate × (1 − discount/100), rounded once here and never
    /// re-derived downstream, so document totals are exact sums of exact lines.
    /// </summary>
    public decimal NetValue => MoneyScale.Round(Quantity * Rate * (1m - DiscountPercent / 100m));

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AggregateType => nameof(TradeDocument);

    public string AggregateId => TradeDocumentId.ToString();
}
