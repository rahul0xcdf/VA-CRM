using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Application.Tds;

/// <summary>
/// The per-company TDS/TCS configuration (phase-7-tds-payroll-banking.md §4a D2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Enabling a flag is checked against the section it names.</b> A purchase-of-goods deduction
/// must point at a TDS section and a sale-of-goods collection at a TCS section; pointing them the
/// wrong way round would post the tax to the wrong liability ledger and file it under the wrong
/// head. The domain refuses an enabled flag with no section; this service refuses one with the
/// wrong kind of section, because only it can see the master row.
/// </para>
/// <para>
/// The row is created on demand rather than assumed present, so a company provisioned before
/// Phase 7 opens its settings screen and finds defaults instead of an error.
/// </para>
/// </remarks>
public sealed class TdsSettingsService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : ITdsSettingsService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<TdsSettingsSummary> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var settings = await LoadOrCreateAsync(companyId, cancellationToken).ConfigureAwait(false);

        return await SummariseAsync(companyId, settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TdsSettingsSummary> SaveAsync(
        SaveTdsSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var settings = await LoadOrCreateAsync(companyId, cancellationToken).ConfigureAwait(false);

        await RequireSectionOfKindAsync(
                companyId,
                request.IsPurchaseOfGoodsDeductionEnabled ? request.PurchaseOfGoodsSectionId : null,
                TaxSectionKind.Tds,
                "purchase-of-goods deduction",
                cancellationToken)
            .ConfigureAwait(false);

        await RequireSectionOfKindAsync(
                companyId,
                request.IsSaleOfGoodsCollectionEnabled ? request.SaleOfGoodsSectionId : null,
                TaxSectionKind.Tcs,
                "sale-of-goods collection",
                cancellationToken)
            .ConfigureAwait(false);

        settings.SetTdsPayableLedgerName(request.TdsPayableLedgerName);
        settings.SetTcsPayableLedgerName(request.TcsPayableLedgerName);
        settings.ConfigurePurchaseOfGoods(
            request.IsPurchaseOfGoodsDeductionEnabled,
            request.PurchaseOfGoodsSectionId);
        settings.ConfigureSaleOfGoods(
            request.IsSaleOfGoodsCollectionEnabled,
            request.SaleOfGoodsSectionId);

        _unitOfWork.Repository<TdsSettings>().Update(settings);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await SummariseAsync(companyId, settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TdsSettings> LoadOrCreateAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<TdsSettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var created = new TdsSettings(
            companyId,
            TdsSeedData.TdsPayableLedgerName,
            TdsSeedData.TcsPayableLedgerName);

        await _unitOfWork.Repository<TdsSettings>().AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return created;
    }

    /// <summary>
    /// Refuses a section that is not of the kind the flag needs, or belongs to another company.
    /// </summary>
    /// <remarks>
    /// Null is accepted, because the caller passes null whenever the flag is off, and the domain's
    /// own guard covers "enabled with nothing named".
    /// </remarks>
    private async Task RequireSectionOfKindAsync(
        Guid companyId,
        Guid? sectionId,
        TaxSectionKind expected,
        string flagDescription,
        CancellationToken cancellationToken)
    {
        if (sectionId is not { } id || id == Guid.Empty)
        {
            return;
        }

        var section = await _unitOfWork.Repository<TaxSection>()
            .GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (section is null || section.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                $"The section chosen for {flagDescription} does not exist in this company's books.");
        }

        if (section.Kind != expected)
        {
            throw new InvalidOperationException(
                $"Section '{section.Code}' is a {section.Kind} section, but {flagDescription} "
                + $"needs a {expected} one.");
        }

        if (!section.IsActive)
        {
            throw new InvalidOperationException(
                $"Section '{section.Code}' is retired, so {flagDescription} cannot resolve through it.");
        }
    }

    private async Task<TdsSettingsSummary> SummariseAsync(
        Guid companyId,
        TdsSettings settings,
        CancellationToken cancellationToken)
    {
        var codes = (await _unitOfWork.Repository<TaxSection>()
                .ListAsync(new SectionsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(section => section.Id, section => section.Code);

        return new TdsSettingsSummary(
            settings.TdsPayableLedgerName,
            settings.TcsPayableLedgerName,
            settings.IsPurchaseOfGoodsDeductionEnabled,
            settings.PurchaseOfGoodsSectionId,
            Code(codes, settings.PurchaseOfGoodsSectionId),
            settings.IsSaleOfGoodsCollectionEnabled,
            settings.SaleOfGoodsSectionId,
            Code(codes, settings.SaleOfGoodsSectionId));
    }

    private static string? Code(Dictionary<Guid, string> codes, Guid? sectionId) =>
        sectionId is { } id && codes.TryGetValue(id, out var code) ? code : null;

    private sealed class SettingsOfCompany : Specification<TdsSettings>
    {
        public SettingsOfCompany(Guid companyId) => Where(settings => settings.CompanyId == companyId);
    }

    private sealed class SectionsOfCompany : Specification<TaxSection>
    {
        public SectionsOfCompany(Guid companyId)
        {
            Where(section => section.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
