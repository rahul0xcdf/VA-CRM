using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Application.Tds;

/// <summary>
/// The TDS/TCS master data (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>This service is what makes checkpoint C7.2 true.</b> Not one section number, rate or threshold
/// appears in this file or anywhere below it: a section is a row the owner types, a rate is a dated
/// row attached to it, and the computation reads both. Adding 194J tomorrow is two inserts through
/// these methods and a voucher posted the same afternoon — no code, no rebuild, no migration. The
/// April 2026 renumbering is the same two inserts plus a deactivation, which is the whole reason §2
/// insists on it.
/// </para>
/// <para>
/// Rate rows follow <c>GstRateService</c>'s discipline exactly — appended, with the open-ended row
/// they supersede closed the day before — because the requirement is identical (C7.4 is C5.5 for a
/// different tax) and two different answers to one question is how a codebase starts disagreeing
/// with itself.
/// </para>
/// </remarks>
public sealed class TaxSectionService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext) : ITaxSectionService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaxSectionSummary>> ListSectionsAsync(
        TaxSectionKind? kind = null,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return (await _unitOfWork.Repository<TaxSection>()
                .ListAsync(new SectionsOfCompany(companyId, kind, includeInactive), cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(section => section.Kind)
            .ThenBy(section => section.Code, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<TaxSectionSummary> CreateSectionAsync(
        SaveTaxSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Code);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await _unitOfWork.Repository<TaxSection>()
                .AnyAsync(new ActiveSectionByCode(companyId, request.Kind, code), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"An active {request.Kind} section '{code}' already exists. Retire it before "
                + "creating another under the same code.");
        }

        var section = new TaxSection(
            companyId,
            request.Kind,
            request.Code,
            request.Name,
            request.StatutePaymentCode);

        await _unitOfWork.Repository<TaxSection>().AddAsync(section, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(section);
    }

    /// <inheritdoc />
    public async Task<TaxSectionSummary> UpdateSectionAsync(
        Guid sectionId,
        SaveTaxSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var section = await RequireSectionAsync(companyId, sectionId, cancellationToken)
            .ConfigureAwait(false);

        section.Rename(request.Name);
        section.SetStatutePaymentCode(request.StatutePaymentCode);

        _unitOfWork.Repository<TaxSection>().Update(section);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(section);
    }

    /// <inheritdoc />
    public async Task<TaxSectionSummary> SetSectionActiveAsync(
        Guid sectionId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var section = await RequireSectionAsync(companyId, sectionId, cancellationToken)
            .ConfigureAwait(false);

        if (isActive)
        {
            // Reactivating into a code another active row already holds would violate the filtered
            // unique index — caught here so the operator gets a sentence rather than a constraint.
            if (await _unitOfWork.Repository<TaxSection>()
                    .AnyAsync(
                        new ActiveSectionByCode(companyId, section.Kind, section.Code),
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Another active {section.Kind} section already uses code '{section.Code}'.");
            }

            section.Activate();
        }
        else
        {
            section.Deactivate();
        }

        _unitOfWork.Repository<TaxSection>().Update(section);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(section);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaxRateScheduleSummary>> ListRateSchedulesAsync(
        Guid? taxSectionId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var schedules = await _unitOfWork.Repository<TaxRateSchedule>()
            .ListAsync(new SchedulesOfCompany(companyId, taxSectionId), cancellationToken)
            .ConfigureAwait(false);

        var codes = await SectionCodeLookupAsync(companyId, cancellationToken).ConfigureAwait(false);

        return schedules
            .OrderBy(schedule => schedule.EffectiveFrom)
            .ThenBy(schedule => schedule.PayeeCategory)
            .Select(schedule => ToSummary(schedule, codes))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<TaxRateScheduleSummary> AddRateScheduleAsync(
        SaveTaxRateScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var section = await RequireSectionAsync(companyId, request.TaxSectionId, cancellationToken)
            .ConfigureAwait(false);

        if (!section.IsActive)
        {
            throw new InvalidOperationException(
                $"Section '{section.Code}' is retired; reactivate it before attaching rates.");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Existing rows for the same section *and the same payee class*. Scoped to the class
        // deliberately: 194C prices individuals and companies with two concurrent rows, and a new
        // company rate must neither end nor collide with the individual one.
        var siblings = await _unitOfWork.Repository<TaxRateSchedule>()
            .ListAsync(
                new SchedulesOfSectionAndCategory(companyId, request.TaxSectionId, request.PayeeCategory),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var existing in siblings)
        {
            // The newcomer is open-ended, so it owns every date from EffectiveFrom onward. A row
            // that begins on or after that date would be swallowed by it, and a closed row that
            // still runs past it would overlap. Both are refused rather than silently resolved.
            //
            // An *open* row that began earlier is the ordinary case — a notification superseding
            // the current rate — and it is closed the day before the newcomer starts. Testing
            // Covers() alone cannot tell these apart: an open row covers every later date by
            // definition, so it would reject the very case it exists to serve.
            if (existing.EffectiveFrom >= request.EffectiveFrom
                || (existing.EffectiveTo.HasValue && existing.EffectiveTo.Value >= request.EffectiveFrom))
            {
                throw new InvalidOperationException(
                    $"A rate already covers section '{section.Code}' for "
                    + $"{request.PayeeCategory} payees on or after {request.EffectiveFrom:dd-MMM-yyyy}. "
                    + "Rates are added as non-overlapping dated rows, never overlaid.");
            }

            if (!existing.EffectiveTo.HasValue)
            {
                existing.CloseAsOf(request.EffectiveFrom.AddDays(-1));
                _unitOfWork.Repository<TaxRateSchedule>().Update(existing);
            }
        }

        var created = new TaxRateSchedule(
            companyId,
            request.TaxSectionId,
            request.PayeeCategory,
            request.RatePercentMinor,
            request.EffectiveFrom,
            request.SingleTransactionThresholdMinor,
            request.AnnualThresholdMinor,
            request.RateWithoutPanPercentMinor,
            effectiveTo: null);

        await _unitOfWork.Repository<TaxRateSchedule>().AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(created, new Dictionary<Guid, string> { [section.Id] = section.Code });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaxPayeeProfileSummary>> ListPayeeProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var profiles = await _unitOfWork.Repository<TaxPayeeProfile>()
            .ListAsync(new ProfilesOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        var names = await LedgerNameLookupAsync(companyId, cancellationToken).ConfigureAwait(false);

        return profiles
            .Select(profile => ToSummary(profile, names))
            .OrderBy(summary => summary.LedgerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<TaxPayeeProfileSummary> SavePayeeProfileAsync(
        SaveTaxPayeeProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(request.LedgerId, cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null || ledger.CompanyId != companyId)
        {
            throw new InvalidOperationException("That ledger does not exist in this company's books.");
        }

        var existing = await _unitOfWork.Repository<TaxPayeeProfile>()
            .SingleOrDefaultAsync(new ProfileOfLedger(companyId, request.LedgerId), cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var created = new TaxPayeeProfile(
                companyId,
                request.LedgerId,
                request.PayeeCategory,
                request.Pan,
                request.IsDeductionApplicable,
                request.DeclaresDeductingOnPurchases);

            await _unitOfWork.Repository<TaxPayeeProfile>().AddAsync(created, cancellationToken)
                .ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ToSummary(created, new Dictionary<Guid, string> { [ledger.Id] = ledger.Name });
        }

        existing.SetPayeeCategory(request.PayeeCategory);
        existing.SetPan(request.Pan);
        existing.SetDeductionApplicable(request.IsDeductionApplicable);
        existing.SetDeclaresDeductingOnPurchases(request.DeclaresDeductingOnPurchases);

        _unitOfWork.Repository<TaxPayeeProfile>().Update(existing);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(existing, new Dictionary<Guid, string> { [ledger.Id] = ledger.Name });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LowerDeductionCertificateSummary>> ListCertificatesAsync(
        Guid? ledgerId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var certificates = await _unitOfWork.Repository<LowerDeductionCertificate>()
            .ListAsync(new CertificatesOfCompany(companyId, ledgerId), cancellationToken)
            .ConfigureAwait(false);

        var names = await LedgerNameLookupAsync(companyId, cancellationToken).ConfigureAwait(false);
        var codes = await SectionCodeLookupAsync(companyId, cancellationToken).ConfigureAwait(false);

        return certificates
            .OrderByDescending(certificate => certificate.ValidFrom)
            .Select(certificate => ToSummary(certificate, names, codes))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<LowerDeductionCertificateSummary> AddCertificateAsync(
        SaveLowerDeductionCertificateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var section = await RequireSectionAsync(companyId, request.TaxSectionId, cancellationToken)
            .ConfigureAwait(false);

        var ledger = await _unitOfWork.Repository<Ledger>()
            .GetByIdAsync(request.LedgerId, cancellationToken)
            .ConfigureAwait(false);

        if (ledger is null || ledger.CompanyId != companyId)
        {
            throw new InvalidOperationException("That ledger does not exist in this company's books.");
        }

        // Two active certificates covering one day for one party and section would make the rate
        // depend on row order. Refused here rather than resolved by a tie-break nobody can predict.
        var overlapping = (await _unitOfWork.Repository<LowerDeductionCertificate>()
                .ListAsync(
                    new CertificatesOfLedgerAndSection(companyId, request.LedgerId, request.TaxSectionId),
                    cancellationToken)
                .ConfigureAwait(false))
            .Where(certificate => certificate.IsActive
                && certificate.ValidFrom <= request.ValidTo
                && request.ValidFrom <= certificate.ValidTo);

        if (overlapping.Any())
        {
            throw new InvalidOperationException(
                $"An active certificate for this party under section '{section.Code}' already "
                + "covers part of that period. Withdraw it first.");
        }

        var created = new LowerDeductionCertificate(
            companyId,
            request.LedgerId,
            request.TaxSectionId,
            request.CertificateNumber,
            request.RatePercentMinor,
            request.ValidFrom,
            request.ValidTo,
            request.CeilingAmountMinor);

        await _unitOfWork.Repository<LowerDeductionCertificate>()
            .AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(
            created,
            new Dictionary<Guid, string> { [ledger.Id] = ledger.Name },
            new Dictionary<Guid, string> { [section.Id] = section.Code });
    }

    /// <inheritdoc />
    public async Task<LowerDeductionCertificateSummary> SetCertificateActiveAsync(
        Guid certificateId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var certificate = await _unitOfWork.Repository<LowerDeductionCertificate>()
            .GetByIdAsync(certificateId, cancellationToken)
            .ConfigureAwait(false);

        if (certificate is null || certificate.CompanyId != companyId)
        {
            throw new InvalidOperationException("That certificate does not exist in this company's books.");
        }

        if (isActive)
        {
            certificate.Reinstate();
        }
        else
        {
            certificate.Withdraw();
        }

        _unitOfWork.Repository<LowerDeductionCertificate>().Update(certificate);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var names = await LedgerNameLookupAsync(companyId, cancellationToken).ConfigureAwait(false);
        var codes = await SectionCodeLookupAsync(companyId, cancellationToken).ConfigureAwait(false);

        return ToSummary(certificate, names, codes);
    }

    /// <inheritdoc />
    public async Task<ResolvedTaxRate?> ResolveRateAsync(
        Guid ledgerId,
        Guid taxSectionId,
        DateOnly onDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var section = await _unitOfWork.Repository<TaxSection>()
            .GetByIdAsync(taxSectionId, cancellationToken)
            .ConfigureAwait(false);

        if (section is null || section.CompanyId != companyId)
        {
            return null;
        }

        var profile = await _unitOfWork.Repository<TaxPayeeProfile>()
            .SingleOrDefaultAsync(new ProfileOfLedger(companyId, ledgerId), cancellationToken)
            .ConfigureAwait(false);

        // No profile means no class of person has been recorded, which is not the same as "Any":
        // the ledger has simply never been set up as a payee. Treated as the unsplit class so the
        // rate still resolves, because a section with a single Any row must not go silent just
        // because nobody classified the party.
        var category = profile?.PayeeCategory ?? PayeeCategory.Any;
        var hasPan = profile?.HasPan ?? false;

        var covering = (await _unitOfWork.Repository<TaxRateSchedule>()
                .ListAsync(new SchedulesOfSection(companyId, taxSectionId), cancellationToken)
                .ConfigureAwait(false))
            .Where(schedule => schedule.Covers(onDate) && schedule.Prices(category))

            // A section split by class has both an exact row and possibly an Any fallback. The
            // exact one wins; ordering makes that a rule rather than an accident of row order.
            .OrderBy(schedule => schedule.PayeeCategory == PayeeCategory.Any ? 1 : 0)
            .FirstOrDefault();

        if (covering is null)
        {
            return null;
        }

        var useNoPanRate = !hasPan && covering.RateWithoutPanPercentMinor.HasValue;

        return new ResolvedTaxRate(
            covering.Id,
            section.Id,
            section.Code,
            section.Kind,
            covering.PayeeCategory,
            useNoPanRate ? covering.RateWithoutPanPercentMinor!.Value : covering.RatePercentMinor,
            covering.SingleTransactionThresholdMinor,
            covering.AnnualThresholdMinor,
            hasPan,
            useNoPanRate);
    }

    private async Task<TaxSection> RequireSectionAsync(
        Guid companyId,
        Guid sectionId,
        CancellationToken cancellationToken)
    {
        var section = await _unitOfWork.Repository<TaxSection>()
            .GetByIdAsync(sectionId, cancellationToken)
            .ConfigureAwait(false);

        return section is null || section.CompanyId != companyId
            ? throw new InvalidOperationException("That section does not exist in this company's books.")
            : section;
    }

    private async Task<Dictionary<Guid, string>> SectionCodeLookupAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<TaxSection>()
            .ListAsync(new SectionsOfCompany(companyId, null, includeInactive: true), cancellationToken)
            .ConfigureAwait(false))
        .ToDictionary(section => section.Id, section => section.Code);

    private async Task<Dictionary<Guid, string>> LedgerNameLookupAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<Ledger>()
            .ListAsync(new LedgersOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false))
        .ToDictionary(ledger => ledger.Id, ledger => ledger.Name);

    private static TaxSectionSummary ToSummary(TaxSection section) =>
        new(section.Id,
            section.Kind,
            section.Code,
            section.Name,
            section.StatutePaymentCode,
            section.IsActive);

    private static TaxRateScheduleSummary ToSummary(
        TaxRateSchedule schedule,
        Dictionary<Guid, string> sectionCodes) =>
        new(schedule.Id,
            schedule.TaxSectionId,
            sectionCodes.TryGetValue(schedule.TaxSectionId, out var code) ? code : "(unknown)",
            schedule.PayeeCategory,
            schedule.RatePercentMinor,
            schedule.RateWithoutPanPercentMinor,
            schedule.SingleTransactionThresholdMinor,
            schedule.AnnualThresholdMinor,
            schedule.EffectiveFrom,
            schedule.EffectiveTo);

    private static TaxPayeeProfileSummary ToSummary(
        TaxPayeeProfile profile,
        Dictionary<Guid, string> ledgerNames) =>
        new(profile.Id,
            profile.LedgerId,
            ledgerNames.TryGetValue(profile.LedgerId, out var name) ? name : "(unknown)",
            profile.PayeeCategory,
            profile.Pan,
            profile.HasPan,
            profile.IsDeductionApplicable,
            profile.DeclaresDeductingOnPurchases);

    private static LowerDeductionCertificateSummary ToSummary(
        LowerDeductionCertificate certificate,
        Dictionary<Guid, string> ledgerNames,
        Dictionary<Guid, string> sectionCodes) =>
        new(certificate.Id,
            certificate.LedgerId,
            ledgerNames.TryGetValue(certificate.LedgerId, out var name) ? name : "(unknown)",
            certificate.TaxSectionId,
            sectionCodes.TryGetValue(certificate.TaxSectionId, out var code) ? code : "(unknown)",
            certificate.CertificateNumber,
            certificate.RatePercentMinor,
            certificate.CeilingAmountMinor,
            certificate.ValidFrom,
            certificate.ValidTo,
            certificate.IsActive);

    private sealed class SectionsOfCompany : Specification<TaxSection>
    {
        public SectionsOfCompany(Guid companyId, TaxSectionKind? kind, bool includeInactive)
        {
            Where(section => section.CompanyId == companyId
                && (includeInactive || section.IsActive)
                && (!kind.HasValue || section.Kind == kind.Value));
            ApplyNoTracking();
        }
    }

    private sealed class ActiveSectionByCode : Specification<TaxSection>
    {
        public ActiveSectionByCode(Guid companyId, TaxSectionKind kind, string code)
        {
            Where(section => section.CompanyId == companyId
                && section.Kind == kind
                && section.Code == code
                && section.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class SchedulesOfCompany : Specification<TaxRateSchedule>
    {
        public SchedulesOfCompany(Guid companyId, Guid? taxSectionId)
        {
            Where(schedule => schedule.CompanyId == companyId
                && (!taxSectionId.HasValue || schedule.TaxSectionId == taxSectionId.Value));
            ApplyNoTracking();
        }
    }

    private sealed class SchedulesOfSection : Specification<TaxRateSchedule>
    {
        public SchedulesOfSection(Guid companyId, Guid taxSectionId)
        {
            Where(schedule => schedule.CompanyId == companyId
                && schedule.TaxSectionId == taxSectionId);
            ApplyNoTracking();
        }
    }

    private sealed class SchedulesOfSectionAndCategory : Specification<TaxRateSchedule>
    {
        public SchedulesOfSectionAndCategory(Guid companyId, Guid taxSectionId, PayeeCategory category)
        {
            Where(schedule => schedule.CompanyId == companyId
                && schedule.TaxSectionId == taxSectionId
                && schedule.PayeeCategory == category);
        }
    }

    private sealed class ProfilesOfCompany : Specification<TaxPayeeProfile>
    {
        public ProfilesOfCompany(Guid companyId)
        {
            Where(profile => profile.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class ProfileOfLedger : Specification<TaxPayeeProfile>
    {
        public ProfileOfLedger(Guid companyId, Guid ledgerId)
        {
            Where(profile => profile.CompanyId == companyId && profile.LedgerId == ledgerId);
        }
    }

    private sealed class CertificatesOfCompany : Specification<LowerDeductionCertificate>
    {
        public CertificatesOfCompany(Guid companyId, Guid? ledgerId)
        {
            Where(certificate => certificate.CompanyId == companyId
                && (!ledgerId.HasValue || certificate.LedgerId == ledgerId.Value));
            ApplyNoTracking();
        }
    }

    private sealed class CertificatesOfLedgerAndSection : Specification<LowerDeductionCertificate>
    {
        public CertificatesOfLedgerAndSection(Guid companyId, Guid ledgerId, Guid taxSectionId)
        {
            Where(certificate => certificate.CompanyId == companyId
                && certificate.LedgerId == ledgerId
                && certificate.TaxSectionId == taxSectionId);
            ApplyNoTracking();
        }
    }

    private sealed class LedgersOfCompany : Specification<Ledger>
    {
        public LedgersOfCompany(Guid companyId)
        {
            Where(ledger => ledger.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
