using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Banking;
using VinayakaAgencies.Application.Payroll;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Application.Tds;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Banking;
using VinayakaAgencies.Domain.Payroll;
using VinayakaAgencies.Domain.Printing;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.App.Tests.ViewModels;

/// <summary>
/// Do-nothing service doubles for the Phase 7 screens.
/// </summary>
/// <remarks>
/// These tests are about what a screen declares to the shell — its bars, its accept and cancel
/// commands, and the handful of decisions the view model itself makes. None of that touches a
/// service, so the doubles return empty rather than pretending to be a database: a stub that
/// invented data would let a test pass on figures no real call would produce. The behaviour that
/// does depend on data is tested against a real SQLite file in the infrastructure suite.
/// </remarks>
internal sealed class StubTaxSectionService : ITaxSectionService
{
    public Task<IReadOnlyList<TaxSectionSummary>> ListSectionsAsync(
        TaxSectionKind? kind = null,
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaxSectionSummary>>([]);

    public Task<TaxSectionSummary> CreateSectionAsync(
        SaveTaxSectionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TaxSectionSummary> UpdateSectionAsync(
        Guid sectionId,
        SaveTaxSectionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TaxSectionSummary> SetSectionActiveAsync(
        Guid sectionId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<TaxRateScheduleSummary>> ListRateSchedulesAsync(
        Guid? taxSectionId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaxRateScheduleSummary>>([]);

    public Task<TaxRateScheduleSummary> AddRateScheduleAsync(
        SaveTaxRateScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<TaxPayeeProfileSummary>> ListPayeeProfilesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaxPayeeProfileSummary>>([]);

    public Task<TaxPayeeProfileSummary> SavePayeeProfileAsync(
        SaveTaxPayeeProfileRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<LowerDeductionCertificateSummary>> ListCertificatesAsync(
        Guid? ledgerId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LowerDeductionCertificateSummary>>([]);

    public Task<LowerDeductionCertificateSummary> AddCertificateAsync(
        SaveLowerDeductionCertificateRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<LowerDeductionCertificateSummary> SetCertificateActiveAsync(
        Guid certificateId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResolvedTaxRate?> ResolveRateAsync(
        Guid ledgerId,
        Guid taxSectionId,
        DateOnly onDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ResolvedTaxRate?>(null);
}

internal sealed class StubTaxDeductionService : ITaxDeductionService
{
    public Task<TaxDeductionPreview> ComputeAsync(
        ComputeTaxDeductionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PostedDeductingVoucherSummary> PostAsync(
        PostDeductingVoucherRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<TaxDeductionRow>> ListAsync(
        DateOnly fromDate,
        DateOnly toDate,
        Guid? taxSectionId = null,
        Guid? ledgerId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaxDeductionRow>>([]);

    public Task<IReadOnlyList<TaxChallanLine>> ChallanSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaxChallanLine>>([]);

    public Task<Guid?> ResolveSaleOfGoodsCollectionSectionAsync(
        Guid customerLedgerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(null);
}

internal sealed class StubPayrollMasterService : IPayrollMasterService
{
    public Task<IReadOnlyList<EmployeeSummary>> ListEmployeesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmployeeSummary>>([]);

    public Task<EmployeeSummary> CreateEmployeeAsync(
        SaveEmployeeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<EmployeeSummary> UpdateEmployeeAsync(
        Guid employeeId,
        SaveEmployeeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<EmployeeSummary> SetEmployeeActiveAsync(
        Guid employeeId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PayHeadSummary>> ListPayHeadsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PayHeadSummary>>([]);

    public Task<PayHeadSummary> CreatePayHeadAsync(
        SavePayHeadRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PayHeadSummary> SetPayHeadActiveAsync(
        Guid payHeadId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SalaryStructureLineSummary>> ListStructureAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SalaryStructureLineSummary>>([]);

    public Task<SalaryStructureLineSummary> AddStructureLineAsync(
        SaveSalaryStructureLineRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<StatutoryRateSummary>> ListStatutoryRatesAsync(
        StatuteKind? statute = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StatutoryRateSummary>>([]);

    public Task<StatutoryRateSummary> AddStatutoryRateAsync(
        SaveStatutoryRateRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ProfessionalTaxSlabSummary>> ListProfessionalTaxSlabsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProfessionalTaxSlabSummary>>([]);

    public Task<ProfessionalTaxSlabSummary> AddProfessionalTaxSlabAsync(
        SaveProfessionalTaxSlabRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PayrollSettingsSummary> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PayrollSettingsSummary("Salary Payable", false, false, false));

    public Task<PayrollSettingsSummary> SaveSettingsAsync(
        SavePayrollSettingsRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class StubPayrollRunService : IPayrollRunService
{
    public Task<PayrollPreview> PreviewAsync(
        DateOnly periodEnd,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PostedPayrollSummary> PostAsync(
        DateOnly periodEnd,
        Guid voucherTypeId,
        string? narration = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PostedPayrollSummary>> ListRunsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PostedPayrollSummary>>([]);

    public Task<PaySlip?> GetPaySlipAsync(
        Guid salaryVoucherId,
        Guid employeeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PaySlip?>(null);

    public Task<IReadOnlyList<StatutoryChallanLine>> ChallanSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StatutoryChallanLine>>([]);
}

internal sealed class StubBankingService : IBankingService
{
    public Task<PaymentInstrumentSummary> PostAsync(
        PostBankVoucherRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PaymentInstrumentSummary>> ListAsync(
        DateOnly fromDate,
        DateOnly toDate,
        Guid? bankLedgerId = null,
        PaymentMode? mode = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PaymentInstrumentSummary>>([]);

    public Task<PaymentInstrumentSummary> UpdateInstrumentAsync(
        Guid instrumentId,
        PaymentMode mode,
        DateOnly instrumentDate,
        string? instrumentNumber,
        string? bankReference,
        string? payeeName,
        string? remarks,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ChequePrintData?> GetChequeDataAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ChequePrintData?>(null);

    public Task<PaymentInstrumentSummary> MarkPrintedAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class StubChartOfAccountsService : IChartOfAccountsService
{
    public Task<IReadOnlyList<AccountGroupSummary>> ListGroupsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccountGroupSummary>>([]);

    public Task<IReadOnlyList<LedgerSummary>> ListLedgersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerSummary>>([]);

    public Task<IReadOnlyList<LedgerSummary>> ListBankLedgersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerSummary>>([]);

    public Task<LedgerSummary?> GetLedgerAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<LedgerSummary?>(null);

    public Task<LedgerSummary> CreateLedgerAsync(
        CreateLedgerRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<LedgerSummary> UpdateLedgerAsync(
        UpdateLedgerRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeactivateLedgerAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task ActivateLedgerAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteLedgerAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AccountGroupSummary> CreateGroupAsync(
        CreateAccountGroupRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AccountGroupSummary> RenameGroupAsync(
        Guid id,
        string newName,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class StubPrintTemplateService : IPrintTemplateService
{
    public Task<IReadOnlyList<PrintTemplateSummary>> ListAsync(
        PrintDocumentKind? documentKind = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PrintTemplateSummary>>([]);

    public Task<PrintTemplateSummary?> ResolveDefaultAsync(
        PrintDocumentKind documentKind,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PrintTemplateSummary?>(null);

    public Task<PrintTemplateDetail?> GetAsync(
        Guid templateId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PrintTemplateDetail?>(null);

    public Task<PrintTemplateDetail> CreateAsync(
        CreatePrintTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PrintTemplateDetail> SaveLayoutAsync(
        Guid templateId,
        PrintTemplateLayout layout,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PrintTemplateSummary> SetActiveAsync(
        Guid templateId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class StubVoucherPostingService : IVoucherPostingService
{
    public Task<IReadOnlyList<VoucherTypeSummary>> ListVoucherTypesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VoucherTypeSummary>>([]);

    public Task<PostedVoucherSummary> PostAsync(
        PostVoucherRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PostedVoucherSummary> AlterAsync(
        AlterVoucherRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PostedVoucherSummary> CancelAsync(
        Guid voucherId,
        string reason,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PostedVoucherSummary?> GetCurrentAsync(
        Guid voucherId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PostedVoucherSummary?>(null);
}
