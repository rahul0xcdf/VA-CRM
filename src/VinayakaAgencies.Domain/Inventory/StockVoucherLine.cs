using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// One line of a stock voucher: a quantity of one item received into or issued out of one godown
/// (phase-2-inventory.md §1).
/// </summary>
public sealed class StockVoucherLine : Entity, ICreationStamped, IBelongsToAggregate
{
    private StockVoucherLine()
    {
        // EF Core materialisation.
    }

    public StockVoucherLine(
        Guid companyId,
        Guid stockVoucherId,
        int lineNumber,
        StockMovementDirection direction,
        Guid itemId,
        Guid godownId,
        decimal quantity,
        bool isSerialTracked,
        bool isBatchTracked,
        decimal unitCost,
        string? batchNumber)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (stockVoucherId == Guid.Empty)
        {
            throw new ArgumentException("StockVoucherId must not be empty.", nameof(stockVoucherId));
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("ItemId must not be empty.", nameof(itemId));
        }

        if (godownId == Guid.Empty)
        {
            throw new ArgumentException("GodownId must not be empty.", nameof(godownId));
        }

        if (lineNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineNumber), lineNumber, "Line numbers start at 1.");
        }

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "A stock quantity must be positive.");
        }

        if (isSerialTracked && quantity != decimal.Truncate(quantity))
        {
            throw new InvalidOperationException(
                "A serial-tracked line carries whole units only; a fractional quantity cannot "
                + "be traced to individual serial numbers.");
        }

        if (isBatchTracked && direction == StockMovementDirection.Increase
            && string.IsNullOrWhiteSpace(batchNumber))
        {
            throw new InvalidOperationException(
                "A receipt of a batch-tracked item must carry its batch number.");
        }

        var roundedQuantity = MoneyScale.Round(quantity);

        if (roundedQuantity != quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"A stock quantity carries more than {MoneyScale.Places} decimal places.");
        }

        CompanyId = companyId;
        StockVoucherId = stockVoucherId;
        LineNumber = lineNumber;
        Direction = direction;
        ItemId = itemId;
        GodownId = godownId;

        // Minor units (ADR-0003): quantities persist as scaled integers so no SQLite aggregation
        // ever lands in IEEE-754 double.
        QuantityMinor = MoneyScale.ToMinor(roundedQuantity);
        IsSerialTracked = isSerialTracked;
        IsBatchTracked = isBatchTracked;

        // Receipt-side costs are what the operator paid; issue-side costs are computed later by the
        // valuation engine against the ledger as it stood just before this voucher. The column is
        // therefore written twice in the posting flow: once here (receipts) and once by the
        // service that owns valuation (issues). Zero means "not yet valued".
        UnitCostMinor = direction == StockMovementDirection.Increase ? MoneyScale.ToMinor(unitCost) : 0L;
        BatchNumber = string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber.Trim();
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Denormalised aggregate root id, so edit-log rows group under their voucher.</summary>
    public Guid StockVoucherId { get; private set; }

    public int LineNumber { get; private set; }

    public StockMovementDirection Direction { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid GodownId { get; private set; }

    /// <summary>
    /// Base units in the item's base unit, always positive — as a domain decimal, computed from
    /// <see cref="QuantityMinor"/> and never a column (ADR-0003).
    /// </summary>
    public decimal Quantity => MoneyScale.FromMinor(QuantityMinor);

    /// <summary>Base units, in minor units of 1/10,000 (ADR-0003). Always positive.</summary>
    public long QuantityMinor { get; private set; }

    /// <summary>Carried from the item so the posting path can enforce serial/batch rules.</summary>
    public bool IsSerialTracked { get; private set; }

    /// <summary>Carried from the item so receipts must name their batch.</summary>
    public bool IsBatchTracked { get; private set; }

    /// <summary>
    /// Cost of one base unit, in minor units of 1/10,000 (ADR-0003). Zero until valued.
    /// </summary>
    public long UnitCostMinor { get; private set; }

    /// <summary>Batch name on a receipt of a batch-tracked item; optional on issues.</summary>
    public string? BatchNumber { get; private set; }

    /// <summary>
    /// Assigns a cost computed outside the operator's input — the valuation engine's costing on
    /// issues, or the derived cost a transfer receipt must carry so total stock value is unchanged
    /// (checkpoint C2.5). Called once per line, before the ledger rows are appended.
    /// </summary>
    public void ApplyValuedCost(long unitCostMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unitCostMinor);

        if (UnitCostMinor != 0L)
        {
            throw new InvalidOperationException(
                $"Line {LineNumber} has already been valued.");
        }

        UnitCostMinor = unitCostMinor;
    }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AggregateType => nameof(StockVoucher);

    public string AggregateId => StockVoucherId.ToString();
}
