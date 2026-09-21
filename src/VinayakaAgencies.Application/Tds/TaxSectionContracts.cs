using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Application.Tds;

/// <summary>Read model for one TDS/TCS section master row.</summary>
public sealed record TaxSectionSummary(
    Guid Id,
    TaxSectionKind Kind,
    string Code,
    string Name,
    string? StatutePaymentCode,
    bool IsActive);

/// <summary>
/// Creates or renames a section. The code itself is only set on creation — a section whose number
/// changed is a different section, which is exactly how the April 2026 renumbering is absorbed.
/// </summary>
public sealed record SaveTaxSectionRequest(
    TaxSectionKind Kind,
    string Code,
    string Name,
    string? StatutePaymentCode = null);

/// <summary>Read model for one dated rate-and-threshold row.</summary>
public sealed record TaxRateScheduleSummary(
    Guid Id,
    Guid TaxSectionId,
    string SectionCode,
    PayeeCategory PayeeCategory,
    long RatePercentMinor,
    long? RateWithoutPanPercentMinor,
    long SingleTransactionThresholdMinor,
    long AnnualThresholdMinor,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>
/// Adds a dated rate row. Rows are appended and the previous open-ended one is closed, never
/// edited: a return filed against the old rate has to stay reproducible (C7.4).
/// </summary>
public sealed record SaveTaxRateScheduleRequest(
    Guid TaxSectionId,
    PayeeCategory PayeeCategory,
    long RatePercentMinor,
    DateOnly EffectiveFrom,
    long SingleTransactionThresholdMinor = 0L,
    long AnnualThresholdMinor = 0L,
    long? RateWithoutPanPercentMinor = null);

/// <summary>Read model for a party's deduction profile.</summary>
public sealed record TaxPayeeProfileSummary(
    Guid Id,
    Guid LedgerId,
    string LedgerName,
    PayeeCategory PayeeCategory,
    string? Pan,
    bool HasPan,
    bool IsDeductionApplicable,
    bool DeclaresDeductingOnPurchases);

/// <summary>Creates or updates the profile for a party ledger. One per ledger.</summary>
public sealed record SaveTaxPayeeProfileRequest(
    Guid LedgerId,
    PayeeCategory PayeeCategory,
    string? Pan = null,
    bool IsDeductionApplicable = true,
    bool DeclaresDeductingOnPurchases = false);

/// <summary>Read model for a Section 197 lower/nil-deduction certificate.</summary>
public sealed record LowerDeductionCertificateSummary(
    Guid Id,
    Guid LedgerId,
    string LedgerName,
    Guid TaxSectionId,
    string SectionCode,
    string CertificateNumber,
    long RatePercentMinor,
    long? CeilingAmountMinor,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    bool IsActive);

/// <summary>Records a certificate issued by the assessing officer.</summary>
public sealed record SaveLowerDeductionCertificateRequest(
    Guid LedgerId,
    Guid TaxSectionId,
    string CertificateNumber,
    long RatePercentMinor,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    long? CeilingAmountMinor = null);

/// <summary>
/// The rate row that governs a payment, with the payee facts that selected it.
/// </summary>
/// <remarks>
/// <see cref="RatePercentMinor"/> is already the applicable rate — the schedule's ordinary rate, or
/// its no-PAN uplift where the payee has furnished none. Resolving that here rather than in the
/// computation keeps Section 206AA in the place that knows what is on file about the payee.
/// </remarks>
public sealed record ResolvedTaxRate(
    Guid ScheduleId,
    Guid TaxSectionId,
    string SectionCode,
    TaxSectionKind Kind,
    PayeeCategory PayeeCategory,
    long RatePercentMinor,
    long SingleTransactionThresholdMinor,
    long AnnualThresholdMinor,
    bool PayeeHadPan,
    bool IsNoPanRate);
