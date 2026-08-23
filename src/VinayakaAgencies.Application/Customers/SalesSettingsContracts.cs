using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Customers;

/// <summary>Read model for the one-per-company sales settings row.</summary>
public sealed record SalesSettingsSummary(
    Guid Id,
    bool AllowPriceOverride,
    CreditLimitPolicy CreditLimitPolicy);

/// <param name="AllowPriceOverride">
/// The Phase 4 override gate (spec §2): when false, the pricing service refuses overrides and the
/// price cell reads read-only. Every override exercised is audit-logged regardless.
/// </param>
public sealed record SaveSalesSettingsRequest(
    bool AllowPriceOverride,
    CreditLimitPolicy CreditLimitPolicy);

/// <summary>
/// Reads and updates the per-company sales settings, creating the row at its approved defaults
/// when absent — the same lazy-but-audited pattern as the inventory settings.
/// </summary>
public interface ISalesSettingsService
{
    /// <summary>Gets the settings row, creating it at the defaults on first touch.</summary>
    Task<SalesSettingsSummary> GetAsync(CancellationToken cancellationToken = default);

    Task<SalesSettingsSummary> UpdateAsync(
        SaveSalesSettingsRequest request,
        CancellationToken cancellationToken = default);
}
