using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Customers;

/// <summary>Read model for one customer.</summary>
public sealed record CustomerSummary(
    Guid Id,
    string Name,
    Guid LedgerId,
    string LedgerName,
    Guid CustomerCategoryId,
    string CustomerCategoryName,
    string? ContactPerson,
    string? PhoneNumber,
    string? Email,
    string? Gstin,
    string? PlaceOfSupplyStateCode,
    string? BillingAddressLines,
    string? ShippingAddressLines,
    decimal CreditLimit,
    int? CreditPeriodDays,
    bool IsActive);

/// <param name="Id">Null creates a customer (and its Sundry Debtors ledger); a value revises it.</param>
/// <param name="CreditLimit">
/// Zero means no limit. A null <c>CreditPeriodDays</c> means no agreed credit period.
/// </param>
public sealed record SaveCustomerRequest(
    Guid? Id,
    string Name,
    Guid CustomerCategoryId,
    string? ContactPerson = null,
    string? PhoneNumber = null,
    string? Email = null,
    string? Gstin = null,
    string? PlaceOfSupplyStateCode = null,
    string? BillingAddressLines = null,
    string? ShippingAddressLines = null,
    decimal CreditLimit = 0m,
    int? CreditPeriodDays = null,
    bool IsActive = true);

/// <summary>The ledger created alongside a new customer, so callers can show what appeared.</summary>
public sealed record SavedCustomer(
    Guid Id,
    string Name,
    Guid LedgerId,
    Guid CustomerCategoryId,
    bool CreatedLedger);

/// <summary>
/// Maintains the customer master: contact and GST details, addresses, place of supply, credit
/// terms and the one category that prices each customer (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// Every customer links one-to-one to an ordinary ledger under "Sundry Debtors", created here
/// inside the caller's save: invoices keep posting against ledgers exactly as Phase 3 did, and
/// every downstream report keeps working unchanged. Deactivating a customer never touches their
/// ledger — the account stays open for receipts against money still owed.
/// </remarks>
public interface ICustomerService
{
    Task<IReadOnlyList<CustomerSummary>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>Name search over the normalised form — "anand" finds "Anand Traders".</summary>
    Task<IReadOnlyList<CustomerSummary>> SearchAsync(
        string searchText,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<CustomerSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer with its own Sundry Debtors ledger (named after the customer), or
    /// revises an existing one — including re-categorisation, which is ordinary maintenance.
    /// </summary>
    Task<SavedCustomer> SaveAsync(
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default);

    Task ActivateAsync(Guid id, CancellationToken cancellationToken = default);
}
