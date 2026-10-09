using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Inventory;

/// <summary>
/// A manual inventory document: an opening balance, a receipt, an issue or an inter-godown
/// transfer (phase-2-inventory.md §1, §2).
/// </summary>
/// <remarks>
/// The document owns its lines and enforces the purpose's shape — a receipt is all increases, an
/// issue all decreases, and a transfer balances item by item. The posting service layers on
/// everything that needs the database: serial availability (C2.3), valuation of the issue side,
/// the negative-stock warning, the linked accounting voucher and the ledger rows themselves.
/// Vouchers are never altered or deleted once posted; a mistake is corrected by a reversing
/// voucher, so the stock ledger stays as complete as the journal.
/// </remarks>
public sealed class StockVoucher : Entity, IAuditStamped
{
    private readonly List<StockVoucherLine> _lines = [];

    private StockVoucher()
    {
        // EF Core materialisation.
    }

    public StockVoucher(
        Guid companyId,
        StockVoucherPurpose purpose,
        DateOnly voucherDate,
        string narration)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        Purpose = purpose;
        VoucherDate = voucherDate;
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();
    }

    public Guid CompanyId { get; private set; }

    public StockVoucherPurpose Purpose { get; private set; }

    /// <summary>The business date the movement happened on (Indian FY logic keys off it).</summary>
    public DateOnly VoucherDate { get; private set; }

    /// <summary>
    /// The ledger the non-stock leg of the accounting effect posts to. Required for every purpose
    /// except <see cref="StockVoucherPurpose.Transfer"/>, whose effect nets to zero on the control
    /// account by construction (C2.5).
    /// </summary>
    public Guid? CounterLedgerId { get; private set; }

    public string? Narration { get; private set; }

    /// <summary>
    /// The posted accounting voucher this movement produced, set once by the posting service.
    /// Every stock movement reaches the books through the journal engine, never around it.
    /// </summary>
    public Guid? PostedVoucherId { get; private set; }

    /// <summary>
    /// The posted voucher's business number, carried denormalised so movement lists never have to
    /// join into the accounting tables to show what the operator filed it under.
    /// </summary>
    public string? PostedNumber { get; private set; }

    public IReadOnlyList<StockVoucherLine> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>
    /// Adds one line, enforcing the direction the purpose allows and carrying the item's tracking
    /// flags so receipt-side batch/serial rules hold at construction.
    /// </summary>
    public StockVoucherLine AddLine(
        StockMovementDirection direction,
        Guid itemId,
        Guid godownId,
        decimal quantity,
        bool isSerialTracked,
        bool isBatchTracked,
        decimal unitCost = 0m,
        string? batchNumber = null)
    {
        var expectedDirection = Purpose switch
        {
            StockVoucherPurpose.OpeningBalance or StockVoucherPurpose.Receipt
                => StockMovementDirection.Increase,
            StockVoucherPurpose.Issue => StockMovementDirection.Decrease,
            _ => direction,
        };

        if (direction != expectedDirection && Purpose != StockVoucherPurpose.Transfer)
        {
            throw new InvalidOperationException(
                $"A {Purpose} voucher only carries {expectedDirection} lines.");
        }

        var line = new StockVoucherLine(
            CompanyId,
            Id,
            _lines.Count + 1,
            direction,
            itemId,
            godownId,
            quantity,
            isSerialTracked,
            isBatchTracked,
            unitCost,
            batchNumber);

        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Sets the counter ledger for the non-stock leg; refused on transfers, which post none.
    /// </summary>
    public void SetCounterLedger(Guid? counterLedgerId)
    {
        if (Purpose == StockVoucherPurpose.Transfer)
        {
            if (counterLedgerId.HasValue)
            {
                throw new InvalidOperationException(
                    "A transfer has no non-stock leg: it moves value between godowns without "
                    + "touching any other account (checkpoint C2.5).");
            }

            return;
        }

        if (!counterLedgerId.HasValue || counterLedgerId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"A {Purpose} voucher must name the ledger its value comes from or goes to.");
        }

        CounterLedgerId = counterLedgerId;
    }

    /// <summary>
    /// Transfer-shape check: for every item, total in equals total out. Without it a half-entered
    /// transfer could silently create or destroy stock.
    /// </summary>
    public void ValidateTransferBalance()
    {
        if (Purpose != StockVoucherPurpose.Transfer)
        {
            return;
        }

        var netByItem = new Dictionary<Guid, decimal>();

        foreach (var line in _lines)
        {
            var signed = line.Direction == StockMovementDirection.Increase
                ? line.Quantity
                : -line.Quantity;

            netByItem[line.ItemId] = netByItem.TryGetValue(line.ItemId, out var current)
                ? current + signed
                : signed;
        }

        var unbalanced = netByItem
            .Where(pair => pair.Value != 0m)
            .Select(pair => pair.Key)
            .ToList();

        if (unbalanced.Count > 0)
        {
            throw new InvalidOperationException(
                "A transfer must move equal quantities in and out for every item; item(s) "
                + string.Join(", ", unbalanced) + " do not balance.");
        }
    }

    /// <summary>Records the linked accounting voucher after a successful post.</summary>
    public void AttachPostedVoucher(Guid voucherId, string number)
    {
        if (voucherId == Guid.Empty)
        {
            throw new ArgumentException("VoucherId must not be empty.", nameof(voucherId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        if (PostedVoucherId.HasValue)
        {
            throw new InvalidOperationException(
                "This stock voucher has already been posted to the books.");
        }

        PostedVoucherId = voucherId;
        PostedNumber = number.Trim();
    }
}
