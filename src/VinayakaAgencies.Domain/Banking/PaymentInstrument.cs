using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Banking;

/// <summary>
/// How a payment or receipt voucher actually settled: the cheque, the UTR, or the UPI reference
/// (phase-7-tds-payroll-banking.md §2 — "e-payment / NEFT / RTGS / UPI reference fields on payment
/// vouchers").
/// </summary>
/// <remarks>
/// <para>
/// <b>A row of its own, not five nullable columns on the voucher.</b> Most vouchers in a trading
/// company's books are sales, purchases and journals, and none of them settle through an
/// instrument. Widening the revision table — the one every report reads through — with fields
/// meaningful to a minority of rows would cost every query for the benefit of a few.
/// </para>
/// <para>
/// <b>It hangs off the voucher, not the revision.</b> A cheque number is a fact about the physical
/// instrument, not about a version of the entry: correcting a mistyped cheque number should not
/// require altering the voucher and superseding its journal lines. Because the entity is
/// <see cref="IAuditStamped"/>, changing it is edit-logged with before and after values like any
/// other master change, so non-negotiable #3 holds without ADR-0002's revision machinery.
/// </para>
/// <para>
/// <b>What is required depends on the mode, and the entity enforces it.</b> A cheque with no number
/// cannot be printed or reconciled; a bank transfer with no reference cannot be traced back to the
/// statement. Cash carries neither and must not be made to invent one. Validating here rather than
/// in the screen is what makes the rule testable without a window (ADR-0007).
/// </para>
/// </remarks>
public sealed class PaymentInstrument : Entity, IAuditStamped, IBelongsToAggregate
{
    /// <summary>A UTR is 16 characters; a UPI transaction id runs longer. Sized for both.</summary>
    public const int ReferenceLength = 64;

    private PaymentInstrument()
    {
        // EF Core materialisation.
    }

    public PaymentInstrument(
        Guid companyId,
        Guid voucherId,
        Guid bankLedgerId,
        PaymentMode mode,
        DateOnly instrumentDate,
        string? instrumentNumber = null,
        string? bankReference = null,
        string? payeeName = null,
        string? remarks = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (voucherId == Guid.Empty)
        {
            throw new ArgumentException(
                "A payment instrument must name the voucher it settled.", nameof(voucherId));
        }

        if (bankLedgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A payment instrument must name the bank or cash ledger it moved through.",
                nameof(bankLedgerId));
        }

        CompanyId = companyId;
        VoucherId = voucherId;
        BankLedgerId = bankLedgerId;
        InstrumentDate = instrumentDate;

        // Through the setter, so the mode-dependent rules below apply on creation as well as on a
        // later edit. A constructor that skips its own validation is the usual way an invariant
        // ends up holding for edits and not for inserts.
        SetMode(mode, instrumentNumber, bankReference);

        SetPayeeName(payeeName);
        SetRemarks(remarks);
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The voucher that settled, and the aggregate this row is audited under.</summary>
    public Guid VoucherId { get; private set; }

    /// <summary>The bank or cash ledger the money moved through.</summary>
    public Guid BankLedgerId { get; private set; }

    public PaymentMode Mode { get; private set; }

    /// <summary>
    /// The cheque or demand-draft number. Null on every electronic mode, where the bank's own
    /// reference is the identifier and there is no instrument to number.
    /// </summary>
    public string? InstrumentNumber { get; private set; }

    /// <summary>
    /// The date written on the instrument, which is not always the voucher date — a post-dated
    /// cheque is the ordinary case, and the difference is what bank reconciliation works from.
    /// </summary>
    public DateOnly InstrumentDate { get; private set; }

    /// <summary>
    /// The UTR, IMPS reference or UPI transaction id the bank issued. Recorded, never generated:
    /// a reference the software invented would reconcile against nothing.
    /// </summary>
    public string? BankReference { get; private set; }

    /// <summary>
    /// Who the cheque is drawn in favour of, as it should be printed. Defaults from the party
    /// ledger at entry but is editable, because the payee line on a cheque is sometimes not the
    /// ledger name.
    /// </summary>
    public string? PayeeName { get; private set; }

    public string? Remarks { get; private set; }

    /// <summary>
    /// True once the cheque has been printed, so a re-print is a deliberate act rather than an
    /// accident of pressing the key twice.
    /// </summary>
    public bool IsPrinted { get; private set; }

    public DateTimeOffset? PrintedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>True for the one mode that has a printed layout (§4a D4).</summary>
    public bool IsPrintable => Mode == PaymentMode.Cheque;

    /// <summary>The voucher, so an edit of this row files under the entry it belongs to (§11.1).</summary>
    string IBelongsToAggregate.AggregateType => nameof(Accounting.Voucher);

    /// <inheritdoc />
    string IBelongsToAggregate.AggregateId => VoucherId.ToString("D");

    /// <summary>
    /// Sets the mode together with the identifiers that mode requires.
    /// </summary>
    /// <remarks>
    /// One method rather than three setters, because the three values are only valid as a set: a
    /// mode changed from cheque to NEFT while a stale cheque number stays behind would print and
    /// reconcile against an instrument that was never written.
    /// </remarks>
    public void SetMode(PaymentMode mode, string? instrumentNumber, string? bankReference)
    {
        var number = Normalise(instrumentNumber);
        var reference = Normalise(bankReference);

        switch (mode)
        {
            case PaymentMode.Cheque:
            case PaymentMode.DemandDraft:
                if (number is null)
                {
                    throw new ArgumentException(
                        $"A {mode} needs its instrument number — it is what the bank, the "
                        + "reconciliation and the printed cheque all identify it by.",
                        nameof(instrumentNumber));
                }

                break;

            case PaymentMode.Neft:
            case PaymentMode.Rtgs:
            case PaymentMode.Imps:
            case PaymentMode.Upi:
            case PaymentMode.Card:
            case PaymentMode.BankTransfer:
                if (reference is null)
                {
                    throw new ArgumentException(
                        $"A {mode} payment needs the reference the bank issued (UTR, transaction "
                        + "id). Without it the entry cannot be traced to the statement.",
                        nameof(bankReference));
                }

                // An electronic transfer has no instrument to number. Dropping a stale one rather
                // than refusing it keeps a mode change from becoming a re-keying exercise.
                number = null;
                break;

            case PaymentMode.Cash:
                number = null;
                reference = null;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown payment mode.");
        }

        if (number is not null && number.Length > ReferenceLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instrumentNumber),
                instrumentNumber,
                $"An instrument number is at most {ReferenceLength} characters.");
        }

        if (reference is not null && reference.Length > ReferenceLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bankReference),
                bankReference,
                $"A bank reference is at most {ReferenceLength} characters.");
        }

        Mode = mode;
        InstrumentNumber = number;
        BankReference = reference;
    }

    public void SetInstrumentDate(DateOnly instrumentDate) => InstrumentDate = instrumentDate;

    public void SetPayeeName(string? payeeName) => PayeeName = Normalise(payeeName);

    public void SetRemarks(string? remarks) => Remarks = Normalise(remarks);

    /// <summary>
    /// Records that the cheque has been printed.
    /// </summary>
    /// <remarks>
    /// Refused for a mode that has nothing to print, rather than silently accepted: a NEFT payment
    /// marked printed would misreport the cheque register, and the register is what tells the owner
    /// which cheque leaves have been used.
    /// </remarks>
    public void MarkPrinted(DateTimeOffset printedAtUtc)
    {
        if (!IsPrintable)
        {
            throw new InvalidOperationException(
                $"A {Mode} payment has no cheque to print.");
        }

        IsPrinted = true;
        PrintedAtUtc = printedAtUtc;
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
