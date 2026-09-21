using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Application.Banking;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Payroll;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Application.Tds;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application layer. Call <c>AddInfrastructure</c> as well — this layer declares
    /// what it needs (<see cref="IUnitOfWork"/>) but never says how it is satisfied.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<ICurrentUser, EnvironmentCurrentUser>();

        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<ICompanyProvisioningService, CompanyProvisioningService>();

        // Scoped, so the resolved company is cached for one operation and re-read for the next: a
        // singleton would outlive the company row it cached (spec §10).
        services.AddScoped<ICompanyContext, SingleCompanyContext>();

        // Phase 1 accounting: masters, the journal engine and its support providers.
        services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
        services.AddScoped<IFinancialYearProvider, FinancialYearProvider>();
        services.AddScoped<IVoucherNumberAllocator, VoucherNumberAllocator>();
        services.AddScoped<IVoucherPostingService, VoucherPostingService>();
        services.AddScoped<IBankReconciliationService, BankReconciliationService>();

        // Derived reports and the edit-log viewer — reads only, through the reporting seam.
        services.AddScoped<IAccountingReportService, AccountingReportService>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();

        // Phase 2 inventory: masters, the unified stock ledger's posting path, valuation and the
        // stock reports. The voucher service composes the Phase 1 journal engine rather than
        // bypassing it — stock movements reach the books only through IVoucherPostingService.
        services.AddScoped<IInventoryMasterService, InventoryMasterService>();
        services.AddScoped<IStockValuationService, StockValuationService>();
        services.AddScoped<IStockVoucherService, StockVoucherService>();
        services.AddScoped<IStockReportService, StockReportService>();

        // Phase 3 sales & purchase pipeline: documents along the chains, atomic invoice posting,
        // and the print templates those documents render from.
        services.AddScoped<ITradeDocumentService, TradeDocumentService>();
        services.AddScoped<IPrintTemplateService, PrintTemplateService>();

        // Phase 4 customers & pricing: masters, the price tables, sale-price resolution with its
        // three bases, the credit gate and the statement reads (phase-4-customer-pricing.md).
        services.AddScoped<ICustomerCategoryService, CustomerCategoryService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IPriceListService, PriceListService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<ISalesSettingsService, SalesSettingsService>();
        services.AddScoped<ICreditLimitService, CreditLimitService>();
        services.AddScoped<ICustomerStatementService, CustomerStatementService>();

        // Phase 5 GST returns & tax invoice compliance: HSN/SAC and rate masters as data, the
        // posting-time tax computation, and the returns built from posted snapshots
        // (phase-5-gst-returns.md §2).
        services.AddScoped<IHsnSacService, HsnSacService>();
        services.AddScoped<IGstRateService, GstRateService>();
        services.AddScoped<IGstComputationService, GstComputationService>();
        services.AddScoped<IGstReturnService, GstReturnService>();

        // Phase 6 e-invoicing & e-way bill: settings and credentials, the idempotent IRN path and
        // the e-way bill workflow. IIrpClient and ICredentialProtector are ports — Infrastructure
        // supplies them, and tests supply a recorded double while open-questions.md #10 is open
        // (phase-6-einvoice-eway.md §4a).
        services.AddScoped<IEinvoiceSettingsService, EinvoiceSettingsService>();
        services.AddScoped<IEinvoiceService, EinvoiceService>();
        services.AddScoped<IEwayBillService, EwayBillService>();

        // Phase 7a TDS/TCS and banking: the section/rate/threshold masters, the computation and
        // posting path, and the per-company applicability flags. Every figure the engine uses is a
        // data row — no section number or rate appears in src/ (checkpoint C7.5).
        services.AddScoped<ITaxSectionService, TaxSectionService>();
        services.AddScoped<ITaxDeductionService, TaxDeductionService>();
        services.AddScoped<ITdsSettingsService, TdsSettingsService>();

        // Phase 7a banking: payment/receipt settlement, the cheque register, and the data a
        // cheque layout places. Layouts are Phase 3 print templates of kind Cheque (ADR-0004,
        // §4a D4) — there is no second template system.
        services.AddScoped<IBankingService, BankingService>();

        // Phase 7b payroll: employee and pay-head masters, the effective-dated statutory config,
        // and the monthly run that posts through the journal engine. Every statutory figure is a
        // data row, and each statute is gated by its own flag (checkpoint C7.7).
        services.AddScoped<IPayrollMasterService, PayrollMasterService>();
        services.AddScoped<IPayrollRunService, PayrollRunService>();

        return services;
    }
}
