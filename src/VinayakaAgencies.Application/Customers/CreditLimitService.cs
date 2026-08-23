using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// Applies the company's credit policy to a proposed sale
/// (phase-4-customer-pricing.md §2): under Block policy a sale that would push the customer past
/// their limit is refused; under Warn it goes through with the numbers attached.
/// </summary>
/// <remarks>
/// The outstanding is the customer ledger's derived balance — SUM over journal lines through the
/// Phase 1 reporting seam, never a stored figure (project.md §4.1). A zero limit means no limit,
/// matching the master's own convention, and nothing ever blocks under Warn.
/// </remarks>
public sealed class CreditLimitService(
    IUnitOfWork unitOfWork,
    ISqlQueryExecutor queryExecutor,
    IClock clock) : ICreditLimitService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ISqlQueryExecutor _queryExecutor = queryExecutor
        ?? throw new ArgumentNullException(nameof(queryExecutor));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async Task<CreditCheckResult> EvaluateAsync(
        Guid companyId,
        Guid customerId,
        long additionalValueMinor,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(additionalValueMinor);

        var customer = await _unitOfWork.Repository<Customer>()
            .GetByIdAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null || customer.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                "This customer does not exist in this company's books.");
        }

        var settings = await _unitOfWork.Repository<SalesSettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        // A company whose settings row has not been seeded yet evaluates at the approved
        // defaults rather than blocking its sales.
        var policy = settings?.CreditLimitPolicy ?? CreditLimitPolicy.Warn;

        var outstanding = await OutstandingForLedgerAsync(companyId, customer.LedgerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer.CreditLimitMinor <= 0)
        {
            return new CreditCheckResult(
                policy,
                IsBlocked: false,
                outstanding,
                LimitMinor: 0,
                "No credit limit is set for this customer.");
        }

        var projected = outstanding + additionalValueMinor;
        var exceeds = projected > customer.CreditLimitMinor;

        return new CreditCheckResult(
            policy,
            IsBlocked: policy == CreditLimitPolicy.Block && exceeds,
            outstanding,
            customer.CreditLimitMinor,
            exceeds
                ? $"Credit limit exceeded: {ToMoneyText(outstanding)} outstanding plus "
                  + $"{ToMoneyText(additionalValueMinor)} on this bill would reach "
                  + $"{ToMoneyText(projected)} against a "
                  + $"{ToMoneyText(customer.CreditLimitMinor)} limit."
                : $"Within the credit limit: projected {ToMoneyText(projected)} of "
                  + $"{ToMoneyText(customer.CreditLimitMinor)}."
        );
    }

    /// <summary>
    /// The ledger's closing position as of today, debit-positive for a debtor, in minor units.
    /// </summary>
    private async Task<long> OutstandingForLedgerAsync(
        Guid companyId,
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        var rows = await _queryExecutor.QueryAsync<LedgerBalanceSqlRow>(
            AccountingQueryKeys.LedgerBalancesAsOf,
            new { CompanyId = companyId, AsOfDate = CalendarText.For(_clock.Today) },
            cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Where(row => Guid.Parse(row.LedgerId) == ledgerId)
            .Select(row => row.ClosingMinor)
            .SingleOrDefault();
    }

    private static string ToMoneyText(long minor) =>
        MoneyScale.ToInvariantString(MoneyScale.FromMinor(minor));

    private sealed record LedgerBalanceSqlRow(
        string LedgerId,
        string LedgerName,
        string AccountGroupName,
        string Nature,
        long ClosingMinor);

    private sealed class SettingsOfCompany : Specification<SalesSettings>
    {
        public SettingsOfCompany(Guid companyId)
        {
            Where(row => row.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
