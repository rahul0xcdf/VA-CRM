using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Tds;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// A provisioned company with the ledgers a TDS test needs, and helpers for the section and rate
/// rows each test chooses for itself (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <b>No section, rate or threshold is set up here.</b> Each test states the ones it depends on, in
/// the test, because the figures are the thing under examination — a shared fixture holding "194C at
/// 1%" would make every test read as though the software knew that, which is exactly the property
/// checkpoints C7.2 and C7.5 deny.
/// </remarks>
public sealed record TdsWorld(
    Guid CompanyId,
    Guid ContractorLedgerId,
    Guid CustomerLedgerId,
    Guid ExpenseLedgerId,
    Guid PurchaseVoucherTypeId,
    Guid BankLedgerId);

public static class TdsTestWorld
{
    /// <summary>Provisions a company and the four ledgers the deducting-voucher tests post through.</summary>
    public static async Task<TdsWorld> ArrangeAsync(SqliteTestHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var companyId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");
            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();
            return company.Id;
        });

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().ProvisionAsync(companyId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureTdsSeedAsync(companyId));

        return await host.InScopeAsync(async sp =>
        {
            var chart = sp.GetRequiredService<IChartOfAccountsService>();
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var groups = await unitOfWork.Repository<AccountGroup>().ListAsync();
            Guid GroupId(string name) => groups
                .Single(group => group.NameNormalised == NameNormaliser.Normalise(name))
                .Id;

            var contractor = await chart.CreateLedgerAsync(
                new CreateLedgerRequest("Ravi Transport", GroupId("Sundry Creditors")));
            var customer = await chart.CreateLedgerAsync(
                new CreateLedgerRequest("Deccan Pumps", GroupId("Sundry Debtors")));
            var expense = await chart.CreateLedgerAsync(
                new CreateLedgerRequest("Freight Inward", GroupId("Direct Expenses")));
            var bank = await chart.CreateLedgerAsync(
                new CreateLedgerRequest("Canara Bank CA", GroupId("Bank Accounts")));

            var voucherTypes = await unitOfWork.Repository<VoucherType>().ListAsync();
            var purchase = voucherTypes.First(type => type.Kind == VoucherKind.Purchase);

            return new TdsWorld(
                companyId,
                contractor.Id,
                customer.Id,
                expense.Id,
                purchase.Id,
                bank.Id);
        });
    }

    /// <summary>
    /// Creates a section and one dated rate row for it — the two inserts checkpoint C7.2 says are
    /// all it takes to make a section usable.
    /// </summary>
    public static Task<Guid> AddSectionWithRateAsync(
        SqliteTestHost host,
        string code,
        string name,
        long ratePercentMinor,
        DateOnly effectiveFrom,
        long singleThresholdMinor = 0L,
        long annualThresholdMinor = 0L,
        long? rateWithoutPanPercentMinor = null,
        TaxSectionKind kind = TaxSectionKind.Tds,
        PayeeCategory payeeCategory = PayeeCategory.Any) =>
        host.InScopeAsync(async sp =>
        {
            var sections = sp.GetRequiredService<ITaxSectionService>();

            var section = await sections.CreateSectionAsync(
                new SaveTaxSectionRequest(kind, code, name));

            await sections.AddRateScheduleAsync(new SaveTaxRateScheduleRequest(
                section.Id,
                payeeCategory,
                ratePercentMinor,
                effectiveFrom,
                singleThresholdMinor,
                annualThresholdMinor,
                rateWithoutPanPercentMinor));

            return section.Id;
        });

    /// <summary>Records a party's class of person and PAN, which is what selects the rate row.</summary>
    public static Task ProfileAsync(
        SqliteTestHost host,
        Guid ledgerId,
        PayeeCategory category,
        string? pan = "AAAPZ1234C",
        bool isDeductionApplicable = true,
        bool declaresDeductingOnPurchases = false) =>
        host.InScopeAsync(sp => sp.GetRequiredService<ITaxSectionService>()
            .SavePayeeProfileAsync(new SaveTaxPayeeProfileRequest(
                ledgerId,
                category,
                pan,
                isDeductionApplicable,
                declaresDeductingOnPurchases)));

    /// <summary>Posts one bill against a party, deducting under the given section.</summary>
    public static Task<PostedDeductingVoucherSummary> PostBillAsync(
        SqliteTestHost host,
        TdsWorld world,
        Guid sectionId,
        DateOnly date,
        decimal amount) =>
        host.InScopeAsync(sp => sp.GetRequiredService<ITaxDeductionService>()
            .PostAsync(new PostDeductingVoucherRequest(
                world.PurchaseVoucherTypeId,
                date,
                world.ContractorLedgerId,
                sectionId,
                [new DeductingVoucherLine(world.ExpenseLedgerId, amount)],
                Narration: $"Freight bill {date:dd-MMM-yyyy}")));
}
