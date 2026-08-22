using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// The warranty-traceable identity of one physical unit of a serial-tracked item
/// (phase-2-inventory.md §2).
/// </summary>
/// <remarks>
/// A serial exists exactly once: the unique <c>(CompanyId, ItemId, ValueNormalised)</c> index holds
/// for the row's whole life, and the status state machine guarantees a serial can be in stock only
/// once — so selling the same serial twice is refused here before any ledger row is written
/// (checkpoints C2.2, C2.3). The row is append-only in spirit: it is created on first receipt and
/// never deleted; lifecycle moves are edits to two or three columns, each captured by the edit log.
/// </remarks>
public sealed class SerialNumber : Entity, ICreationStamped
{
    private SerialNumber()
    {
        // EF Core materialisation.
    }

    /// <summary>
    /// Creates an in-stock serial at the moment its receipt is being posted. The receipt line link
    /// is what ties the serial back to the document that brought it in.
    /// </summary>
    public SerialNumber(
        Guid companyId,
        Guid itemId,
        string value,
        Guid godownId,
        Guid receiptLineId,
        DateOnly receivedOn)
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

        if (receiptLineId == Guid.Empty)
        {
            throw new ArgumentException(
                "A serial must be linked to the stock voucher line that received it.",
                nameof(receiptLineId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        CompanyId = companyId;
        ItemId = itemId;
        SetValue(value);
        CurrentGodownId = godownId;
        ReceiptLineId = receiptLineId;
        ReceivedOn = receivedOn;
        Status = SerialNumberStatus.InStock;
    }

    public Guid CompanyId { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>The serial as the operator entered it, preserved verbatim.</summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Value"/>, over which the unique index is built, so "sn001" and
    /// "SN001" cannot both exist.
    /// </summary>
    public string ValueNormalised { get; private set; } = string.Empty;

    public SerialNumberStatus Status { get; private set; }

    public Guid CurrentGodownId { get; private set; }

    public DateOnly ReceivedOn { get; private set; }

    /// <summary>The stock voucher line that received this unit.</summary>
    public Guid ReceiptLineId { get; private set; }

    /// <summary>The stock voucher line that sold this unit; null while in stock.</summary>
    public Guid? SaleLineId { get; private set; }

    /// <summary>When the unit was sold; null while in stock.</summary>
    public DateOnly? SoldOn { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Marks the unit sold against an issue line. Refuses anything but an in-stock unit, which is
    /// checkpoint C2.3 expressed as code rather than as a procedure.
    /// </summary>
    public void MarkSold(Guid saleLineId, DateOnly soldOn)
    {
        if (saleLineId == Guid.Empty)
        {
            throw new ArgumentException(
                "A sale must be linked to the stock voucher line that issued the unit.",
                nameof(saleLineId));
        }

        if (Status != SerialNumberStatus.InStock)
        {
            throw new InvalidOperationException(
                $"Serial '{Value}' has already been sold on {SoldOn:yyyy-MM-dd} and cannot be "
                + "sold again. The same serial cannot leave stock twice (checkpoint C2.3).");
        }

        Status = SerialNumberStatus.Sold;
        SaleLineId = saleLineId;
        SoldOn = soldOn;
    }

    /// <summary>Puts a sold unit back into stock at a godown (an optional lifecycle stage).</summary>
    public void ReturnToStock(Guid godownId)
    {
        if (godownId == Guid.Empty)
        {
            throw new ArgumentException("GodownId must not be empty.", nameof(godownId));
        }

        if (Status != SerialNumberStatus.Sold)
        {
            throw new InvalidOperationException(
                $"Serial '{Value}' is in stock and cannot be returned.");
        }

        Status = SerialNumberStatus.InStock;
        CurrentGodownId = godownId;
        SaleLineId = null;
        SoldOn = null;
    }

    /// <summary>Moves an in-stock unit to another godown without changing its status.</summary>
    public void MoveTo(Guid godownId)
    {
        if (godownId == Guid.Empty)
        {
            throw new ArgumentException("GodownId must not be empty.", nameof(godownId));
        }

        if (Status != SerialNumberStatus.InStock)
        {
            throw new InvalidOperationException(
                $"Serial '{Value}' has been sold and cannot move between godowns.");
        }

        CurrentGodownId = godownId;
    }

    private void SetValue(string value)
    {
        Value = value.Trim();
        ValueNormalised = NameNormaliser.Normalise(value);
    }
}
