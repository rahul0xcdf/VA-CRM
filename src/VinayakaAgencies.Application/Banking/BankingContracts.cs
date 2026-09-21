using VinayakaAgencies.Domain.Banking;

namespace VinayakaAgencies.Application.Banking;

/// <summary>
/// Posts a payment or receipt and records how it settled
/// (phase-7-tds-payroll-banking.md §2 — cheque printing and e-payment reference fields).
/// </summary>
/// <remarks>
/// <para>
/// The two amounts are one: a payment debits the party and credits the bank, a receipt does the
/// reverse, and the service derives the pair from <see cref="IsReceipt"/> rather than making the
/// caller hand-build journal lines. That is the same reasoning the deducting voucher uses — the
/// operator states the business fact and the service states the accounting.
/// </para>
/// <para>
/// <see cref="BankLedgerId"/> is the ledger the money moves through; for a cash payment it is the
/// cash ledger, and the mode carries no instrument.
/// </para>
/// </remarks>
public sealed record PostBankVoucherRequest(
    Guid VoucherTypeId,
    DateOnly VoucherDate,
    Guid PartyLedgerId,
    Guid BankLedgerId,
    decimal Amount,
    PaymentMode Mode,
    DateOnly? InstrumentDate = null,
    string? InstrumentNumber = null,
    string? BankReference = null,
    string? PayeeName = null,
    string? Narration = null,
    string? Remarks = null,
    bool IsReceipt = false,
    string? ManualNumber = null);

/// <summary>What settled, and how — the row the cheque register and reconciliation read.</summary>
public sealed record PaymentInstrumentSummary(
    Guid Id,
    Guid VoucherId,
    string VoucherNumber,
    DateOnly VoucherDate,
    Guid BankLedgerId,
    string BankLedgerName,
    Guid PartyLedgerId,
    string PartyLedgerName,
    decimal Amount,
    PaymentMode Mode,
    string? InstrumentNumber,
    DateOnly InstrumentDate,
    string? BankReference,
    string? PayeeName,
    string? Remarks,
    bool IsPrinted,
    DateTimeOffset? PrintedAtUtc)
{
    /// <summary>True for the one mode that has a printed layout.</summary>
    public bool IsPrintable => Mode == PaymentMode.Cheque;
}

/// <summary>Everything a cheque layout can place, resolved for one instrument.</summary>
/// <remarks>
/// A flat record rather than the entity, because the renderer lives in the presentation layer and
/// must not reach into the domain. <see cref="AmountInWords"/> is produced here, once, so the
/// printed cheque and any other rendering of the same payment cannot word it differently.
/// </remarks>
public sealed record ChequePrintData(
    string PayeeName,
    DateOnly InstrumentDate,
    decimal Amount,
    string AmountInWords,
    string AmountInFigures,
    string? ChequeNumber,
    string BankLedgerName,
    string? Remarks);

/// <summary>
/// Payment and receipt settlement: the instrument on a voucher, the cheque register, and the data
/// a cheque layout prints (phase-7-tds-payroll-banking.md §2, checkpoint C7.8).
/// </summary>
public interface IBankingService
{
    /// <summary>
    /// Posts the voucher through the journal engine and attaches its instrument, atomically.
    /// </summary>
    Task<PaymentInstrumentSummary> PostAsync(
        PostBankVoucherRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>The cheque register / payment list for a period, newest first.</summary>
    Task<IReadOnlyList<PaymentInstrumentSummary>> ListAsync(
        DateOnly fromDate,
        DateOnly toDate,
        Guid? bankLedgerId = null,
        PaymentMode? mode = null,
        CancellationToken cancellationToken = default);

    /// <summary>Corrects how a posted voucher settled, without superseding its journal lines.</summary>
    Task<PaymentInstrumentSummary> UpdateInstrumentAsync(
        Guid instrumentId,
        PaymentMode mode,
        DateOnly instrumentDate,
        string? instrumentNumber,
        string? bankReference,
        string? payeeName,
        string? remarks,
        CancellationToken cancellationToken = default);

    /// <summary>The values a cheque layout places, or null where the voucher has no cheque.</summary>
    Task<ChequePrintData?> GetChequeDataAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>Records that the cheque was printed, so a re-print is deliberate.</summary>
    Task<PaymentInstrumentSummary> MarkPrintedAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default);
}
