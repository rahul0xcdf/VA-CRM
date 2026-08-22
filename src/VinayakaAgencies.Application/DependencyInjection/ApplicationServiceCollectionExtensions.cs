using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Reporting;
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

        return services;
    }
}
