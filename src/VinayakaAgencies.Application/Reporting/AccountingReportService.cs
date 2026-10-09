using System.Globalization;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Reporting;

/// <summary>
/// Derived accounting statements, read through the reporting seam (ADR-0001 §3).
/// </summary>
/// <remarks>
/// Every figure comes from a named <see cref="SqlQueryKey"/> resolved per provider — no SQL string
/// lives in this assembly. The database returns integer minor units (ADR-0003), which are converted
/// to domain decimals exactly once, in <see cref="ToMoney"/>, so no report can drift into floating
/// point by taking a shortcut.
/// <para>
/// Dates travel in and out as canonical calendar text (<c>yyyy-MM-dd</c>): business dates are local
/// calendar facts, and parsing them against the invariant culture keeps a voucher dated 1 April in
/// April regardless of the machine's regional settings.
/// </para>
/// </remarks>
public sealed class AccountingReportService(
    IUnitOfWork unitOfWork,
    ISqlQueryExecutor queryExecutor,
    ICompanyContext companyContext) : IAccountingReportService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ISqlQueryExecutor _queryExecutor = queryExecutor
        ?? throw new ArgumentNullException(nameof(queryExecutor));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<DayBookRow>> GetDayBookAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<DayBookSqlRow>(
            AccountingQueryKeys.DayBookForPeriod,
            new { CompanyId = companyId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(MapDayBookRow).ToList();
    }

    /// <inheritdoc />
    public async Task<TrialBalance> GetTrialBalanceAsync(
        DateOnly asOnDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var yearStart = await FinancialYearStartForAsync(asOnDate, cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<TrialBalanceSqlRow>(
            AccountingQueryKeys.TrialBalance,
            new { CompanyId = companyId, FromDate = CalendarText.For(yearStart), ToDate = CalendarText.For(asOnDate) },
            cancellationToken)
            .ConfigureAwait(false);

        var mapped = rows
            .Select(row =>
            {
                var closing = ToMoney(row.ClosingMinor);

                // Presentation columns: one net balance per ledger, shown in whichever column its
                // sign puts it. Exactly one of the two is non-zero.
                var (debit, credit) = closing > 0m ? (closing, 0m) : (0m, -closing);

                return new TrialBalanceRow(
                    ToGuid(row.LedgerId),
                    row.LedgerName,
                    row.AccountGroupName,
                    ToEnum<AccountNature>(row.Nature),
                    debit,
                    credit);
            })
            .ToList();

        return new TrialBalance(
            asOnDate,
            mapped,
            mapped.Sum(row => row.Debit),
            mapped.Sum(row => row.Credit));
    }

    /// <inheritdoc />
    public async Task<ProfitAndLoss> GetProfitAndLossAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<ProfitAndLossSqlRow>(
            AccountingQueryKeys.ProfitAndLossForPeriod,
            new { CompanyId = companyId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);

        var mapped = rows.Select(MapProfitAndLossRow).ToList();

        var totalIncome = mapped.Where(row => row.Nature == AccountNature.Income).Sum(row => row.Amount);
        var totalExpense = mapped.Where(row => row.Nature == AccountNature.Expense).Sum(row => row.Amount);

        // The gross-profit block is every income and expense line whose group affects gross profit
        // (spec §4.3); everything else lands below the line.
        var grossIncome = mapped
            .Where(row => row.AffectsGrossProfit && row.Nature == AccountNature.Income)
            .Sum(row => row.Amount);
        var grossExpense = mapped
            .Where(row => row.AffectsGrossProfit && row.Nature == AccountNature.Expense)
            .Sum(row => row.Amount);

        return new ProfitAndLoss(
            fromDate,
            toDate,
            mapped,
            totalIncome,
            totalExpense,
            grossIncome - grossExpense,
            totalIncome - totalExpense);
    }

    /// <inheritdoc />
    public async Task<BalanceSheet> GetBalanceSheetAsync(
        DateOnly asOnDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var yearStart = await FinancialYearStartForAsync(asOnDate, cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<BalanceSheetRowSql>(
            AccountingQueryKeys.BalanceSheetAsOf,
            new { CompanyId = companyId, AsOfDate = CalendarText.For(asOnDate), FinancialYearStartDate = CalendarText.For(yearStart) },
            cancellationToken)
            .ConfigureAwait(false);

        var assets = new List<BalanceSheetRow>();
        var liabilities = new List<BalanceSheetRow>();

        foreach (var row in rows)
        {
            var mapped = MapBalanceSheetRow(row);

            if (mapped.Nature == AccountNature.Asset)
            {
                assets.Add(mapped);
            }
            else
            {
                liabilities.Add(mapped);
            }
        }

        return new BalanceSheet(
            asOnDate,
            assets,
            liabilities,
            assets.Sum(row => row.Amount),
            liabilities.Sum(row => row.Amount));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerStatementRow>> GetLedgerStatementAsync(
        Guid ledgerId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<LedgerStatementSqlRow>(
            AccountingQueryKeys.LedgerVouchersForPeriod,
            new { CompanyId = companyId, LedgerId = ledgerId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(MapStatementRow).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerStatementRow>> GetVoucherRegisterAsync(
        Guid voucherTypeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _queryExecutor.QueryAsync<LedgerStatementSqlRow>(
            AccountingQueryKeys.VoucherRegisterByType,
            new { CompanyId = companyId, VoucherTypeId = voucherTypeId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(MapStatementRow).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VoucherNumberGap>> GetVoucherNumberGapsAsync(
        Guid seriesId,
        Guid financialYearId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var seriesRepository = _unitOfWork.Repository<VoucherNumberSeries>();
        var series = await seriesRepository.GetByIdAsync(seriesId, cancellationToken).ConfigureAwait(false);

        if (series is null || series.CompanyId != companyId)
        {
            throw new InvalidOperationException($"Voucher number series '{seriesId}' does not exist.");
        }

        var sequence = await _unitOfWork.Repository<VoucherNumberSequence>()
            .SingleOrDefaultAsync(new SequenceOfSeriesAndYear(seriesId, financialYearId), cancellationToken)
            .ConfigureAwait(false);

        var issued = await _queryExecutor.QueryAsync<IssuedNumberSqlRow>(
            AccountingQueryKeys.VoucherNumberGaps,
            new { CompanyId = companyId, SeriesId = seriesId, FinancialYearId = financialYearId },
            cancellationToken)
            .ConfigureAwait(false);

        // Recover each document's sequence position through the one parser the allocator validates
        // manual overrides with. A number that does not parse cannot participate in gap arithmetic —
        // reported as absent rather than guessed at.
        var held = issued
            .Select(row => VoucherNumberParser.TryParse(series, row.Number, out var numeric) ? numeric : 0)
            .Where(numeric => numeric > 0)
            .ToHashSet();

        var highWater = Math.Max(sequence?.LastAllocatedNumber ?? 0, held.Count == 0 ? 0 : held.Max());

        var gaps = new List<VoucherNumberGap>();

        for (var expected = series.StartNumber; expected <= highWater; expected++)
        {
            if (!held.Contains(expected))
            {
                gaps.Add(new VoucherNumberGap(expected));
            }
        }

        return gaps;
    }

    /// <summary>The first day of the financial year containing <paramref name="date"/>.</summary>
    private async Task<DateOnly> FinancialYearStartForAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.Repository<VinayakaAgencies.Domain.Entities.Company>()
            .GetByIdAsync(await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

        if (company is null)
        {
            throw new InvalidOperationException("No company has been provisioned, so there is no financial year.");
        }

        return FinancialYear.RangeFor(date, company.FinancialYearStartMonth).Start;
    }

    private static DayBookRow MapDayBookRow(DayBookSqlRow row)
    {
        var isCancelled = string.Equals(row.Status, nameof(VoucherStatus.Cancelled), StringComparison.Ordinal);
        var amount = row.AmountMinor ?? 0;

        return new DayBookRow(
            ToDate(row.VoucherDate),
            row.VoucherNumber,
            row.VoucherTypeName,
            ToEnum<VoucherKind>(row.Kind),
            isCancelled ? "(cancelled)" : row.LedgerName ?? string.Empty,
            !isCancelled && amount > 0 ? ToMoney(amount) : 0m,
            !isCancelled && amount < 0 ? ToMoney(-amount) : 0m,
            row.Narration,
            ToGuid(row.VoucherId),
            isCancelled);
    }

    private static ProfitAndLossRow MapProfitAndLossRow(ProfitAndLossSqlRow row)
    {
        var nature = ToEnum<AccountNature>(row.Nature);

        // Income arrives credit-positive in the journal (a negative signed sum); expenses arrive
        // debit-positive. Both are shown as positive figures on their own side of the statement.
        var amount = nature == AccountNature.Income ? -row.NetMinor : row.NetMinor;

        return new ProfitAndLossRow(
            ToGuid(row.LedgerId),
            row.LedgerName,
            row.AccountGroupName,
            nature,
            row.AffectsGrossProfit != 0,
            ToMoney(amount));
    }

    private static BalanceSheetRow MapBalanceSheetRow(BalanceSheetRowSql row)
    {
        var nature = ToEnum<AccountNature>(row.Nature);

        // The statement shows both sides positive-normal: an asset's debit balance as-is, a
        // liability's credit balance flipped from its negative signed sum. This is the only place
        // the flip happens — the query hands over raw signed minor units — so a second provider's
        // dialect cannot disagree with this one about the sign of a creditor.
        var amount = nature == AccountNature.Liability ? -row.AmountMinor : row.AmountMinor;

        return new BalanceSheetRow(
            row.LedgerId is { } id ? ToGuid(id) : Guid.Empty,
            row.LedgerName,
            row.AccountGroupName,
            nature,
            ToMoney(amount));
    }

    private static LedgerStatementRow MapStatementRow(LedgerStatementSqlRow row)
    {
        var amount = row.AmountMinor;

        return new LedgerStatementRow(
            ToGuid(row.VoucherId),
            ToDate(row.VoucherDate),
            row.VoucherNumber,
            row.VoucherTypeName,
            ToEnum<VoucherKind>(row.Kind),
            amount > 0 ? ToMoney(amount) : 0m,
            amount < 0 ? ToMoney(-amount) : 0m,
            row.Narration);
    }

    /// <summary>Minor units to rupees — the one conversion point every report passes through.</summary>
    private static decimal ToMoney(long minor) => MoneyScale.FromMinor(minor);

    private static DateOnly ToDate(string value) =>
        DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static Guid ToGuid(string value) => Guid.Parse(value);

    private static TEnum ToEnum<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(value, ignoreCase: false);

    private sealed class SequenceOfSeriesAndYear : Specification<VoucherNumberSequence>
    {
        public SequenceOfSeriesAndYear(Guid seriesId, Guid financialYearId)
        {
            Where(sequence => sequence.SeriesId == seriesId
                && sequence.FinancialYearId == financialYearId);
            ApplyNoTracking();
        }
    }

    /// <summary>Dapper materialisation rows. Minor units stay integers until <see cref="ToMoney"/>.</summary>
    private sealed record DayBookSqlRow(
        string VoucherDate,
        string VoucherNumber,
        string VoucherTypeName,
        string Kind,
        string? LedgerName,
        long? AmountMinor,
        string? Narration,
        string VoucherId,
        string Status);

    private sealed record TrialBalanceSqlRow(
        string LedgerId,
        string LedgerName,
        string AccountGroupName,
        string Nature,
        long ClosingMinor);

    private sealed record ProfitAndLossSqlRow(
        string LedgerId,
        string LedgerName,
        string AccountGroupName,
        string Nature,
        long AffectsGrossProfit,
        long NetMinor);

    private sealed record BalanceSheetRowSql(
        string? LedgerId,
        string LedgerName,
        string AccountGroupName,
        string Nature,
        long AmountMinor);

    private sealed record LedgerStatementSqlRow(
        string VoucherId,
        string VoucherDate,
        string VoucherNumber,
        string VoucherTypeName,
        string Kind,
        long AmountMinor,
        string? Narration);

    private sealed record IssuedNumberSqlRow(string Number);
}
