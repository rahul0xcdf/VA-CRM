using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Application.Payroll;

/// <summary>
/// The payroll masters: employees, pay heads, salary structures and the statutory config rows
/// (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// Every statutory figure this service manages is a dated row — PF and ESI contribution rates,
/// their wage ceilings, and the Professional Tax slabs. §2 extends non-negotiable #6 to all of
/// them by name, so a rate that changes by notification is a row the owner adds, never a constant
/// anyone edits.
/// </remarks>
public interface IPayrollMasterService
{
    Task<IReadOnlyList<EmployeeSummary>> ListEmployeesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<EmployeeSummary> CreateEmployeeAsync(
        SaveEmployeeRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployeeSummary> UpdateEmployeeAsync(
        Guid employeeId,
        SaveEmployeeRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployeeSummary> SetEmployeeActiveAsync(
        Guid employeeId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayHeadSummary>> ListPayHeadsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<PayHeadSummary> CreatePayHeadAsync(
        SavePayHeadRequest request,
        CancellationToken cancellationToken = default);

    Task<PayHeadSummary> SetPayHeadActiveAsync(
        Guid payHeadId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalaryStructureLineSummary>> ListStructureAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a structure line, closing the open-ended line for the same employee and head the
    /// day before the newcomer begins.
    /// </summary>
    Task<SalaryStructureLineSummary> AddStructureLineAsync(
        SaveSalaryStructureLineRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StatutoryRateSummary>> ListStatutoryRatesAsync(
        StatuteKind? statute = null,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a dated PF or ESI contribution row, closing the one it supersedes.</summary>
    Task<StatutoryRateSummary> AddStatutoryRateAsync(
        SaveStatutoryRateRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfessionalTaxSlabSummary>> ListProfessionalTaxSlabsAsync(
        CancellationToken cancellationToken = default);

    Task<ProfessionalTaxSlabSummary> AddProfessionalTaxSlabAsync(
        SaveProfessionalTaxSlabRequest request,
        CancellationToken cancellationToken = default);

    Task<PayrollSettingsSummary> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<PayrollSettingsSummary> SaveSettingsAsync(
        SavePayrollSettingsRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the payroll: computes a month, posts it through the journal engine, and reports what has
/// to be remitted (phase-7-tds-payroll-banking.md §4a D3, checkpoint C7.7).
/// </summary>
public interface IPayrollRunService
{
    /// <summary>
    /// Computes a month without posting anything. Safe to call repeatedly while the owner checks
    /// the figures.
    /// </summary>
    Task<PayrollPreview> PreviewAsync(
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts the month: the salary voucher, its pay-slip lines, and the journal voucher, in one
    /// transaction.
    /// </summary>
    Task<PostedPayrollSummary> PostAsync(
        DateOnly periodEnd,
        Guid voucherTypeId,
        string? narration = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PostedPayrollSummary>> ListRunsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>One employee's posted pay slip for a month.</summary>
    Task<PaySlip?> GetPaySlipAsync(
        Guid salaryVoucherId,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What must be remitted per statute for the period.
    /// </summary>
    /// <remarks>
    /// Returns an empty list when no statute is in force, rather than three zero rows — a zero
    /// challan line claims a scheme applies (checkpoint C7.7).
    /// </remarks>
    Task<IReadOnlyList<StatutoryChallanLine>> ChallanSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}
