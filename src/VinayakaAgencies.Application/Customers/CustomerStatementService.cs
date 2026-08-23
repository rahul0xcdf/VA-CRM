using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// The customer-facing reads over the books: the account statement and the bill-wise outstanding
/// report (phase-4-customer-pricing.md §2, checkpoints C4.3 and C4.6).
/// </summary>
/// <remarks>
/// Nothing here stores a balance. The statement walks the customer ledger's postings through the
/// Phase 1 reporting seam — the same ledger-statement query the drill-down uses — with the opening
/// position derived from the balances-as-of query. Bill-wise derives each bill from its posted tax
/// invoice and shares out the unallocated receipt pool oldest-bill-first, where the pool is simply
/// what was billed less what is still owed: <c>Σbills − closing</c>. All arithmetic is minor-unit
/// integers; decimals appear once, in the mappers.
/// </remarks>
public sealed class CustomerStatementService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    ISqlQueryExecutor queryExecutor,
    IAccountingReportService reportService,
    IClock clock) : ICustomerStatementService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly ISqlQueryExecutor _queryExecutor = queryExecutor
        ?? throw new ArgumentNullException(nameof(queryExecutor));

    private readonly IAccountingReportService _reportService = reportService
        ?? throw new ArgumentNullException(nameof(reportService));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async Task<CustomerStatement?> CustomerStatementAsync(
        Guid customerId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (toDate < fromDate)
        {
            throw new ArgumentException("The statement's end date is before its start date.");
        }

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var customer = await RequireCustomerAsync(companyId, customerId, cancellationToken)
            .ConfigureAwait(false);
        var ledger = await RequireLedgerAsync(customer, cancellationToken).ConfigureAwait(false);

        var openingMinor = await ClosingBalanceMinorAsync(
                companyId, customer.LedgerId, fromDate.AddDays(-1), cancellationToken)
            .ConfigureAwait(false);

        var opening = MoneyScale.FromMinor(openingMinor);

        var postings = await _reportService
            .GetLedgerStatementAsync(customer.LedgerId, fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);

        var running = opening;
        var rows = new List<CustomerStatementRow>(postings.Count);

        foreach (var posting in postings)
        {
            running = MoneyScale.Round(running + posting.Debit - posting.Credit);

            rows.Add(new CustomerStatementRow(
                posting.VoucherDate,
                posting.VoucherNumber,
                posting.VoucherTypeName,
                posting.Debit,
                posting.Credit,
                running));
        }

        return new CustomerStatement(
            customer.Id,
            customer.Name,
            customer.LedgerId,
            fromDate,
            toDate,
            opening,
            rows,
            running);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenBillRow>> BillwiseOutstandingAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var customer = await RequireCustomerAsync(companyId, customerId, cancellationToken)
            .ConfigureAwait(false);

        var invoices = (await _unitOfWork.Repository<TradeDocument>()
                .ListAsync(
                    new IssuedInvoicesForParty(companyId, customer.LedgerId),
                    cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(invoice => invoice.DocumentDate)
            .ThenBy(invoice => invoice.Number, StringComparer.Ordinal)
            .ToList();

        if (invoices.Count == 0)
        {
            return [];
        }

        var closingMinor = await ClosingBalanceMinorAsync(
                companyId, customer.LedgerId, _clock.Today, cancellationToken)
            .ConfigureAwait(false);

        // Each bill is what its posted lines totalled; the pool is what was billed less what is
        // still owed, i.e. the receipts (and any other credits) not yet spoken for by a bill.
        var bills = invoices
            .Select(invoice => (
                Invoice: invoice,
                AmountMinor: MoneyScale.ToMinor(invoice.Lines.Sum(line => line.NetValue))))
            .ToList();

        var poolMinor = Math.Max(0L, bills.Sum(bill => bill.AmountMinor) - closingMinor);

        var rows = new List<OpenBillRow>(bills.Count);

        foreach (var bill in bills)
        {
            var allocated = Math.Min(bill.AmountMinor, poolMinor);
            poolMinor -= allocated;

            if (bill.AmountMinor - allocated <= 0)
            {
                continue; // Fully paid bills leave the report, exactly like Tally's.
            }

            rows.Add(new OpenBillRow(
                bill.Invoice.Id,
                bill.Invoice.Number ?? string.Empty,
                bill.Invoice.DocumentDate,
                MoneyScale.FromMinor(bill.AmountMinor),
                MoneyScale.FromMinor(allocated),
                MoneyScale.FromMinor(bill.AmountMinor - allocated)));
        }

        return rows;
    }

    /// <summary>The ledger's closing position as of a date, debit-positive, in minor units.</summary>
    private async Task<long> ClosingBalanceMinorAsync(
        Guid companyId,
        Guid ledgerId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var rows = await _queryExecutor.QueryAsync<LedgerBalanceSqlRow>(
            AccountingQueryKeys.LedgerBalancesAsOf,
            new { CompanyId = companyId, AsOfDate = CalendarText.For(asOfDate) },
            cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Where(row => Guid.Parse(row.LedgerId) == ledgerId)
            .Select(row => row.ClosingMinor)
            .SingleOrDefault();
    }

    private async Task<Customer> RequireCustomerAsync(
        Guid companyId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var customer = await _unitOfWork.Repository<Customer>()
            .GetByIdAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null || customer.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                "This customer does not exist in this company's books.");
        }

        return customer;
    }

    private async Task<Ledger> RequireLedgerAsync(
        Customer customer,
        CancellationToken cancellationToken)
    {
        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(customer.LedgerId, cancellationToken)
            .ConfigureAwait(false);

        return ledger ?? throw new InvalidOperationException(
            $"Customer '{customer.Name}' has no account ledger, which should be impossible "
            + "outside corrupted books.");
    }

    private sealed record LedgerBalanceSqlRow(
        string LedgerId,
        string LedgerName,
        string AccountGroupName,
        string Nature,
        long ClosingMinor);

    /// <summary>Posted, uncancelled sales invoices against one party, with their lines.</summary>
    private sealed class IssuedInvoicesForParty : Specification<TradeDocument>
    {
        public IssuedInvoicesForParty(Guid companyId, Guid partyLedgerId)
        {
            Where(document => document.CompanyId == companyId
                && document.PartyLedgerId == partyLedgerId
                && document.Kind == TradeDocumentKind.TaxInvoice
                && document.Status == TradeDocumentStatus.Issued);
            AddInclude(document => document.Lines);
            ApplyNoTracking();
        }
    }
}
