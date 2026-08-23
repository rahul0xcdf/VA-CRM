using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// Reads and updates the per-company sales settings row
/// (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// Get-or-create on first read, exactly like the inventory settings' lazy-but-audited pattern: a
/// company provisioned before Phase 4 gains its row the first time anything asks, and the write
/// goes through <see cref="IUnitOfWork.SaveChangesAsync"/> so the edit log sees it.
/// </remarks>
public sealed class SalesSettingsService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : ISalesSettingsService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<SalesSettingsSummary> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var settings = await RequireSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);

        return ToSummary(settings);
    }

    /// <inheritdoc />
    public async Task<SalesSettingsSummary> UpdateAsync(
        SaveSalesSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var settings = await RequireSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);

        settings.SetAllowPriceOverride(request.AllowPriceOverride);
        settings.SetCreditLimitPolicy(request.CreditLimitPolicy);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(settings);
    }

    private async Task<SalesSettings> RequireSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var settings = await _unitOfWork.Repository<SalesSettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        if (settings is not null)
        {
            return settings;
        }

        // The approved defaults (§2): overrides allowed for the single-operator install, credit
        // breaches warn. Seeded at provisioning too; this is the belt to that braces.
        settings = new SalesSettings(companyId);
        await _unitOfWork.Repository<SalesSettings>()
            .AddAsync(settings, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return settings;
    }

    private static SalesSettingsSummary ToSummary(SalesSettings settings) =>
        new(settings.Id, settings.AllowPriceOverride, settings.CreditLimitPolicy);

    private sealed class SettingsOfCompany : Specification<SalesSettings>
    {
        public SettingsOfCompany(Guid companyId)
        {
            // Deliberately tracked: UpdateAsync mutates the row it returns, so a detached read
            // here would silently write nothing.
            Where(row => row.CompanyId == companyId);
        }
    }
}
