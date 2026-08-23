using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Auditing;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Accounting;

/// <summary>
/// The Phase 1 vertical slice exercised end to end against a real SQLite file: chart of accounts,
/// posting, append-only alteration, cancellation, numbering, the derived statements, and the edit
/// log that watches all of it (spec §8, §11).
/// </summary>
/// <remarks>
/// Everything goes through the public application services over the real DI composition, because
/// the checkpoint criteria are about what the product does, not what its parts promise in isolation.
/// Each operation runs in its own DI scope — and therefore its own unit of work — exactly as the
/// shell would drive it.
/// </remarks>
public sealed class PostingWorkflowTests
{
    private static readonly DateOnly InFy2026 = new(2026, 4, 15);

    // ---------------------------------------------------------------- posts and statements

    [Fact]
    public async Task Post_BalancedPayment_UpdatesEveryDerivedStatement()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(new PostVoucherRequest(
                books.PaymentTypeId,
                InFy2026,
                [
                    new PostVoucherLine(books.ElectricityLedgerId, Debit: 1250.50m, Credit: 0m),
                    new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: 1250.50m),
                ],
                Narration: "Counter sale deposited")));

        posted.Number.ShouldBe("PMT/0001");
        posted.Status.ShouldBe(VoucherStatus.Posted);
        posted.TotalDebit.ShouldBe(1250.50m);
        posted.TotalCredit.ShouldBe(1250.50m);

        var trialBalance = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetTrialBalanceAsync(new DateOnly(2026, 4, 30)));

        trialBalance.IsBalanced.ShouldBeTrue("debits and credits were posted equal");
        trialBalance.TotalDebit.ShouldBe(1250.50m);

        var cashRow = trialBalance.Rows.Single(row => row.LedgerName == "Cash");

        cashRow.Credit.ShouldBe(1250.50m);
        cashRow.Debit.ShouldBe(0m);

        trialBalance.Rows.Single(row => row.LedgerName == "Electricity Expenses")
            .Debit.ShouldBe(1250.50m);

        var dayBook = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetDayBookAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)));

        dayBook.Count.ShouldBe(2);
        dayBook.ShouldNotContain(row => row.IsCancelled);
        dayBook.Count(row => row.LedgerName == "Cash" && row.Credit == 1250.50m).ShouldBe(1);
        dayBook.Count(row => row.LedgerName == "Electricity Expenses" && row.Debit == 1250.50m).ShouldBe(1);

        var profitAndLoss = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetProfitAndLossAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 31).AddDays(365)));

        profitAndLoss.TotalExpense.ShouldBe(1250.50m);
        profitAndLoss.NetProfit.ShouldBe(-1250.50m);

        var balanceSheet = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetBalanceSheetAsync(new DateOnly(2026, 4, 30)));
        // The loss must appear as the derived Profit & Loss A/c figure, keeping the sheet exact.
        balanceSheet.IsBalanced.ShouldBeTrue();
        balanceSheet.Assets.Single(row => row.LedgerName == "Profit & Loss A/c")
            .Amount.ShouldBe(1250.50m);
        balanceSheet.Assets.Single(row => row.LedgerName == "Cash").Amount.ShouldBe(-1250.50m);
    }

    [Fact]
    public async Task Post_UnbalancedByOneMinorUnit_IsRejectedWithoutConsumingANumber()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var rejection = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(new PostVoucherRequest(
                    books.PaymentTypeId,
                    InFy2026,
                    [
                        new PostVoucherLine(books.ElectricityLedgerId, Debit: 100.00m, Credit: 0m),
                        new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: 99.9999m),
                    ]))));

        rejection.Message.ShouldContain("balance");

        // Nothing was written, and nothing was consumed: the next voucher still opens the series.
        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 40m)));

        posted.Number.ShouldBe("PMT/0001");

        var vouchers = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<Voucher>()
            .ListAsync());

        vouchers.Count.ShouldBe(1);
    }

    // ---------------------------------------------------------------- alteration

    [Fact]
    public async Task Alter_PostedVoucher_WritesAppendOnlyHistory()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var original = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 500m)));

        var altered = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .AlterAsync(new AlterVoucherRequest(
                original.VoucherId,
                InFy2026,
                [
                    new PostVoucherLine(books.ElectricityLedgerId, Debit: 750m, Credit: 0m),
                    new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: 750m),
                ],
                ChangeReason: "Supplier bill corrected")));

        altered.RevisionNumber.ShouldBe(2);
        altered.Number.ShouldBe(original.Number);
        altered.TotalDebit.ShouldBe(750m);

        var revisions = await ReadRevisionsWithLinesAsync(host, original.VoucherId);

        revisions.Count.ShouldBe(2);

        var superseded = revisions.Single(revision =>
            revision.SupersededByRevisionId is not null);

        superseded.RevisionNumber.ShouldBe(1);
        superseded.SupersededByRevisionId.ShouldBe(altered.RevisionId);
        // Append-only: the superseded revision's original lines survive byte-for-byte.
        superseded.Lines.Select(line => line.AmountMinor)
            .OrderBy(minor => minor)
            .ShouldBe(new long[] { -5_000_000L, 5_000_000L });

        var current = revisions.Single(revision => revision.SupersededByRevisionId is null);

        current.Lines.Select(line => line.AmountMinor)
            .OrderBy(minor => minor)
            .ShouldBe(new long[] { -7_500_000L, 7_500_000L });

        var trialBalance = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetTrialBalanceAsync(InFy2026.AddDays(15)));

        trialBalance.Rows.Single(row => row.LedgerName == "Electricity Expenses")
            .Debit.ShouldBe(750m, "superseded figures must stop counting the moment they are superseded");

        var history = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAuditLogReader>()
            .ListForEntityAsync(nameof(VoucherRevision), superseded.Id.ToString("D")));

        var supersessionRows = history.Where(row => row.Action == AuditAction.Update).ToList();

        supersessionRows.ShouldNotBeEmpty("the supersession itself is a master change");
        supersessionRows.ShouldContain(row => row.ChangedProperties.Contains("SupersededByRevisionId"));
        supersessionRows.ShouldContain(row =>
            row.BeforeJson!.Contains("SupersededByRevisionId")
            && row.AfterJson!.Contains(altered.RevisionId.ToString("D")));
    }

    [Fact]
    public async Task Alter_WithoutChangeReason_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 100m)));

        await Should.ThrowAsync<ArgumentException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .AlterAsync(new AlterVoucherRequest(
                    posted.VoucherId,
                    InFy2026,
                    BalancedPayment(books, 120m).Lines,
                    ChangeReason: " "))));
    }

    // ---------------------------------------------------------------- cancellation and numbering

    [Fact]
    public async Task Cancel_PostedVoucher_KeepsItsNumberOccupiedAndFlagsTheDayBook()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var first = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 100m)));
        var second = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 200m)));

        var cancelled = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .CancelAsync(first.VoucherId, "Entered twice"));

        cancelled.Status.ShouldBe(VoucherStatus.Cancelled);
        cancelled.Number.ShouldBe(first.Number);

        (await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .GetCurrentAsync(first.VoucherId)))
            .ShouldNotBeNull().Status.ShouldBe(VoucherStatus.Cancelled);

        var dayBook = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetDayBookAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)));

        dayBook.Where(row => row.VoucherId == first.VoucherId)
            .ShouldNotBeEmpty();
        dayBook.Where(row => row.VoucherId == first.VoucherId)
            .ToList()
            .ShouldAllBe(row => row.IsCancelled);

        dayBook.Where(row => row.VoucherId == second.VoucherId)
            .ToList()
            .ShouldAllBe(row => !row.IsCancelled && row.Debit + row.Credit > 0m);

        // The cancelled voucher's figures leave every statement automatically — no special case.
        var trialBalance = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetTrialBalanceAsync(new DateOnly(2026, 4, 30)));

        trialBalance.TotalDebit.ShouldBe(200m);

        // D2: gaps are blocked. The number stays taken, so the gap report has nothing to report.
        var seriesId = await SeriesIdForPaymentAsync(host);
        var financialYearId = await FinancialYearIdAsync(host);

        (await host.InScopeAsync(sp => sp
                .GetRequiredService<IAccountingReportService>()
                .GetVoucherNumberGapsAsync(seriesId, financialYearId)))
            .ShouldBeEmpty();

        var third = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 300m)));

        third.Number.ShouldBe("PMT/0003", "a cancelled voucher's number is never reissued");
    }

    [Fact]
    public async Task Cancel_AlreadyCancelledVoucher_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 100m)));

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .CancelAsync(posted.VoucherId, "Entered twice"));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .CancelAsync(posted.VoucherId, "Again")));
    }

    [Fact]
    public async Task Post_AcrossAFinancialYearBoundary_RestartsTheCounter()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var lastYear = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 100m, new DateOnly(2026, 1, 10))));

        var thisYear = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 200m, new DateOnly(2026, 4, 10))));

        lastYear.Number.ShouldBe("PMT/0001");
        thisYear.Number.ShouldBe("PMT/0001", "decision D2 resets each series at the financial year");

        var years = await host.InScopeAsync(async sp =>
        {
            var years = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<FinancialYear>()
                .ListAsync();

            return years.Where(year => year.CompanyId == books.CompanyId).ToList();
        });

        years.Count.ShouldBe(2);
        years.ShouldContain(year => year.StartDate == new DateOnly(2025, 4, 1));
        years.ShouldContain(year => year.StartDate == new DateOnly(2026, 4, 1));
    }

    [Fact]
    public async Task Post_ManualNumberOverride_AdvancesHighWaterAndIsRecorded()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        (await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(BalancedPayment(books, 100m))))
            .Number.ShouldBe("PMT/0001");

        var overridden = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(new PostVoucherRequest(
                books.PaymentTypeId,
                InFy2026,
                BalancedPayment(books, 150m).Lines,
                ManualNumber: "PMT/0010")));

        overridden.Number.ShouldBe("PMT/0010");

        var revision = (await ReadRevisionsWithLinesAsync(host, overridden.VoucherId)).Single();

        revision.IsManualNumber.ShouldBeTrue("the override must be visible afterwards, not inferred from a gap");
        revision.NumberNormalised.ShouldBe(VoucherRevision.Normalise("PMT/0010"));

        (await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(BalancedPayment(books, 175m))))
            .Number.ShouldBe("PMT/0011", "automatic numbers continue past the manual high-water mark");

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(new PostVoucherRequest(
                    books.PaymentTypeId,
                    InFy2026,
                    BalancedPayment(books, 125m).Lines,
                    ManualNumber: "PMT/0010"))));
    }

    // ---------------------------------------------------------------- guards

    [Fact]
    public async Task Post_IntoLockedPeriod_IsRefusedButLaterDatesAreNot()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var periodLock = new PeriodLock(books.CompanyId);
            periodLock.LockThrough(new DateOnly(2026, 4, 20), "Statutory audit in progress");

            await unitOfWork.Repository<PeriodLock>().AddAsync(periodLock);
            await unitOfWork.SaveChangesAsync();
        });

        var locked = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(BalancedPayment(books, 100m, new DateOnly(2026, 4, 12)))));

        locked.Message.ShouldContain("locked");

        (await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(BalancedPayment(books, 100m, new DateOnly(2026, 4, 21)))))
            .Number.ShouldBe("PMT/0001");
    }

    [Fact]
    public async Task Alter_CannotMoveAVoucherIntoALockedPeriod()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 100m, new DateOnly(2026, 5, 5))));

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var periodLock = new PeriodLock(books.CompanyId);
            periodLock.LockThrough(new DateOnly(2026, 4, 30), "Filed");

            await unitOfWork.Repository<PeriodLock>().AddAsync(periodLock);
            await unitOfWork.SaveChangesAsync();
        });

        var refusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .AlterAsync(new AlterVoucherRequest(
                    posted.VoucherId,
                    new DateOnly(2026, 4, 10),
                    BalancedPayment(books, 120m).Lines,
                    ChangeReason: "Date was wrong"))));

        refusal.Message.ShouldContain("locked");
    }

    [Fact]
    public async Task Post_ContraToNonCashLedger_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var refusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(new PostVoucherRequest(
                    books.ContraTypeId,
                    InFy2026,
                    [
                        new PostVoucherLine(books.ElectricityLedgerId, Debit: 300m, Credit: 0m),
                        new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: 300m),
                    ]))));

        refusal.Message.ShouldContain("cash and bank");
    }

    [Fact]
    public async Task Post_ToInactiveOrDerivedLedger_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .DeactivateLedgerAsync(books.ElectricityLedgerId));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(BalancedPayment(books, 100m))));

        // The Profit & Loss A/c ledger is seeded AllowsManualPosting = false: its balance must stay
        // derived (spec §8.4), so posting to it directly is refused even though it exists.
        var profitAndLoss = await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .ListLedgersAsync());

        var derived = profitAndLoss.Single(ledger => ledger.Name == "Profit & Loss A/c");

        derived.AllowsManualPosting.ShouldBeFalse();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(new PostVoucherRequest(
                    books.PaymentTypeId,
                    InFy2026,
                    [
                        new PostVoucherLine(derived.Id, Debit: 100m, Credit: 0m),
                        new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: 100m),
                    ]))));
    }

    // ---------------------------------------------------------------- the edit log itself

    [Fact]
    public async Task EditLogChain_AfterAFullWorkflow_VerifiesIntact()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 100m)));
        await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .AlterAsync(new AlterVoucherRequest(
                posted.VoucherId, InFy2026, BalancedPayment(books, 110m).Lines, "Fix")));
        await host.InScopeAsync(async sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .CreateLedgerAsync(new CreateLedgerRequest(
                "HDFC Current A/c",
                await GroupIdByNameAsync(host, "Bank Accounts"))));

        var verification = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAuditLogReader>()
            .VerifyChainAsync());

        verification.IsIntact.ShouldBeTrue(verification.Detail ?? "chain broken");
        verification.FirstBrokenSequence.ShouldBeNull();
        verification.RowsChecked.ShouldBeGreaterThan(50, "provisioning alone writes ~53 rows");
    }

    [Fact]
    public async Task EditLog_ListForEntity_ShowsBeforeAndAfterImagesOfALedgerChange()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .UpdateLedgerAsync(new UpdateLedgerRequest(
                books.ElectricityLedgerId, "Electricity & Water", books.ElectricityGroupId,
                Alias: "ELEC")));

        var history = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAuditLogReader>()
            .ListForEntityAsync(nameof(Ledger), books.ElectricityLedgerId.ToString("D")));

        history.ShouldNotBeEmpty();

        var rename = history.Last(row => row.Action == AuditAction.Update);

        rename.BeforeJson!.ShouldContain("Electricity Expenses");
        rename.AfterJson!.ShouldContain("Electricity & Water");
        rename.UserName.ShouldNotBeNullOrEmpty();
        rename.ChangedProperties.ShouldNotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------- fixtures

    private static PostVoucherRequest BalancedPayment(
        Books books,
        decimal amount,
        DateOnly? date = null) => new(
        books.PaymentTypeId,
        date ?? InFy2026,
        [
            new PostVoucherLine(books.ElectricityLedgerId, Debit: amount, Credit: 0m),
            new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: amount),
        ]);

    /// <summary>A provisioned company plus the ids every workflow below needs.</summary>
    private sealed record Books(
        Guid CompanyId,
        Guid PaymentTypeId,
        Guid ContraTypeId,
        Guid CashLedgerId,
        Guid ElectricityLedgerId,
        Guid ElectricityGroupId);

    private static async Task<Books> ArrangeBooksAsync(SqliteTestHost host)
    {
        var companyId = await AddProvisionedCompanyAsync(host);

        var (paymentTypeId, contraTypeId, cashLedgerId) = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var types = await unitOfWork.Repository<VoucherType>().ListAsync();
            var ledgers = await unitOfWork.Repository<Ledger>().ListAsync();

            return (
                types.Single(t => t.CompanyId == companyId && t.Name == "Payment").Id,
                types.Single(t => t.CompanyId == companyId && t.Name == "Contra").Id,
                ledgers.Single(l => l.CompanyId == companyId && l.Name == "Cash").Id);
        });

        var indirectExpensesGroupId = await GroupIdByNameAsync(host, "Indirect Expenses");

        var electricity = await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .CreateLedgerAsync(new CreateLedgerRequest(
                "Electricity Expenses", indirectExpensesGroupId)));

        return new Books(
            companyId,
            paymentTypeId,
            contraTypeId,
            cashLedgerId,
            electricity.Id,
            indirectExpensesGroupId);
    }

    private static async Task<Guid> GroupIdByNameAsync(SqliteTestHost host, string groupName) =>
        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var groups = await unitOfWork.Repository<AccountGroup>().ListAsync();

            return groups.Single(group => group.Name == groupName).Id;
        });

    private static async Task<Guid> AddProvisionedCompanyAsync(SqliteTestHost host)
    {
        var companyId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");
            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        await host.InScopeAsync(sp => sp
            .GetRequiredService<ICompanyProvisioningService>()
            .ProvisionAsync(companyId));

        return companyId;
    }

    private static async Task<Guid> SeriesIdForPaymentAsync(SqliteTestHost host) =>
        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var types = await unitOfWork.Repository<VoucherType>().ListAsync();
            var paymentType = types.Single(type => type.Name == "Payment");

            var series = await unitOfWork.Repository<VoucherNumberSeries>().ListAsync();

            return series.Single(candidate => candidate.VoucherTypeId == paymentType.Id).Id;
        });

    private static async Task<Guid> FinancialYearIdAsync(SqliteTestHost host) =>
        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var companyId = (await unitOfWork.Repository<Company>().ListAsync()).Single().Id;
            var years = await unitOfWork.Repository<FinancialYear>().ListAsync();

            return years.Single(year => year.CompanyId == companyId && year.Contains(InFy2026)).Id;
        });

    private static Task<IReadOnlyList<VoucherRevision>> ReadRevisionsWithLinesAsync(
        SqliteTestHost host,
        Guid voucherId) =>
        host.InScopeAsync(async sp =>
        {
            var revisions = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<VoucherRevision>()
                .ListAsync(new RevisionsWithLines(voucherId));

            return (IReadOnlyList<VoucherRevision>)revisions
                .OrderBy(revision => revision.RevisionNumber)
                .ToList();
        });

    private sealed class RevisionsWithLines : Specification<VoucherRevision>
    {
        public RevisionsWithLines(Guid voucherId)
        {
            Where(revision => revision.VoucherId == voucherId);
            AddInclude(revision => revision.Lines);
        }
    }
}
