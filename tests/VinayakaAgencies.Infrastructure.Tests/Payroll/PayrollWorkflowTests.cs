using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Payroll;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Payroll;

/// <summary>
/// The Phase 7b payroll slice against a real SQLite file: a salary voucher posting through the
/// journal engine, statutory contributions from dated config rows, and the flags-off case
/// checkpoint C7.7 pins (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// Every rate, ceiling and slab below is chosen by the test. §2 extends non-negotiable #6 to all
/// of them, so nothing in <c>src/</c> supplies one, and these tests would pass unchanged after a
/// notification moved every figure.
/// </remarks>
public sealed class PayrollWorkflowTests
{
    private static readonly DateOnly April = new(2026, 4, 1);
    private static readonly DateOnly MayEnd = new(2026, 5, 31);

    private const long TwelvePercent = 120_000L;
    private const long FortyPercent = 400_000L;

    // --------------------------------------------------- C7.7 — with every flag off, nothing runs

    [Fact]
    public async Task WithEveryStatutoryFlagOff_NoStatutoryLineIsProducedAtAll()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        // The rates and slabs exist and are in force. The only thing missing is the declaration
        // that the business is covered — and that alone must keep the whole path silent.
        await ConfigureStatutesAsync(host, world);

        var preview = await PreviewAsync(host, MayEnd);

        var slip = preview.PaySlips.ShouldHaveSingleItem();

        slip.Lines.ShouldAllBe(line => line.Statute == null);
        slip.EmployerContributions.ShouldBe(0m);

        // Gross and net are the structure alone — no deduction was invented and none was zeroed.
        slip.GrossEarnings.ShouldBe(28_000m);   // 20,000 basic + 40% HRA
        slip.NetPay.ShouldBe(28_000m);

        var posted = await PostAsync(host, world, MayEnd);
        posted.EmployerContributions.ShouldBe(0m);

        // And the challan report says nothing rather than three zero rows, because a zero row
        // claims a scheme applies and happened to cost nothing.
        var challan = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPayrollRunService>().ChallanSummaryAsync(April, MayEnd));

        challan.ShouldBeEmpty();
    }

    [Fact]
    public async Task TurningOneFlagOn_ProducesThatStatuteAndNoOther()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);
        await ConfigureStatutesAsync(host, world);

        await SaveSettingsAsync(host, providentFund: true);

        var slip = (await PreviewAsync(host, MayEnd)).PaySlips.ShouldHaveSingleItem();

        var statutory = slip.Lines.Where(line => line.Statute.HasValue).ToList();

        statutory.ShouldAllBe(line => line.Statute == StatuteKind.ProvidentFund);
        statutory.Count.ShouldBe(2, "the employee share and the employer's own");

        // Gross 28,000, capped at the 15,000 wage ceiling, at 12%.
        slip.Lines.Single(line =>
                line.Statute == StatuteKind.ProvidentFund && line.Type == PayHeadType.Deduction)
            .Amount.ShouldBe(1_800m);

        slip.NetPay.ShouldBe(26_200m, "the employer's share never reduces net pay");
        slip.EmployerContributions.ShouldBe(1_800m);
    }

    // ------------------------------------------------- the salary voucher posts through Phase 1

    [Fact]
    public async Task PostAsync_PostsABalancedJournalVoucherAndFreezesThePaySlip()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);
        await ConfigureStatutesAsync(host, world);
        await SaveSettingsAsync(host, providentFund: true, professionalTax: true);

        var posted = await PostAsync(host, world, MayEnd);

        posted.EmployeeCount.ShouldBe(1);
        posted.GrossEarnings.ShouldBe(28_000m);

        // PF 1,800 + PT 200 on a 28,000 wage.
        posted.TotalDeductions.ShouldBe(2_000m);
        posted.NetPayable.ShouldBe(26_000m);
        posted.PostedVoucherNumber.ShouldNotBeNullOrWhiteSpace();

        var lines = await ReadJournalAsync(host, posted.PostedVoucherId);

        lines.Sum(line => line.AmountMinor).ShouldBe(0L, "a posted voucher must balance exactly");

        // Net pay reached the salary payable liability.
        var payableId = await LedgerIdAsync(host, "Salary Payable");
        lines.Single(line => line.LedgerId == payableId).AmountMinor.ShouldBe(-260_000_000L);

        // The pay slip reads back from the frozen lines, not from the masters.
        var slip = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPayrollRunService>()
                .GetPaySlipAsync(posted.SalaryVoucherId, world.EmployeeId));

        slip.ShouldNotBeNull();
        slip.NetPay.ShouldBe(26_000m);
        slip.Lines.Count.ShouldBe(5, "basic, HRA, PF employee, PF employer, PT");
    }

    [Fact]
    public async Task PostAsync_ForAMonthAlreadyPosted_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await PostAsync(host, world, MayEnd);

        await Should.ThrowAsync<InvalidOperationException>(() => PostAsync(host, world, MayEnd));
    }

    [Fact]
    public async Task ChallanSummary_CountsEmployeesAndSplitsTheTwoShares()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);
        await ConfigureStatutesAsync(host, world);
        await SaveSettingsAsync(host, providentFund: true);

        await PostAsync(host, world, MayEnd);

        var challan = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPayrollRunService>().ChallanSummaryAsync(April, MayEnd));

        var line = challan.ShouldHaveSingleItem();
        line.Statute.ShouldBe(StatuteKind.ProvidentFund);
        line.EmployeeCount.ShouldBe(1);
        line.EmployeeShare.ShouldBe(1_800m);
        line.EmployerShare.ShouldBe(1_800m);
        line.Total.ShouldBe(3_600m, "the challan is the sum of both shares");
    }

    // ----------------------------------------------------------------- dated rows, as elsewhere

    [Fact]
    public async Task ARateChange_AppliesToTheMonthAfterItAndNotBefore()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);
        await ConfigureStatutesAsync(host, world);
        await SaveSettingsAsync(host, providentFund: true);

        // From 1 June the employee share halves. The April row closes at 31 May, automatically.
        await host.InScopeAsync(sp => sp.GetRequiredService<IPayrollMasterService>()
            .AddStatutoryRateAsync(new SaveStatutoryRateRequest(
                StatuteKind.ProvidentFund,
                EmployeeRatePercentMinor: 60_000L,
                EmployerRatePercentMinor: 60_000L,
                EffectiveFrom: new DateOnly(2026, 6, 1),
                WageCeiling: 15_000m)));

        var may = await PreviewAsync(host, MayEnd);
        var june = await PreviewAsync(host, new DateOnly(2026, 6, 30));

        PfDeduction(may).ShouldBe(1_800m, "May is priced by the rate in force in May");
        PfDeduction(june).ShouldBe(900m);

        var rates = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPayrollMasterService>()
                .ListStatutoryRatesAsync(StatuteKind.ProvidentFund));

        rates.Count.ShouldBe(2, "history is closed, never rewritten");
        rates.Single(rate => rate.EmployeeRatePercentMinor == TwelvePercent).EffectiveTo
            .ShouldBe(MayEnd);
    }

    [Fact]
    public async Task ARaise_DoesNotChangeWhatAnEarlierMonthPaid()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        // Basic rises to 30,000 from 1 June.
        await host.InScopeAsync(sp => sp.GetRequiredService<IPayrollMasterService>()
            .AddStructureLineAsync(new SaveSalaryStructureLineRequest(
                world.EmployeeId,
                world.BasicHeadId,
                new DateOnly(2026, 6, 1),
                Amount: 30_000m)));

        (await PreviewAsync(host, MayEnd)).GrossEarnings.ShouldBe(28_000m);
        (await PreviewAsync(host, new DateOnly(2026, 6, 30))).GrossEarnings.ShouldBe(42_000m);
    }

    [Fact]
    public async Task AnEmployeeWhoLeft_DropsOutOfTheMonthAfterTheirLastDay()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await host.InScopeAsync(sp => sp.GetRequiredService<IPayrollMasterService>()
            .UpdateEmployeeAsync(world.EmployeeId, new SaveEmployeeRequest(
                "E001",
                "Suresh Kumar",
                April,
                LeftOn: MayEnd)));

        (await PreviewAsync(host, MayEnd)).PaySlips.Count
            .ShouldBe(1, "their final month still has to be paid");

        (await PreviewAsync(host, new DateOnly(2026, 6, 30))).PaySlips
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task WithAFlagOnButNoRateInForce_TheRunWarnsRatherThanSilentlyUnderRemitting()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        // The PF pay heads exist and the flag is on, but no contribution rate was ever entered.
        await AddStatutoryHeadsAsync(host, world);
        await SaveSettingsAsync(host, providentFund: true);

        var preview = await PreviewAsync(host, MayEnd);

        preview.Warnings.ShouldNotBeEmpty();
        preview.Warnings.ShouldContain(warning => warning.Contains("ProvidentFund"));
        preview.PaySlips.ShouldHaveSingleItem().TotalDeductions.ShouldBe(0m);
    }

    [Fact]
    public async Task AStatutoryPayHeadWithNoStatute_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await Should.ThrowAsync<ArgumentException>(() =>
            host.InScopeAsync(sp => sp.GetRequiredService<IPayrollMasterService>()
                .CreatePayHeadAsync(new SavePayHeadRequest(
                    "Mystery deduction",
                    PayHeadType.Deduction,
                    PayComputationBasis.Statutory,
                    world.SalaryExpenseLedgerId))));
    }

    // -------------------------------------------------------------------------------- fixtures

    private sealed record PayrollWorld(
        Guid CompanyId,
        Guid EmployeeId,
        Guid BasicHeadId,
        Guid SalaryExpenseLedgerId,
        Guid PfPayableLedgerId,
        Guid PtPayableLedgerId,
        Guid EmployerExpenseLedgerId,
        Guid JournalVoucherTypeId);

    /// <summary>
    /// A provisioned company, one employee on 20,000 basic plus 40% HRA, and nothing statutory.
    /// </summary>
    private static async Task<PayrollWorld> ArrangeAsync(SqliteTestHost host)
    {
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
            sp.GetRequiredService<ICompanyProvisioningService>().EnsurePayrollSeedAsync(companyId));

        return await host.InScopeAsync(async sp =>
        {
            var masters = sp.GetRequiredService<IPayrollMasterService>();
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var ledgers = await unitOfWork.Repository<Ledger>().ListAsync();
            Guid LedgerId(string name) => ledgers
                .Single(ledger => ledger.NameNormalised == NameNormaliser.Normalise(name))
                .Id;

            var salaryExpense = LedgerId("Salaries");
            var employerExpense = LedgerId("Employer Statutory Contributions");

            var employee = await masters.CreateEmployeeAsync(
                new SaveEmployeeRequest("E001", "Suresh Kumar", April, Pan: "ABCPK1234D"));

            var basic = await masters.CreatePayHeadAsync(new SavePayHeadRequest(
                PayrollRunService.BasicPayHeadName,
                PayHeadType.Earning,
                PayComputationBasis.FlatAmount,
                salaryExpense,
                SortOrder: 1));

            var hra = await masters.CreatePayHeadAsync(new SavePayHeadRequest(
                "HRA",
                PayHeadType.Earning,
                PayComputationBasis.PercentOfBasic,
                salaryExpense,
                SortOrder: 2));

            await masters.AddStructureLineAsync(new SaveSalaryStructureLineRequest(
                employee.Id, basic.Id, April, Amount: 20_000m));
            await masters.AddStructureLineAsync(new SaveSalaryStructureLineRequest(
                employee.Id, hra.Id, April, PercentMinor: FortyPercent));

            var voucherTypes = await unitOfWork.Repository<VoucherType>().ListAsync();

            return new PayrollWorld(
                companyId,
                employee.Id,
                basic.Id,
                salaryExpense,
                LedgerId("PF Payable"),
                LedgerId("Professional Tax Payable"),
                employerExpense,
                voucherTypes.First(type => type.Kind == VoucherKind.Journal).Id);
        });
    }

    /// <summary>Adds the statutory pay heads, the PF rate and the PT slabs — all as data rows.</summary>
    private static async Task ConfigureStatutesAsync(SqliteTestHost host, PayrollWorld world)
    {
        await AddStatutoryHeadsAsync(host, world);

        await host.InScopeAsync(async sp =>
        {
            var masters = sp.GetRequiredService<IPayrollMasterService>();

            await masters.AddStatutoryRateAsync(new SaveStatutoryRateRequest(
                StatuteKind.ProvidentFund,
                TwelvePercent,
                TwelvePercent,
                April,
                WageCeiling: 15_000m));

            await masters.AddProfessionalTaxSlabAsync(
                new SaveProfessionalTaxSlabRequest(15_000m, 25_000m, 150m, April));
            await masters.AddProfessionalTaxSlabAsync(
                new SaveProfessionalTaxSlabRequest(25_000m, null, 200m, April));
        });
    }

    private static Task AddStatutoryHeadsAsync(SqliteTestHost host, PayrollWorld world) =>
        host.InScopeAsync(async sp =>
        {
            var masters = sp.GetRequiredService<IPayrollMasterService>();

            await masters.CreatePayHeadAsync(new SavePayHeadRequest(
                "PF (employee)",
                PayHeadType.Deduction,
                PayComputationBasis.Statutory,
                world.PfPayableLedgerId,
                StatuteKind.ProvidentFund,
                SortOrder: 10));

            await masters.CreatePayHeadAsync(new SavePayHeadRequest(
                "PF (employer)",
                PayHeadType.EmployerContribution,
                PayComputationBasis.Statutory,
                world.EmployerExpenseLedgerId,
                StatuteKind.ProvidentFund,
                SortOrder: 11));

            await masters.CreatePayHeadAsync(new SavePayHeadRequest(
                "Professional Tax",
                PayHeadType.Deduction,
                PayComputationBasis.Statutory,
                world.PtPayableLedgerId,
                StatuteKind.ProfessionalTax,
                SortOrder: 12));
        });

    private static Task<PayrollSettingsSummary> SaveSettingsAsync(
        SqliteTestHost host,
        bool providentFund = false,
        bool stateInsurance = false,
        bool professionalTax = false) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IPayrollMasterService>()
            .SaveSettingsAsync(new SavePayrollSettingsRequest(
                "Salary Payable",
                providentFund,
                stateInsurance,
                professionalTax)));

    private static Task<PayrollPreview> PreviewAsync(SqliteTestHost host, DateOnly periodEnd) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IPayrollRunService>().PreviewAsync(periodEnd));

    private static Task<PostedPayrollSummary> PostAsync(
        SqliteTestHost host,
        PayrollWorld world,
        DateOnly periodEnd) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IPayrollRunService>()
            .PostAsync(periodEnd, world.JournalVoucherTypeId));

    private static decimal PfDeduction(PayrollPreview preview) =>
        preview.PaySlips
            .SelectMany(slip => slip.Lines)
            .Where(line => line.Statute == StatuteKind.ProvidentFund
                && line.Type == PayHeadType.Deduction)
            .Sum(line => line.Amount);

    private static Task<List<JournalLine>> ReadJournalAsync(SqliteTestHost host, Guid voucherId) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            return (await unitOfWork.Repository<JournalLine>().ListAsync())
                .Where(line => line.VoucherId == voucherId)
                .ToList();
        });

    private static Task<Guid> LedgerIdAsync(SqliteTestHost host, string name) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            return (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(ledger => ledger.Name == name)
                .Id;
        });
}
