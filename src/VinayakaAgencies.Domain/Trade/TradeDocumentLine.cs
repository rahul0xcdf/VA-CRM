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
        IReadOnlyList<string>? serialNumbers = null)
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

    /// <summary>The line's parsed serial list, or an empty list when none were staged.</summary>
    public IReadOnlyList<string> ParsedSerials =>
        SerialNumbersText?
            .Split([';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList()
        ?? [];

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
