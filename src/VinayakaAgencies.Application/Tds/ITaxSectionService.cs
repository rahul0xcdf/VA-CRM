using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Application.Tds;

/// <summary>
/// The TDS/TCS master data: sections, their dated rate rows, payee profiles and Section 197
/// certificates (phase-7-tds-payroll-banking.md §2, checkpoints C7.2 and C7.4).
/// </summary>
/// <remarks>
/// Everything this service manages is <b>data</b>. Checkpoint C7.2 is the whole point: adding a
/// section is calling <see cref="CreateSectionAsync"/> and
/// <see cref="AddRateScheduleAsync"/> — two rows — and it is usable on the next voucher with no
/// code change and no rebuild. Nothing here special-cases a section number, and nothing anywhere
/// else may either (C7.5).
/// </remarks>
public interface ITaxSectionService
{
    Task<IReadOnlyList<TaxSectionSummary>> ListSectionsAsync(
        TaxSectionKind? kind = null,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<TaxSectionSummary> CreateSectionAsync(
        SaveTaxSectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a section or records its new-regime payment code. The code never changes.</summary>
    Task<TaxSectionSummary> UpdateSectionAsync(
        Guid sectionId,
        SaveTaxSectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a section, or brings a retired one back. Never deletes: posted deductions quote it.
    /// </summary>
    Task<TaxSectionSummary> SetSectionActiveAsync(
        Guid sectionId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaxRateScheduleSummary>> ListRateSchedulesAsync(
        Guid? taxSectionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a dated rate row, closing the open-ended row it supersedes for the same section and
    /// payee class the day before it begins (C7.4).
    /// </summary>
    Task<TaxRateScheduleSummary> AddRateScheduleAsync(
        SaveTaxRateScheduleRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaxPayeeProfileSummary>> ListPayeeProfilesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Creates the profile for a party ledger, or updates the one that exists.</summary>
    Task<TaxPayeeProfileSummary> SavePayeeProfileAsync(
        SaveTaxPayeeProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LowerDeductionCertificateSummary>> ListCertificatesAsync(
        Guid? ledgerId = null,
        CancellationToken cancellationToken = default);

    Task<LowerDeductionCertificateSummary> AddCertificateAsync(
        SaveLowerDeductionCertificateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws a certificate, or reinstates one withdrawn in error.</summary>
    Task<LowerDeductionCertificateSummary> SetCertificateActiveAsync(
        Guid certificateId,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The rate row governing this section, payee and date, with the payee facts that selected it,
    /// or null where nothing covers it.
    /// </summary>
    Task<ResolvedTaxRate?> ResolveRateAsync(
        Guid ledgerId,
        Guid taxSectionId,
        DateOnly onDate,
        CancellationToken cancellationToken = default);
}
