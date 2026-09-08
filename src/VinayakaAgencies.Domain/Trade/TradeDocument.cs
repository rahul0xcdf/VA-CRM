using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Trade;

/// <summary>
/// A sales or purchase pipeline document: quotation, order, challan, invoice and their purchase
/// mirrors (phase-3-sales-purchase-pipeline.md §1, §2).
/// </summary>
/// <remarks>
/// The aggregate owns its lines and enforces the kind's shape — which statuses a kind may hold,
/// whether lines carry godowns, and when the document may still be edited. What needs the database
/// layers on: numbering, conversion links, and for invoices the atomic posting of journal voucher
/// plus stock ledger rows (checkpoint C3.3), which is the posting service's job.
/// <para>
/// Conversion carries data forward without re-keying: the successor stores where it came from
/// (<see cref="ConvertedFromDocumentId"/>, denormalised with its number so lists never join to show
/// provenance). Drill-down runs both ways off that one indexed column.
/// </para>
/// </remarks>
public sealed class TradeDocument : Entity, IAuditStamped
{
    private readonly List<TradeDocumentLine> _lines = [];

    private TradeDocument()
    {
        // EF Core materialisation.
    }

    public TradeDocument(
        Guid companyId,
        TradeDocumentKind kind,
        DateOnly documentDate,
        Guid partyLedgerId,
        string? narration)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "Unknown trade document kind.");
        }

        if (partyLedgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A trade document must name its party ledger.", nameof(partyLedgerId));
        }

        CompanyId = companyId;
        Kind = kind;
        DocumentDate = documentDate;
        PartyLedgerId = partyLedgerId;
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();
        Status = InitialStatus(kind);
    }

    public Guid CompanyId { get; private set; }

    public TradeDocumentKind Kind { get; private set; }

    /// <summary>The business date of the document (Indian FY logic keys off it).</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>
    /// The customer's ledger on the sales side, the supplier's on the purchase side. Phase 4 adds
    /// the customer master; until then parties are ordinary ledgers under Sundry Debtors /
    /// Sundry Creditors, which keeps every downstream report working unchanged.
    /// </summary>
    public Guid PartyLedgerId { get; private set; }

    public string? Narration { get; private set; }

    /// <summary>The buyer's / supplier's own reference ("your order no."), printed as received.</summary>
    public string? PartyReferenceNumber { get; private set; }

    public DateOnly? PartyReferenceDate { get; private set; }

    /// <summary>
    /// This document's number in its kind's series, claimed at creation inside the caller's
    /// transaction. Quotations and orders are numbered exactly like invoices — an operator refers to
    /// them by number long before any of them posts.
    /// </summary>
    public string? Number { get; private set; }

    /// <summary>The document this one was converted from; null for documents raised from scratch.</summary>
    public Guid? ConvertedFromDocumentId { get; private set; }

    /// <summary>Carried denormalised so provenance shows without a join.</summary>
    public string? ConvertedFromNumber { get; private set; }

    /// <summary>
    /// The posted accounting voucher this invoice produced, set once by the posting service.
    /// Only <see cref="TradeDocumentKind.TaxInvoice"/> and
    /// <see cref="TradeDocumentKind.PurchaseInvoice"/> ever post.
    /// </summary>
    public Guid? PostedVoucherId { get; private set; }

    /// <summary>The posted voucher's business number, carried for the same reason as on stock vouchers.</summary>
    public string? PostedNumber { get; private set; }

    /// <summary>
    /// The party's GSTIN as keyed on this document, when it differs from (or exists without) a
    /// customer master row — the purchase side has no supplier master, so its supplier GSTIN can
    /// only live here. Drives the B2B vs B2C split and GSTR-2B matching.
    /// </summary>
    public string? PartyGstin { get; private set; }

    /// <summary>
    /// GST state code of the place of supply, snapshotted at posting time by the tax computation
    /// step (phase-5-gst-returns.md §2): resolution order is this document, then the customer
    /// master, then the company's own state. Null on documents that never posted.
    /// </summary>
    public string? PlaceOfSupplyStateCode { get; private set; }

    /// <summary>
    /// Whether supplies under this invoice are liable to tax on a reverse-charge basis — the
    /// recipient self-assesses instead of being billed. Snapshotted at posting time alongside the
    /// place of supply.
    /// </summary>
    public bool IsReverseCharge { get; private set; }

    public TradeDocumentStatus Status { get; private set; }

    public IReadOnlyList<TradeDocumentLine> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Sales side: quotation through tax invoice. Purchase side otherwise.</summary>
    public bool SalesSide => IsSalesSide(Kind);

    /// <summary>Whether posting this kind writes both a journal voucher and stock movements.</summary>
    public bool StockMoving => IsStockMoving(Kind);

    /// <summary>Sales side: quotation through tax invoice. Purchase side otherwise.</summary>
    public static bool IsSalesSide(TradeDocumentKind kind) =>
        kind is TradeDocumentKind.Quotation
            or TradeDocumentKind.SalesOrder
            or TradeDocumentKind.DeliveryChallan
            or TradeDocumentKind.TaxInvoice;

    /// <summary>Whether posting this kind writes both a journal voucher and stock movements.</summary>
    public static bool IsStockMoving(TradeDocumentKind kind) =>
        kind is TradeDocumentKind.TaxInvoice or TradeDocumentKind.PurchaseInvoice;

    /// <summary>Whether every line of this kind must name the godown it moves quantity through.</summary>
    public static bool RequiresGodownPerLine(TradeDocumentKind kind) =>
        kind is TradeDocumentKind.DeliveryChallan
            or TradeDocumentKind.GoodsReceipt
            or TradeDocumentKind.TaxInvoice
            or TradeDocumentKind.PurchaseInvoice;

    /// <summary>A draft or open document may still be edited; nothing else may.</summary>
    public bool CanEdit => Status is TradeDocumentStatus.Draft or TradeDocumentStatus.Open;

    /// <summary>
    /// Adds one line under the kind's godown rule. Serial-tracked items additionally demand whole
    /// quantities — enforced here because a fractional serial-tracked line can never be traced to
    /// individual units at posting time.
    /// </summary>
    public TradeDocumentLine AddLine(
        Guid itemId,
        Guid? godownId,
        decimal quantity,
        decimal rate,
        decimal discountPercent,
        bool isSerialTracked,
        string? batchNumber = null,
        IReadOnlyList<string>? serialNumbers = null,
        long? appliedRateMinor = null,
        bool isPriceOverridden = false,
        SalePriceSource priceSource = SalePriceSource.Manual)
    {
        if (RequiresGodownPerLine(Kind) && !godownId.HasValue)
        {
            throw new InvalidOperationException(
                $"Every line of a {Kind} must name the godown it moves through.");
        }

        if (!RequiresGodownPerLine(Kind) && godownId.HasValue)
        {
            throw new InvalidOperationException(
                $"A {Kind} moves no stock, so its lines carry no godown.");
        }

        if (isSerialTracked && quantity != decimal.Truncate(quantity))
        {
            throw new InvalidOperationException(
                "'This item is serial-tracked: its lines carry whole units only.");
        }

        var line = new TradeDocumentLine(
            CompanyId,
            Id,
            _lines.Count + 1,
            itemId,
            godownId,
            quantity,
            rate,
            discountPercent,
            batchNumber,
            serialNumbers,
            appliedRateMinor,
            isPriceOverridden,
            priceSource);

        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Replaces the whole line set of an editable document. Callers remove the previous line rows
    /// through their repository first — deletion is explicit, never a silent orphan sweep — then
    /// hand the replacement set here, which renumbers from 1.
    /// </summary>
    public void ResetLines(
        IEnumerable<TradeLineInput> lines)
    {
        EnsureEditable("revised");

        _lines.Clear();

        foreach (var line in lines)
        {
            AddLine(
                line.ItemId,
                line.GodownId,
                line.Quantity,
                line.Rate,
                line.DiscountPercent,
                line.IsSerialTracked,
                line.BatchNumber,
                line.SerialNumbers,
                line.AppliedRateMinor,
                line.IsPriceOverridden,
                line.PriceSource);
        }
    }

    /// <summary>Sets the buyer's/supplier's own reference, printed back to them.</summary>
    public void SetPartyReference(string? referenceNumber, DateOnly? referenceDate)
    {
        PartyReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber)
            ? null
            : referenceNumber.Trim();
        PartyReferenceDate = PartyReferenceNumber is null ? null : referenceDate;
    }

    /// <summary>
    /// Sets the party's GSTIN (null clears it). Stored upper-case, exactly as the customer master
    /// and the GSTN portal expect.
    /// </summary>
    public void SetPartyGstin(string? gstin) =>
        PartyGstin = string.IsNullOrWhiteSpace(gstin) ? null : gstin.Trim().ToUpperInvariant();

    /// <summary>
    /// Flags this document as a reverse-charge supply while it is still editable: the recipient
    /// self-assesses instead of being billed (phase-5-gst-returns.md §4a). The posted snapshot
    /// keeps whatever was flagged here.
    /// </summary>
    public void SetReverseCharge(bool isReverseCharge)
    {
        if (Status == TradeDocumentStatus.Issued)
        {
            throw new InvalidOperationException(
                "A posted invoice's reverse-charge flag is part of its tax snapshot and cannot change.");
        }

        IsReverseCharge = isReverseCharge;
    }

    /// <summary>
    /// Records the tax snapshot the posting-time computation resolved: where the supply was made,
    /// and whether it is reverse-charge. Only a posting invoice carries one, and it is written
    /// once — returns are built from what was snapshotted, never recomputed ad hoc.
    /// </summary>
    public void AttachGstSnapshot(string placeOfSupplyStateCode, bool isReverseCharge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeOfSupplyStateCode);

        var trimmed = placeOfSupplyStateCode.Trim();

        if (trimmed.Length != 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(placeOfSupplyStateCode),
                placeOfSupplyStateCode,
                "A place-of-supply state code is exactly two characters (e.g. \"29\").");
        }

        if (!StockMoving)
        {
            throw new InvalidOperationException($"A {Kind} never posts, so it carries no GST snapshot.");
        }

        PlaceOfSupplyStateCode = trimmed;
        IsReverseCharge = isReverseCharge;
    }

    /// <summary>Records the number this document was given in its kind's series.</summary>
    public void AttachNumber(string number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        if (Number is not null)
        {
            throw new InvalidOperationException(
                $"This {Kind} has already been numbered ({Number}).");
        }

        Number = number.Trim();
    }

    /// <summary>Moves a still-editable document to another business date.</summary>
    public void SetDocumentDate(DateOnly date) => DocumentDate = date;

    /// <summary>Re-points a still-editable document at another party ledger.</summary>
    public void SetParty(Guid partyLedgerId)
    {
        if (partyLedgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A trade document must name its party ledger.", nameof(partyLedgerId));
        }

        PartyLedgerId = partyLedgerId;
    }

    public void SetNarration(string? narration) =>
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();

    /// <summary>
    /// Marks this document fully carried into its successor(s). The conversion links themselves
    /// live on the successors; a partially converted order stays open until its last line is
    /// carried forward.
    /// </summary>
    public void MarkConverted()
    {
        if (Status is TradeDocumentStatus.Converted or TradeDocumentStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"This {Kind} is already {Status} and cannot be converted again.");
        }

        Status = TradeDocumentStatus.Converted;
    }

    /// <summary>An order whose ordered quantity is fully delivered or received.</summary>
    public void MarkFulfilled()
    {
        if (Status != TradeDocumentStatus.Open)
        {
            throw new InvalidOperationException(
                $"Only an open order can be marked fulfilled; this one is {Status}.");
        }

        Status = TradeDocumentStatus.Fulfilled;
    }

    /// <summary>
    /// Puts a fulfilled order back in play after a reversal freed its balance — a posted invoice
    /// cancelled, so the pending quantity must show on the outstanding report again.
    /// </summary>
    public void Reopen()
    {
        if (Status != TradeDocumentStatus.Fulfilled)
        {
            throw new InvalidOperationException(
                $"Only a fulfilled order can reopen; this one is {Status}.");
        }

        Status = TradeDocumentStatus.Open;
    }

    /// <summary>
    /// Withdraws a document that has had no effect: never a posted invoice, whose cancellation goes
    /// through the journal engine and reverses its stock instead.
    /// </summary>
    public void MarkCancelled()
    {
        if (Status == TradeDocumentStatus.Issued)
        {
            throw new InvalidOperationException(
                "A posted invoice cannot simply be cancelled: reverse it through the journal "
                + "engine so its ledger and stock effects are undone atomically.");
        }

        if (Status == TradeDocumentStatus.Cancelled)
        {
            throw new InvalidOperationException("This document is already cancelled.");
        }

        Status = TradeDocumentStatus.Cancelled;
    }

    /// <summary>Records the linked accounting voucher after a successful post, marking it issued.</summary>
    public void AttachPostedVoucher(Guid voucherId, string number)
    {
        if (voucherId == Guid.Empty)
        {
            throw new ArgumentException("VoucherId must not be empty.", nameof(voucherId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        if (!StockMoving)
        {
            throw new InvalidOperationException(
                $"A {Kind} never posts to the books; only invoices do.");
        }

        if (PostedVoucherId.HasValue)
        {
            throw new InvalidOperationException(
                "This invoice has already been posted to the books.");
        }

        PostedVoucherId = voucherId;
        PostedNumber = number.Trim();
        Status = TradeDocumentStatus.Issued;
    }

    /// <summary>Marks the invoice cancelled after the journal engine reversed it.</summary>
    public void MarkIssuedCancelled()
    {
        if (Status != TradeDocumentStatus.Issued)
        {
            throw new InvalidOperationException(
                $"Only an issued invoice can be reversed; this one is {Status}.");
        }

        Status = TradeDocumentStatus.Cancelled;
    }

    /// <summary>Records the conversion link on the successor side.</summary>
    public void SetConvertedFrom(Guid sourceDocumentId, string sourceNumber)
    {
        if (sourceDocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "SourceDocumentId must not be empty.", nameof(sourceDocumentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNumber);

        if (ConvertedFromDocumentId.HasValue)
        {
            throw new InvalidOperationException(
                "This document already records what it was converted from.");
        }

        ConvertedFromDocumentId = sourceDocumentId;
        ConvertedFromNumber = sourceNumber.Trim();
    }

    private void EnsureEditable(string action)
    {
        if (!CanEdit)
        {
            throw new InvalidOperationException(
                $"A {Kind} in status {Status} cannot be {action}. Only drafts and open documents may change.");
        }
    }

    private static TradeDocumentStatus InitialStatus(TradeDocumentKind kind) => kind switch
    {
        // Estimates stay drafts until something converts them; invoices stay drafts until they post.
        TradeDocumentKind.Quotation
            or TradeDocumentKind.TaxInvoice
            or TradeDocumentKind.PurchaseInvoice => TradeDocumentStatus.Draft,

        // Orders, challans and goods receipts are live from birth.
        _ => TradeDocumentStatus.Open,
    };
}
