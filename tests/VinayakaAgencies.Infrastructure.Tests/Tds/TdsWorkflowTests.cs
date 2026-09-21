using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Tds;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Tds;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Tds;

/// <summary>
/// The Phase 7a vertical slice against a real SQLite file: a deduction computed from config rows
/// and posted through the journal engine (C7.1), a section added as data alone (C7.2), the
/// cumulative annual threshold (C7.3), a dated rate change (C7.4) and the 194Q/206C(1H) mutual
/// exclusion (C7.6) — phase-7-tds-payroll-banking.md §3.
/// </summary>
/// <remarks>
/// Every section code and every rate in this file is <b>test data the test chooses</b>, not a
/// constant the software knows. That is the property under examination: the same tests would pass
/// against sections named "392(1)" and "Q17" after the April 2026 renumbering, with no code change.
/// </remarks>
public sealed class TdsWorkflowTests
{
    private static readonly DateOnly April = new(2026, 4, 10);
    private static readonly DateOnly May = new(2026, 5, 10);
    private static readonly DateOnly June = new(2026, 6, 10);
    private static readonly DateOnly July = new(2026, 7, 10);

    private const long OnePercent = 10_000L;
    private const long TwoPercent = 20_000L;
    private const long TwentyPercent = 200_000L;

    // -------------------------------------------- C7.1 — computed from config, posted to the books

    [Fact]
    public async Task PostAsync_DeductsUsingTheConfigTableAndPostsABalancedVoucher()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        // 2% for a company payee, deducting from the first rupee — both figures chosen here.
        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host, "TEST-A", "Payments to contractors", TwoPercent, April);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.Other);

        var posted = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 50_000m);

        posted.GrossAmount.ShouldBe(50_000m);
        posted.DeductedAmount.ShouldBe(1_000m);          // 2% of 50,000
        posted.NetPayableToParty.ShouldBe(49_000m);
        posted.Deduction.AppliedRatePercentMinor.ShouldBe(TwoPercent);

        // The books agree: expense debited gross, party credited net, TDS payable credited the tax.
        var lines = await ReadJournalAsync(host, posted.VoucherId);

        lines.Sum(line => line.AmountMinor).ShouldBe(0L, "a posted voucher must balance exactly");
        lines.Single(line => line.LedgerId == world.ExpenseLedgerId).AmountMinor
            .ShouldBe(500_000_000L);
        lines.Single(line => line.LedgerId == world.ContractorLedgerId).AmountMinor
            .ShouldBe(-490_000_000L);

        var tdsLedgerId = await LedgerIdAsync(host, "TDS Payable");
        lines.Single(line => line.LedgerId == tdsLedgerId).AmountMinor.ShouldBe(-10_000_000L);

        // And the snapshot row that a return is built from.
        var register = await RegisterAsync(host, April, July);
        var row = register.ShouldHaveSingleItem();
        row.SectionCode.ShouldBe("TEST-A");
        row.DeductedAmount.ShouldBe(1_000m);
        row.LedgerName.ShouldBe("Ravi Transport");
    }

    [Fact]
    public async Task PostAsync_WithNoRateConfigured_DeductsNothingAndSaysWhy()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        // A section with no rate row at all — the setup gap that otherwise compounds silently.
        var sectionId = await host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<ITaxSectionService>()
                .CreateSectionAsync(new SaveTaxSectionRequest(TaxSectionKind.Tds, "TEST-B", "Rent")))
            .Id);

        var posted = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 50_000m);

        posted.DeductedAmount.ShouldBe(0m);
        posted.Deduction.Reason.ShouldBe(NoDeductionReason.NoScheduleInForce);
        posted.Deduction.Explanation.ShouldContain("No rate");

        // The voucher still posted in full — an unconfigured section must not block a real bill.
        posted.NetPayableToParty.ShouldBe(50_000m);
    }

    // -------------------------------------------------- C7.2 — a new section as a data row alone

    [Fact]
    public async Task AddingASectionAsDataOnly_MakesItUsableOnTheNextVoucher()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        // Nothing in src/ has ever heard of this code. It is two rows, entered at runtime.
        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host, "392(1)", "Renumbered contractor payments", OnePercent, April);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.IndividualOrHuf);

        var posted = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 100_000m);

        posted.DeductedAmount.ShouldBe(1_000m);
        posted.Deduction.SectionCode.ShouldBe("392(1)");

        // The old and the new numbering coexist across the cutover, which is the §2 requirement
        // the renumbering has to land as: two active sections, both usable, neither special.
        var oldSectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host, "TEST-C", "Pre-cutover contractor payments", TwoPercent, April);

        var underOld = await TdsTestWorld.PostBillAsync(host, world, oldSectionId, June, 100_000m);
        underOld.DeductedAmount.ShouldBe(2_000m);
    }

    // ------------------------------------------------------ C7.3 — cumulative annual threshold

    [Fact]
    public async Task TheTransactionThatCrossesTheAnnualLimit_DeductsOnTheCaughtUpBase()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        // 1%, no single-payment test, annual aggregate of ₹1,00,000.
        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host,
            "TEST-D",
            "Contractor payments",
            OnePercent,
            April,
            singleThresholdMinor: 0L,
            annualThresholdMinor: 100_000L * 10_000L);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.IndividualOrHuf);

        // Three payments of ₹40,000. The first two stay under the limit and deduct nothing.
        var first = await TdsTestWorld.PostBillAsync(host, world, sectionId, April, 40_000m);
        var second = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 40_000m);

        first.DeductedAmount.ShouldBe(0m);
        second.DeductedAmount.ShouldBe(0m);
        second.Deduction.Reason.ShouldBe(NoDeductionReason.BelowThreshold);

        // The third carries the year past ₹1,00,000, so it catches up on all three.
        var third = await TdsTestWorld.PostBillAsync(host, world, sectionId, June, 40_000m);

        third.Deduction.IsThresholdCrossingPayment.ShouldBeTrue();
        third.Deduction.TaxableBase.ShouldBe(120_000m, "the whole year to date, not just this bill");
        third.DeductedAmount.ShouldBe(1_200m);

        // A fourth is taxed on itself alone — the catch-up happens once.
        var fourth = await TdsTestWorld.PostBillAsync(host, world, sectionId, July, 40_000m);

        fourth.Deduction.IsThresholdCrossingPayment.ShouldBeFalse();
        fourth.Deduction.TaxableBase.ShouldBe(40_000m);
        fourth.DeductedAmount.ShouldBe(400m);

        // Every rupee taxed exactly once across the year.
        var register = await RegisterAsync(host, April, July);
        register.Sum(row => row.TaxableBase).ShouldBe(160_000m);
        register.Sum(row => row.DeductedAmount).ShouldBe(1_600m);
    }

    [Fact]
    public async Task ASinglePaymentOverTheSingleLimit_IsTaxedWithoutReopeningEarlierOnes()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        // Both tests configured: ₹30,000 per payment, ₹1,00,000 for the year.
        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host,
            "TEST-E",
            "Contractor payments",
            OnePercent,
            April,
            singleThresholdMinor: 30_000L * 10_000L,
            annualThresholdMinor: 100_000L * 10_000L);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.IndividualOrHuf);

        await TdsTestWorld.PostBillAsync(host, world, sectionId, April, 20_000m);
        var big = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 50_000m);

        // Only the big one is taxed, and only on itself: the annual aggregate is still ₹70,000.
        big.Deduction.TaxableBase.ShouldBe(50_000m);
        big.DeductedAmount.ShouldBe(500m);
        big.Deduction.IsThresholdCrossingPayment.ShouldBeFalse();
    }

    // -------------------------------------------------------------- C7.4 — a dated rate change

    [Fact]
    public async Task ADatedRateChange_AppliesOnEachSideOfItsEffectiveDate()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        // Both rows price the same class of payee, so the newcomer genuinely supersedes its
        // predecessor rather than sitting beside it as a more specific match.
        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host,
            "TEST-F",
            "Contractor payments",
            OnePercent,
            April,
            payeeCategory: PayeeCategory.Other);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.Other);

        // From 1 June the rate becomes 2%. The earlier row closes at 31 May, automatically.
        var changeDate = new DateOnly(2026, 6, 1);

        await host.InScopeAsync(sp => sp.GetRequiredService<ITaxSectionService>()
            .AddRateScheduleAsync(new SaveTaxRateScheduleRequest(
                sectionId, PayeeCategory.Other, TwoPercent, changeDate)));

        var before = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 100_000m);
        var onTheDay = await TdsTestWorld.PostBillAsync(host, world, sectionId, changeDate, 100_000m);
        var after = await TdsTestWorld.PostBillAsync(host, world, sectionId, June, 100_000m);

        before.DeductedAmount.ShouldBe(1_000m, "a May payment is priced by the May rate");
        onTheDay.DeductedAmount.ShouldBe(2_000m, "the new rate applies from its own first day");
        after.DeductedAmount.ShouldBe(2_000m);

        // History is closed, not rewritten: both rows survive, and the old one now has an end date.
        var schedules = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ITaxSectionService>().ListRateSchedulesAsync(sectionId));

        schedules.Count.ShouldBe(2);
        schedules.Single(row => row.RatePercentMinor == OnePercent).EffectiveTo
            .ShouldBe(new DateOnly(2026, 5, 31));
        schedules.Single(row => row.RatePercentMinor == TwoPercent).EffectiveTo.ShouldBeNull();
    }

    [Fact]
    public async Task WithNoPanOnFile_TheHigherRateApplies()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host,
            "TEST-G",
            "Contractor payments",
            OnePercent,
            April,
            rateWithoutPanPercentMinor: TwentyPercent);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.Other, pan: null);

        var posted = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 100_000m);

        posted.DeductedAmount.ShouldBe(20_000m);
        posted.Deduction.IsNoPanRate.ShouldBeTrue();
        posted.Deduction.PayeeHadPan.ShouldBeFalse();
        posted.Deduction.Explanation.ShouldContain("no PAN");
    }

    // ------------------------------------------------------ C7.6 — 194Q and 206C(1H) exclusive

    [Fact]
    public async Task WhereTheBuyerDeclaresTheyDeduct_CollectionDoesNotAlsoApply()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var collectionSectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host,
            "TEST-H",
            "Collection on sale of goods",
            OnePercent,
            April,
            kind: TaxSectionKind.Tcs);

        await host.InScopeAsync(sp => sp.GetRequiredService<ITdsSettingsService>()
            .SaveAsync(new SaveTdsSettingsRequest(
                "TDS Payable",
                "TCS Payable",
                IsSaleOfGoodsCollectionEnabled: true,
                SaleOfGoodsSectionId: collectionSectionId)));

        // A buyer who has not declared anything: we collect.
        await TdsTestWorld.ProfileAsync(host, world.CustomerLedgerId, PayeeCategory.Other);

        var collecting = await ResolveCollectionAsync(host, world.CustomerLedgerId);
        collecting.ShouldBe(collectionSectionId);

        // The same buyer files a declaration that they deduct on their purchase from us. The two
        // are mutually exclusive by statute, so collection must stop.
        await TdsTestWorld.ProfileAsync(
            host,
            world.CustomerLedgerId,
            PayeeCategory.Other,
            declaresDeductingOnPurchases: true);

        (await ResolveCollectionAsync(host, world.CustomerLedgerId)).ShouldBeNull();
    }

    [Fact]
    public async Task WithTheCollectionFlagOff_NothingCollectsAtAll()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        // A TCS section exists and is rated, but the owner has not declared the business liable.
        await TdsTestWorld.AddSectionWithRateAsync(
            host, "TEST-I", "Collection on sale of goods", OnePercent, April, kind: TaxSectionKind.Tcs);

        (await ResolveCollectionAsync(host, world.CustomerLedgerId)).ShouldBeNull();
    }

    [Fact]
    public async Task EnablingCollectionAgainstATdsSection_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await TdsTestWorld.ArrangeAsync(host);

        var deductionSectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host, "TEST-J", "Deduction on purchase of goods", OnePercent, April);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            host.InScopeAsync(sp => sp.GetRequiredService<ITdsSettingsService>()
                .SaveAsync(new SaveTdsSettingsRequest(
                    "TDS Payable",
                    "TCS Payable",
                    IsSaleOfGoodsCollectionEnabled: true,
                    SaleOfGoodsSectionId: deductionSectionId))));
    }

    // ------------------------------------------------------------- Section 197 certificates

    [Fact]
    public async Task UnderANilCertificate_NothingIsDeductedButTheBaseIsStillRecorded()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host, "TEST-K", "Contractor payments", TwoPercent, April);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.Other);

        await host.InScopeAsync(sp => sp.GetRequiredService<ITaxSectionService>()
            .AddCertificateAsync(new SaveLowerDeductionCertificateRequest(
                world.ContractorLedgerId,
                sectionId,
                "NIL/2026/0042",
                RatePercentMinor: 0L,
                ValidFrom: April,
                ValidTo: new DateOnly(2027, 3, 31))));

        var posted = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 100_000m);

        posted.DeductedAmount.ShouldBe(0m);
        posted.Deduction.Reason.ShouldBe(NoDeductionReason.NilCertificate);
        posted.Deduction.CertificateNumber.ShouldBe("NIL/2026/0042");

        // Recorded, because the return has to show it: a nil deduction under a certificate is a
        // different fact from no deduction at all.
        var row = (await RegisterAsync(host, April, July)).ShouldHaveSingleItem();
        row.TaxableBase.ShouldBe(100_000m);
        row.IsCertificateApplied.ShouldBeTrue();
        row.CertificateNumber.ShouldBe("NIL/2026/0042");
    }

    [Fact]
    public async Task PastACertificatesExpiry_TheScheduledRateReturnsWithoutAnyoneSwitchingItOff()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host, "TEST-L", "Contractor payments", TwoPercent, April);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.Other);

        await host.InScopeAsync(sp => sp.GetRequiredService<ITaxSectionService>()
            .AddCertificateAsync(new SaveLowerDeductionCertificateRequest(
                world.ContractorLedgerId,
                sectionId,
                "LOW/2026/0007",
                RatePercentMinor: 0L,
                ValidFrom: April,
                ValidTo: new DateOnly(2026, 5, 31))));

        var inside = await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 100_000m);
        var outside = await TdsTestWorld.PostBillAsync(host, world, sectionId, June, 100_000m);

        inside.DeductedAmount.ShouldBe(0m);
        outside.DeductedAmount.ShouldBe(2_000m, "the day after expiry deducts at the full rate");
    }

    // ------------------------------------------------------------------- the challan summary

    [Fact]
    public async Task ChallanSummary_GroupsByOneSectionAndCountsDeducteesNotDeductions()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var sectionId = await TdsTestWorld.AddSectionWithRateAsync(
            host, "TEST-M", "Contractor payments", TwoPercent, April);

        await TdsTestWorld.ProfileAsync(host, world.ContractorLedgerId, PayeeCategory.Other);

        await TdsTestWorld.PostBillAsync(host, world, sectionId, April, 10_000m);
        await TdsTestWorld.PostBillAsync(host, world, sectionId, May, 10_000m);

        var challan = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ITaxDeductionService>().ChallanSummaryAsync(April, July));

        var line = challan.ShouldHaveSingleItem();
        line.SectionCode.ShouldBe("TEST-M");
        line.DeducteeCount.ShouldBe(1, "one contractor billing twice is one deductee");
        line.TotalBase.ShouldBe(20_000m);
        line.TotalDeducted.ShouldBe(400m);
    }

    private static Task<IReadOnlyList<TaxDeductionRow>> RegisterAsync(
        SqliteTestHost host,
        DateOnly fromDate,
        DateOnly toDate) =>
        host.InScopeAsync(sp =>
            sp.GetRequiredService<ITaxDeductionService>().ListAsync(fromDate, toDate));

    private static Task<Guid?> ResolveCollectionAsync(SqliteTestHost host, Guid customerLedgerId) =>
        host.InScopeAsync(sp => sp.GetRequiredService<ITaxDeductionService>()
            .ResolveSaleOfGoodsCollectionSectionAsync(customerLedgerId));

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
