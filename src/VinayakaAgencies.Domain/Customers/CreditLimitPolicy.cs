namespace VinayakaAgencies.Domain.Customers;

/// <summary>
/// What happens when a new order would push a customer past their credit limit or credit period
/// (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// Policy is the per-company setting (<see cref="SalesSettings.CreditLimitPolicy"/>), not a
/// per-customer column: the owner picks one behaviour for the install and changes it when credit
/// discipline tightens. Persisted by name, never by number.
/// </remarks>
public enum CreditLimitPolicy
{
    /// <summary>Allow the order through and surface a warning.</summary>
    Warn = 1,

    /// <summary>Refuse the order until the outstanding clears.</summary>
    Block = 2,
}
