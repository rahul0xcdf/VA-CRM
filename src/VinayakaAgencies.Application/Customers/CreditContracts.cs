using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Customers;

/// <summary>The outcome of a credit check against one customer's terms.</summary>
/// <param name="Policy">The policy in force — the result never blocks under <see cref="CreditLimitPolicy.Warn"/>.</param>
/// <param name="IsBlocked">True only under Block policy with a limit set and exceeded.</param>
/// <param name="CurrentOutstandingMinor">
/// The customer ledger's derived receivable balance before this sale, in minor units.
/// </param>
/// <param name="LimitMinor">Zero means no limit is configured.</param>
/// <param name="Message">Operator-facing sentence carrying the numbers.</param>
public sealed record CreditCheckResult(
    CreditLimitPolicy Policy,
    bool IsBlocked,
    long CurrentOutstandingMinor,
    long LimitMinor,
    string Message);

/// <summary>
/// Applies the company's credit policy to a proposed sale
/// (phase-4-customer-pricing.md §2): warn or block when the outstanding plus the new value would
/// cross the customer's credit limit. Aging buckets and overdue-day analytics stay out of this
/// phase; the check is deliberately just the two numbers an operator can act on.
/// </summary>
public interface ICreditLimitService
{
    /// <summary>Evaluates a proposed sale of <paramref name="additionalValueMinor"/> to one customer.</summary>
    Task<CreditCheckResult> EvaluateAsync(
        Guid companyId,
        Guid customerId,
        long additionalValueMinor,
        CancellationToken cancellationToken = default);
}
