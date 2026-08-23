using System.Globalization;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Reporting;

using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Manual bank reconciliation against the derived ledger statement (spec §12).
/// </summary>
/// <remarks>
/// The statement is a read through the reporting seam; the marks are ordinary entity writes through
/// the unit of work, so each mark and unmark lands in the edit log with its full before/after image.
/// A mark pins one specific journal line: altering its voucher creates new lines, so the item
/// reappears unreconciled and must be re-confirmed — the safe behaviour, stated in the spec.
/// </remarks>
public sealed class BankReconciliationService(
    IUnitOfWork unitOfWork,
    ISqlQueryExecutor queryExecutor,
    ICompanyContext companyContext) : IBankReconciliationService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ISqlQueryExecutor _queryExecutor = queryExecutor
        ?? throw new ArgumentNullException(nameof(queryExecutor));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankReconciliationLine>> GetStatementAsync(
        Guid ledgerId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("A bank statement needs a ledger.", nameof(ledgerId));
        }

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<StatementSqlRow>(
            AccountingQueryKeys.BankReconciliationStatement,
            new { CompanyId = companyId, LedgerId = ledgerId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(MapRow).ToList();
    }

    /// <inheritdoc />
    public async Task MarkClearedAsync(
        Guid journalLineId,
        DateOnly bankDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var line = await RequireOwnLineAsync(journalLineId, companyId, cancellationToken).ConfigureAwait(false);

        var repository = _unitOfWork.Repository<BankReconciliationMark>();

        var existing = await repository
            .SingleOrDefaultAsync(new MarkOfLine(journalLineId), cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await repository.AddAsync(new BankReconciliationMark(companyId, line.Id, bankDate), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            existing.SetBankDate(bankDate);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnmarkClearedAsync(
        Guid journalLineId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var mark = await _unitOfWork.Repository<BankReconciliationMark>()
            .SingleOrDefaultAsync(new MarkOfLine(journalLineId), cancellationToken)
            .ConfigureAwait(false);

        if (mark is null || mark.CompanyId != companyId)
        {
            return;
        }

        _unitOfWork.Repository<BankReconciliationMark>().Remove(mark);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<JournalLine> RequireOwnLineAsync(
        Guid journalLineId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (journalLineId == Guid.Empty)
        {
            throw new ArgumentException("JournalLineId must not be empty.", nameof(journalLineId));
        }

        var line = await _unitOfWork.Repository<JournalLine>()
            .GetByIdAsync(journalLineId, cancellationToken)
            .ConfigureAwait(false);

        if (line is null || line.CompanyId != companyId)
        {
            throw new InvalidOperationException($"Journal line '{journalLineId}' does not exist.");
        }

        return line;
    }

    private static BankReconciliationLine MapRow(StatementSqlRow row)
    {
        var amountMinor = row.AmountMinor;

        return new BankReconciliationLine(
            ToGuid(row.JournalLineId),
            ToDate(row.VoucherDate),
            row.VoucherNumber,
            row.VoucherTypeName,
            amountMinor > 0 ? MoneyScale.FromMinor(amountMinor) : 0m,
            amountMinor < 0 ? MoneyScale.FromMinor(-amountMinor) : 0m,
            row.Narration,
            IsCleared: row.BankDate is not null,
            ClearedOnBank: row.BankDate is { } cleared ? ToDate(cleared) : null);
    }

    private static DateOnly ToDate(string value) =>
        DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static Guid ToGuid(string value) => Guid.Parse(value);

    private sealed class MarkOfLine : Specification<BankReconciliationMark>
    {
        public MarkOfLine(Guid journalLineId)
        {
            Where(mark => mark.JournalLineId == journalLineId);
        }
    }

    private sealed record StatementSqlRow(
        string JournalLineId,
        string VoucherDate,
        string VoucherNumber,
        string VoucherTypeName,
        long AmountMinor,
        string? Narration,
        string? BankDate);
}
