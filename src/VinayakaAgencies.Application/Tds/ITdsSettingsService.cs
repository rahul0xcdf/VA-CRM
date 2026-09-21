namespace VinayakaAgencies.Application.Tds;

/// <summary>Read model for a company's TDS/TCS configuration.</summary>
public sealed record TdsSettingsSummary(
    string TdsPayableLedgerName,
    string TcsPayableLedgerName,
    bool IsPurchaseOfGoodsDeductionEnabled,
    Guid? PurchaseOfGoodsSectionId,
    string? PurchaseOfGoodsSectionCode,
    bool IsSaleOfGoodsCollectionEnabled,
    Guid? SaleOfGoodsSectionId,
    string? SaleOfGoodsSectionCode);

/// <summary>
/// Saves the configuration. Enabling either flag without naming its section is refused — a flag
/// that is on and resolves to nothing is worse than one that is off.
/// </summary>
public sealed record SaveTdsSettingsRequest(
    string TdsPayableLedgerName,
    string TcsPayableLedgerName,
    bool IsPurchaseOfGoodsDeductionEnabled = false,
    Guid? PurchaseOfGoodsSectionId = null,
    bool IsSaleOfGoodsCollectionEnabled = false,
    Guid? SaleOfGoodsSectionId = null);

/// <summary>
/// The per-company TDS/TCS configuration (phase-7-tds-payroll-banking.md §4a D2).
/// </summary>
/// <remarks>
/// The two turnover-linked sections are declared here and nowhere else. They are <b>flags the owner
/// sets</b>, never inferred from computed turnover — the Phase 6 precedent for e-invoicing
/// applicability, for the same reason: turnover as the books compute it is not necessarily turnover
/// as the statute defines it, and an obligation that switches itself on silently is worse than one
/// that is declared.
/// </remarks>
public interface ITdsSettingsService
{
    Task<TdsSettingsSummary> GetAsync(CancellationToken cancellationToken = default);

    Task<TdsSettingsSummary> SaveAsync(
        SaveTdsSettingsRequest request,
        CancellationToken cancellationToken = default);
}
