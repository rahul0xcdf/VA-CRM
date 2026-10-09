using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Customers;

/// <summary>
/// Per-company sales configuration: the override gate and the credit-limit policy
/// (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// One row per company, seeded at provisioning. The override permission lives here as a data-driven
/// app setting because user roles only arrive in Phase 9 (§2): until then it gates both the pricing
/// service and the UI, and every exercised override is written to the audit trail regardless.
/// </remarks>
public sealed class SalesSettings : Entity, IAuditStamped
{
    private SalesSettings()
    {
        // EF Core materialisation.
    }

    public SalesSettings(
        Guid companyId,
        bool allowPriceOverride = true,
        CreditLimitPolicy creditLimitPolicy = CreditLimitPolicy.Warn)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (!Enum.IsDefined(creditLimitPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(creditLimitPolicy), creditLimitPolicy, "Unknown credit limit policy.");
        }

        CompanyId = companyId;
        AllowPriceOverride = allowPriceOverride;
        CreditLimitPolicy = creditLimitPolicy;
    }

    /// <summary>One row per company.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Whether a manual price override is permitted on a sale line. Default on for the
    /// single-operator install; turning it off makes the price cell read-only and refuses overrides
    /// in the pricing service. Logging happens either way.
    /// </summary>
    public bool AllowPriceOverride { get; private set; } = true;

    /// <summary>Whether crossing a customer's credit terms warns or blocks a new order.</summary>
    public CreditLimitPolicy CreditLimitPolicy { get; private set; } = CreditLimitPolicy.Warn;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void SetAllowPriceOverride(bool allowPriceOverride) =>
        AllowPriceOverride = allowPriceOverride;

    public void SetCreditLimitPolicy(CreditLimitPolicy creditLimitPolicy)
    {
        if (!Enum.IsDefined(creditLimitPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(creditLimitPolicy), creditLimitPolicy, "Unknown credit limit policy.");
        }

        CreditLimitPolicy = creditLimitPolicy;
    }
}
