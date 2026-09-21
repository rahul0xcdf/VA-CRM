using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Application.Payroll;

/// <summary>Read model for one person on the payroll.</summary>
public sealed record EmployeeSummary(
    Guid Id,
    string Code,
    string Name,
    string? Designation,
    DateOnly JoinedOn,
    DateOnly? LeftOn,
    string? Pan,
    string? Uan,
    string? EsiNumber,
    bool IsActive);

/// <summary>Creates or updates an employee. The code is fixed at creation.</summary>
public sealed record SaveEmployeeRequest(
    string Code,
    string Name,
    DateOnly JoinedOn,
    string? Designation = null,
    string? Pan = null,
    string? Uan = null,
    string? EsiNumber = null,
    DateOnly? LeftOn = null);

/// <summary>Read model for a pay head.</summary>
public sealed record PayHeadSummary(
    Guid Id,
    string Name,
    PayHeadType Type,
    PayComputationBasis Basis,
    Guid LedgerId,
    string LedgerName,
    StatuteKind? Statute,
    int SortOrder,
    bool IsActive);

/// <summary>Creates a pay head. A statutory basis and a statute go together or not at all.</summary>
public sealed record SavePayHeadRequest(
    string Name,
    PayHeadType Type,
    PayComputationBasis Basis,
    Guid LedgerId,
    StatuteKind? Statute = null,
    int SortOrder = 0);

/// <summary>Read model for one line of an employee's salary structure.</summary>
public sealed record SalaryStructureLineSummary(
    Guid Id,
    Guid EmployeeId,
    Guid PayHeadId,
    string PayHeadName,
    PayHeadType Type,
    PayComputationBasis Basis,
    decimal? Amount,
    long? PercentMinor,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>
/// Appends a structure line, closing the one it supersedes the day before it begins. Lines are
/// never edited: a raise in October must not change what September's pay slip said.
/// </summary>
public sealed record SaveSalaryStructureLineRequest(
    Guid EmployeeId,
    Guid PayHeadId,
    DateOnly EffectiveFrom,
    decimal? Amount = null,
    long? PercentMinor = null);

/// <summary>Read model for a dated PF or ESI contribution row.</summary>
public sealed record StatutoryRateSummary(
    Guid Id,
    StatuteKind Statute,
    long EmployeeRatePercentMinor,
    long EmployerRatePercentMinor,
    decimal? WageCeiling,
    decimal? EligibilityCeiling,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>Appends a dated contribution row for PF or ESI.</summary>
public sealed record SaveStatutoryRateRequest(
    StatuteKind Statute,
    long EmployeeRatePercentMinor,
    long EmployerRatePercentMinor,
    DateOnly EffectiveFrom,
    decimal? WageCeiling = null,
    decimal? EligibilityCeiling = null);

/// <summary>Read model for one Professional Tax band.</summary>
public sealed record ProfessionalTaxSlabSummary(
    Guid Id,
    decimal WageFrom,
    decimal? WageTo,
    decimal TaxAmount,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>Adds one Professional Tax band. The upper bound is exclusive; null is the top band.</summary>
public sealed record SaveProfessionalTaxSlabRequest(
    decimal WageFrom,
    decimal? WageTo,
    decimal TaxAmount,
    DateOnly EffectiveFrom);

/// <summary>Read model for the payroll configuration.</summary>
public sealed record PayrollSettingsSummary(
    string SalaryPayableLedgerName,
    bool IsProvidentFundEnabled,
    bool IsEmployeeStateInsuranceEnabled,
    bool IsProfessionalTaxEnabled);

/// <summary>Saves the configuration — which statutes are in force (checkpoint C7.7).</summary>
public sealed record SavePayrollSettingsRequest(
    string SalaryPayableLedgerName,
    bool IsProvidentFundEnabled = false,
    bool IsEmployeeStateInsuranceEnabled = false,
    bool IsProfessionalTaxEnabled = false);

/// <summary>One computed line of one employee's pay slip.</summary>
public sealed record PaySlipLine(
    Guid PayHeadId,
    string PayHeadName,
    PayHeadType Type,
    decimal Amount,
    StatuteKind? Statute,
    int SortOrder);

/// <summary>
/// One employee's pay slip for a month, before or after posting.
/// </summary>
/// <remarks>
/// <see cref="Lines"/> carries no statutory row at all when the matching flag is off — not a zero
/// row. A zero claims the scheme applies and happened to cost nothing, which is a different and
/// false statement (checkpoint C7.7).
/// </remarks>
public sealed record PaySlip(
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    decimal GrossEarnings,
    decimal TotalDeductions,
    decimal EmployerContributions,
    decimal NetPay,
    IReadOnlyList<PaySlipLine> Lines);

/// <summary>A whole month's run, computed but not yet posted.</summary>
public sealed record PayrollPreview(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrossEarnings,
    decimal TotalDeductions,
    decimal EmployerContributions,
    decimal NetPayable,
    IReadOnlyList<PaySlip> PaySlips,
    IReadOnlyList<string> Warnings);

/// <summary>A posted run, with the journal voucher it went through.</summary>
public sealed record PostedPayrollSummary(
    Guid SalaryVoucherId,
    Guid PostedVoucherId,
    string PostedVoucherNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int EmployeeCount,
    decimal GrossEarnings,
    decimal TotalDeductions,
    decimal EmployerContributions,
    decimal NetPayable);

/// <summary>
/// What has to be remitted for one statute in one period — the figure the owner pays from
/// (§4a D3's "challan-ready output").
/// </summary>
public sealed record StatutoryChallanLine(
    StatuteKind Statute,
    int EmployeeCount,
    decimal EmployeeShare,
    decimal EmployerShare,
    decimal Total);
