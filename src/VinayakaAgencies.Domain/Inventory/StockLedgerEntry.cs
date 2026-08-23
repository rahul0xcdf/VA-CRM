using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// One immutable row of the unified stock ledger (phase-2-inventory.md §2;
/// <c>open-questions.md</c> #1).
/// </summary>
/// <remarks>
/// Every module — stock vouchers now, purchase and sales documents from Phase 3 on — records its
/// movements here and nowhere else. On-hand by godown, valuation, reorder alerts and aging are all
/// sums or replays over this table; no module keeps its own stock table or cached quantity.
/// <para>
/// Rows are append-only: a mistake is corrected by a later reversing entry, never by editing
/// history, which is what makes FIFO replay and the C2.1 reconciliation trustworthy.
/// </para>
/// </remarks>
public sealed class StockLedgerEntry : Entity, ICreationStamped, IBelongsToAggregate
{
    private StockLedgerEntry()
    {
        // EF Core materialisation.
    }

    public StockLedgerEntry(
        Guid companyId,
        long globalSequence,
        DateOnly entryDate,
        StockMovementKind kind,
        Guid itemId,
        Guid godownId,
        decimal quantity,
        long unitCostMinor,
        Guid stockVoucherId,
        Guid stockVoucherLineId,
        string? batchNumber)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("ItemId must not be empty.", nameof(itemId));
        }

        if (godownId == Guid.Empty)
        {
            throw new ArgumentException("GodownId must not be empty.", nameof(godownId));
        }

        if (stockVoucherId == Guid.Empty)
        {
            throw new ArgumentException("StockVoucherId must not be empty.", nameof(stockVoucherId));
        }

        if (stockVoucherLineId == Guid.Empty)
        {
            throw new ArgumentException(
                "StockVoucherLineId must not be empty.", nameof(stockVoucherLineId));
        }

        if (globalSequence < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(globalSequence), globalSequence, "Global sequence starts at 1.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(unitCostMinor);

        var direction = KindDirection(kind);

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "A ledger quantity must be positive.");
        }

        var rounded = MoneyScale.Round(quantity);

        if (rounded != quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"A ledger quantity carries more than {MoneyScale.Places} decimal places.");
        }

        CompanyId = companyId;
        GlobalSequence = globalSequence;
        EntryDate = entryDate;
        Kind = kind;
        Direction = direction;
        ItemId = itemId;
        GodownId = godownId;

        // Quantities persist as scaled 64-bit integers for exactly the reason money does
        // (ADR-0003): SQLite stores decimals as TEXT, and SUM() over TEXT aggregates in IEEE-754
        // double. Every on-hand figure and aging bucket is an exact integer sum instead.
        QuantityInMinor = direction == StockMovementDirection.Increase ? MoneyScale.ToMinor(rounded) : 0L;
        QuantityOutMinor = direction == StockMovementDirection.Decrease ? MoneyScale.ToMinor(rounded) : 0L;
        UnitCostMinor = unitCostMinor;
        ValueMinor = ComputeValueMinor(rounded, unitCostMinor);
        StockVoucherId = stockVoucherId;
        StockVoucherLineId = stockVoucherLineId;
        BatchNumber = string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber.Trim();
    }

    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Company-scoped posting order, assigned by the ledger service inside the posting transaction.
    /// Valuation replays entries in this order, so an issue is costed against exactly the state it
    /// saw — including within one voucher that both issues and receives the same item. Single-user
    /// through Phase 8, like the voucher-number allocator's read-modify-write; Phase 9 hardens it.
    /// </summary>
    public long GlobalSequence { get; private set; }

    /// <summary>The business date of the movement.</summary>
    public DateOnly EntryDate { get; private set; }

    public StockMovementKind Kind { get; private set; }

    public StockMovementDirection Direction { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid GodownId { get; private set; }

    /// <summary>Positive quantity on an increase row, in minor units of 1/10,000; zero otherwise.</summary>
    public long QuantityInMinor { get; private set; }

    /// <summary>Positive quantity on a decrease row, in minor units of 1/10,000; zero otherwise.</summary>
    public long QuantityOutMinor { get; private set; }

    /// <summary>Base units received, as a domain decimal. Computed, never a column (ADR-0003).</summary>
    public decimal QuantityIn => MoneyScale.FromMinor(QuantityInMinor);

    /// <summary>Base units issued, as a domain decimal. Computed, never a column (ADR-0003).</summary>
    public decimal QuantityOut => MoneyScale.FromMinor(QuantityOutMinor);

    /// <summary>Cost of one base unit, in minor units of 1/10,000 (ADR-0003).</summary>
    public long UnitCostMinor { get; private set; }

    /// <summary>
    /// Quantity × unit cost in minor units — computed once, here, with explicit rounding, so every
    /// report totals exact integers and the C2.1 reconciliation holds without tolerance.
    /// </summary>
    public long ValueMinor { get; private set; }

    public Guid StockVoucherId { get; private set; }

    public Guid StockVoucherLineId { get; private set; }

    public string? BatchNumber { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AggregateType => nameof(StockVoucher);

    public string AggregateId => StockVoucherId.ToString();

    /// <summary>The signed movement: positive into stock, negative out of it.</summary>
    public decimal SignedQuantity =>
        Direction == StockMovementDirection.Increase ? QuantityIn : -QuantityOut;
    /// <summary>
    /// The exact value of a movement, in minor units. Quantities carry up to four decimals and
    /// costs four, so the product can carry eight; the excess is rounded once, explicitly,
    /// banker's-rounding, at the single point where value is born — never re-derived downstream.
    /// </summary>
    public static long ComputeValueMinor(decimal quantity, long unitCostMinor)
    {
        // Decimal arithmetic throughout: quantity × cost-in-minor-units is exact until this one
        // rounding, so no floating point ever touches a stock value (ADR-0003).
        var scaled = quantity * unitCostMinor;
        var rounded = Math.Round(scaled, 0, MidpointRounding.ToEven);

        if (rounded < long.MinValue || rounded > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "The movement's value falls outside the representable minor-unit range.");
        }

        return (long)rounded;
    }

    /// <summary>
    /// Derives one base-unit cost from a total value, both already in minor units of 1/10,000,
    /// rounding once to scale.
    /// </summary>
    /// <remarks>
    /// The inverse view of <see cref="ComputeValueMinor"/>: what one unit must have cost for the
    /// lot to be worth <paramref name="valueMinor"/>. Used by valuation and by transfers, which
    /// must land their receipt side at exactly the value their issue side took out. No rescaling
    /// happens here — value and cost live in the same minor-unit space, so the division is direct
    /// and exact until the single rounding.
    /// </summary>
    public static long UnitCostFromValue(long valueMinor, decimal quantity)
    {
        if (quantity <= 0m)
        {
            return 0L;
        }

        var rounded = Math.Round(valueMinor / quantity, 0, MidpointRounding.ToEven);

        return rounded >= long.MaxValue ? long.MaxValue : (long)rounded;
    }

    private static StockMovementDirection KindDirection(StockMovementKind kind) => kind switch
    {
        StockMovementKind.OpeningBalance
            or StockMovementKind.Receipt
            or StockMovementKind.TransferIn
            or StockMovementKind.Purchase
            or StockMovementKind.SaleCancellation => StockMovementDirection.Increase,
        StockMovementKind.Issue
            or StockMovementKind.TransferOut
            or StockMovementKind.Sale
            or StockMovementKind.PurchaseCancellation => StockMovementDirection.Decrease,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown movement kind."),
    };
}
