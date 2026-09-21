using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Banking;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Banking;

/// <summary>
/// Payment and receipt settlement (phase-7-tds-payroll-banking.md §2, checkpoint C7.8).
/// </summary>
/// <remarks>
/// <para>
/// Composes the Phase 1 journal engine like every other posting path in the product: the two
/// journal lines go in through <c>IVoucherPostingService</c>, and the instrument row is added in
/// the same transaction, so a payment either reaches the books with its cheque number or does not
/// reach them at all.
/// </para>
/// <para>
/// <b>Correcting an instrument does not supersede the voucher.</b> A mistyped cheque number is a
/// fact about the piece of paper, not about the accounting entry, and forcing an ADR-0002
/// alteration for it would write a new revision of journal lines that did not change. The
/// instrument is <c>IAuditStamped</c>, so the correction is edit-logged with before and after
/// values — which is what non-negotiable #3 actually requires.
/// </para>
/// </remarks>
public sealed class BankingService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    IVoucherPostingService voucherPostingService,
    IClock clock) : IBankingService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly IVoucherPostingService _voucherPostingService = voucherPostingService
        ?? throw new ArgumentNullException(nameof(voucherPostingService));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async Task<PaymentInstrumentSummary> PostAsync(
        PostBankVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Amount <= 0m)
        {
            throw new InvalidOperationException("A payment or receipt must be for a positive amount.");
        }

        if (request.PartyLedgerId == request.BankLedgerId)
        {
            throw new InvalidOperationException(
                "A payment cannot move money from an account to itself.");
        }

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var party = await RequireLedgerAsync(companyId, request.PartyLedgerId, cancellationToken)
            .ConfigureAwait(false);
        var bank = await RequireLedgerAsync(companyId, request.BankLedgerId, cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // A payment settles a liability: the party is debited and the bank credited. A receipt is
        // the mirror. Derived here so the operator states the business fact and never the signs.
        var lines = request.IsReceipt
            ?
            [
                new PostVoucherLine(request.BankLedgerId, request.Amount, 0m),
                new PostVoucherLine(request.PartyLedgerId, 0m, request.Amount),
            ]
            : new List<PostVoucherLine>
            {
                new(request.PartyLedgerId, request.Amount, 0m),
                new(request.BankLedgerId, 0m, request.Amount),
            };

        var posted = await _voucherPostingService.PostAsync(
                new PostVoucherRequest(
                    request.VoucherTypeId,
                    request.VoucherDate,
                    lines,
                    request.Narration,

                    // The bank's own reference doubles as the voucher reference, so the day book
                    // shows it without anyone opening the instrument.
                    ReferenceNumber: request.BankReference ?? request.InstrumentNumber,
                    ReferenceDate: request.InstrumentDate,
                    ManualNumber: request.ManualNumber),
                cancellationToken)
            .ConfigureAwait(false);

        var instrument = new PaymentInstrument(
            companyId,
            posted.VoucherId,
            request.BankLedgerId,

            // The instrument date defaults to the voucher date rather than to today: a voucher
            // entered a week late is still the cheque's own date that matters for reconciliation.
            mode: request.Mode,
            instrumentDate: request.InstrumentDate ?? request.VoucherDate,
            instrumentNumber: request.InstrumentNumber,
            bankReference: request.BankReference,
            payeeName: request.PayeeName ?? party.Name,
            remarks: request.Remarks);

        await _unitOfWork.Repository<PaymentInstrument>().AddAsync(instrument, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Summarise(instrument, posted.Number, posted.VoucherDate, request.Amount, party, bank);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PaymentInstrumentSummary>> ListAsync(
        DateOnly fromDate,
        DateOnly toDate,
        Guid? bankLedgerId = null,
        PaymentMode? mode = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var instruments = (await _unitOfWork.Repository<PaymentInstrument>()
                .ListAsync(new InstrumentsOfCompany(companyId, bankLedgerId, mode), cancellationToken)
                .ConfigureAwait(false))
            .Where(instrument => instrument.InstrumentDate >= fromDate
                && instrument.InstrumentDate <= toDate)
            .ToList();

        if (instruments.Count == 0)
        {
            return [];
        }

        return (await HydrateAsync(companyId, instruments, cancellationToken).ConfigureAwait(false))
            .OrderByDescending(summary => summary.InstrumentDate)
            .ThenBy(summary => summary.InstrumentNumber, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PaymentInstrumentSummary> UpdateInstrumentAsync(
        Guid instrumentId,
        PaymentMode mode,
        DateOnly instrumentDate,
        string? instrumentNumber,
        string? bankReference,
        string? payeeName,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var instrument = await RequireInstrumentAsync(companyId, instrumentId, cancellationToken)
            .ConfigureAwait(false);

        instrument.SetMode(mode, instrumentNumber, bankReference);
        instrument.SetInstrumentDate(instrumentDate);
        instrument.SetPayeeName(payeeName);
        instrument.SetRemarks(remarks);

        _unitOfWork.Repository<PaymentInstrument>().Update(instrument);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (await HydrateAsync(companyId, [instrument], cancellationToken).ConfigureAwait(false))
            .Single();
    }

    /// <inheritdoc />
    public async Task<ChequePrintData?> GetChequeDataAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var instrument = await RequireInstrumentAsync(companyId, instrumentId, cancellationToken)
            .ConfigureAwait(false);

        if (!instrument.IsPrintable)
        {
            return null;
        }

        var summary = (await HydrateAsync(companyId, [instrument], cancellationToken)
            .ConfigureAwait(false)).Single();

        return new ChequePrintData(
            summary.PayeeName ?? summary.PartyLedgerName,
            instrument.InstrumentDate,
            summary.Amount,

            // Worded once, here, so the figures and the words on one cheque cannot disagree — and
            // where they do disagree on a real cheque, the words are what the bank honours.
            IndianAmountInWords.ForCheque(summary.Amount),
            IndianAmountInWords.ToIndianFigures(summary.Amount),
            instrument.InstrumentNumber,
            summary.BankLedgerName,
            instrument.Remarks);
    }

    /// <inheritdoc />
    public async Task<PaymentInstrumentSummary> MarkPrintedAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var instrument = await RequireInstrumentAsync(companyId, instrumentId, cancellationToken)
            .ConfigureAwait(false);

        instrument.MarkPrinted(_clock.UtcNow.ToUniversalTime());

        _unitOfWork.Repository<PaymentInstrument>().Update(instrument);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (await HydrateAsync(companyId, [instrument], cancellationToken).ConfigureAwait(false))
            .Single();
    }

    /// <summary>
    /// Joins instruments to their vouchers and ledgers in three reads rather than per row.
    /// </summary>
    /// <remarks>
    /// The party ledger is not on the instrument — it is whichever side of the voucher is not the
    /// bank — so it is recovered from the journal lines. Deliberately not denormalised onto the
    /// instrument: the voucher already states it, and two places to state one fact is two places
    /// for them to disagree.
    /// </remarks>
    private async Task<List<PaymentInstrumentSummary>> HydrateAsync(
        Guid companyId,
        IReadOnlyCollection<PaymentInstrument> instruments,
        CancellationToken cancellationToken)
    {
        var voucherIds = instruments.Select(instrument => instrument.VoucherId).ToHashSet();

        var lines = (await _unitOfWork.Repository<JournalLine>()
                .ListAsync(new LinesOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Where(line => voucherIds.Contains(line.VoucherId))
            .ToLookup(line => line.VoucherId);

        var revisions = (await _unitOfWork.Repository<VoucherRevision>()
                .ListAsync(new CurrentRevisionsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .Where(revision => voucherIds.Contains(revision.VoucherId))
            .ToDictionary(revision => revision.VoucherId);

        var ledgers = (await _unitOfWork.Repository<Ledger>()
                .ListAsync(new LedgersOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(ledger => ledger.Id, ledger => ledger.Name);

        var summaries = new List<PaymentInstrumentSummary>(instruments.Count);

        foreach (var instrument in instruments)
        {
            var voucherLines = lines[instrument.VoucherId].ToList();

            var bankLine = voucherLines
                .FirstOrDefault(line => line.LedgerId == instrument.BankLedgerId);

            var partyLine = voucherLines
                .FirstOrDefault(line => line.LedgerId != instrument.BankLedgerId);

            revisions.TryGetValue(instrument.VoucherId, out var revision);

            summaries.Add(new PaymentInstrumentSummary(
                instrument.Id,
                instrument.VoucherId,
                revision?.Number ?? "(cancelled)",
                revision?.VoucherDate ?? instrument.InstrumentDate,
                instrument.BankLedgerId,
                Name(ledgers, instrument.BankLedgerId),
                partyLine?.LedgerId ?? Guid.Empty,
                partyLine is null ? "(unknown)" : Name(ledgers, partyLine.LedgerId),

                // The instrument's face value is whatever moved through the bank, sign discarded:
                // the register shows ₹5,000 whether it was paid out or received in, and the mode
                // and the voucher say which.
                MoneyScale.FromMinor(Math.Abs(bankLine?.AmountMinor ?? 0L)),
                instrument.Mode,
                instrument.InstrumentNumber,
                instrument.InstrumentDate,
                instrument.BankReference,
                instrument.PayeeName,
                instrument.Remarks,
                instrument.IsPrinted,
                instrument.PrintedAtUtc));
        }

        return summaries;
    }

    private static PaymentInstrumentSummary Summarise(
        PaymentInstrument instrument,
        string voucherNumber,
        DateOnly voucherDate,
        decimal amount,
        Ledger party,
        Ledger bank) =>
        new(instrument.Id,
            instrument.VoucherId,
            voucherNumber,
            voucherDate,
            bank.Id,
            bank.Name,
            party.Id,
            party.Name,
            amount,
            instrument.Mode,
            instrument.InstrumentNumber,
            instrument.InstrumentDate,
            instrument.BankReference,
            instrument.PayeeName,
            instrument.Remarks,
            instrument.IsPrinted,
            instrument.PrintedAtUtc);

    private static string Name(Dictionary<Guid, string> ledgers, Guid id) =>
        ledgers.TryGetValue(id, out var name) ? name : "(unknown)";

    private async Task<Ledger> RequireLedgerAsync(
        Guid companyId,
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(ledgerId, cancellationToken)
            .ConfigureAwait(false);

        return ledger is null || ledger.CompanyId != companyId
            ? throw new InvalidOperationException("That ledger does not exist in this company's books.")
            : ledger;
    }

    private async Task<PaymentInstrument> RequireInstrumentAsync(
        Guid companyId,
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        var instrument = await _unitOfWork.Repository<PaymentInstrument>()
            .GetByIdAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        return instrument is null || instrument.CompanyId != companyId
            ? throw new InvalidOperationException(
                "That payment instrument does not exist in this company's books.")
            : instrument;
    }

    private sealed class InstrumentsOfCompany : Specification<PaymentInstrument>
    {
        public InstrumentsOfCompany(Guid companyId, Guid? bankLedgerId, PaymentMode? mode)
        {
            Where(instrument => instrument.CompanyId == companyId
                && (!bankLedgerId.HasValue || instrument.BankLedgerId == bankLedgerId.Value)
                && (!mode.HasValue || instrument.Mode == mode.Value));
        }
    }

    private sealed class LinesOfCompany : Specification<JournalLine>
    {
        public LinesOfCompany(Guid companyId)
        {
            Where(line => line.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class CurrentRevisionsOfCompany : Specification<VoucherRevision>
    {
        public CurrentRevisionsOfCompany(Guid companyId)
        {
            Where(revision => revision.CompanyId == companyId
                && revision.SupersededByRevisionId == null);
            ApplyNoTracking();
        }
    }

    private sealed class LedgersOfCompany : Specification<Ledger>
    {
        public LedgersOfCompany(Guid companyId)
        {
            Where(ledger => ledger.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
