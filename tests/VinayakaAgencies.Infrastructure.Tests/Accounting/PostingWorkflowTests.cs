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
    public async Task Post_ContraBetweenCashAndBank_IsAccepted()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);
        var bankLedgerId = await AddBankLedgerAsync(host);

        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(new PostVoucherRequest(
                books.ContraTypeId,
                InFy2026,
                [
                    new PostVoucherLine(bankLedgerId, Debit: 25_000m, Credit: 0m),
                    new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: 25_000m),
                ],
                Narration: "Counter cash deposited into the current account")));

        // The regression for the defect the C1.5 fixture found: the Contra rule matched its seeded
        // group names against the upper-cased NameNormalised column with an ordinal comparison, so
        // nothing was ever "cash or bank" and a plain cash-to-bank deposit — the single commonest
        // Contra there is — was refused. Post_ContraToNonCashLedger_IsRefused above passed
        // throughout, because a rule that refuses everything also refuses what it should.
        posted.Status.ShouldBe(VoucherStatus.Posted);
        posted.Number.ShouldBe("CTR/0001");
        posted.TotalDebit.ShouldBe(25_000m);

        var trialBalance = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetTrialBalanceAsync(new DateOnly(2026, 4, 30)));

        trialBalance.IsBalanced.ShouldBeTrue();
        trialBalance.Rows.Single(row => row.LedgerName == BankLedgerName).Debit.ShouldBe(25_000m);
    }

    [Fact]
    public async Task EditLog_ForAnAccountGroup_ShowsBeforeAndAfterImagesOfARenameAndAMove()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var chart = await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .CreateGroupAsync(new CreateAccountGroupRequest("Depot Overheads", books.ElectricityGroupId)));

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .RenameGroupAsync(chart.Id, "Depot & Yard Overheads"));

        // C1.3 asks for the account-**group** case explicitly, alongside the voucher and the
        // ledger. It runs through the same SaveChanges interceptor, but "the same code path" is an
        // argument rather than evidence, and MCA Rule 3(1) covers every master.
        var history = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAuditLogReader>()
            .ListForEntityAsync(nameof(AccountGroup), chart.Id.ToString("D")));

        history.ShouldNotBeEmpty();
        history.ShouldContain(row => row.Action == AuditAction.Create);

        var rename = history.Last(row => row.Action == AuditAction.Update);

        rename.BeforeJson!.ShouldContain("Depot Overheads");
        rename.AfterJson!.ShouldContain("Depot & Yard Overheads");
        rename.UserName.ShouldNotBeNullOrEmpty();
        rename.OccurredAtUtc.ShouldNotBe(default);
        rename.ChangedProperties.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Alter_PostedVoucher_IssuesTheSupersedingUpdateBeforeTheSuccessorInsert()
    {
        var recorder = new SqlCommandRecorder();

        await using var host = new SqliteTestHost(interceptor: recorder);
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 500m)));

        // Only the alteration's own SQL is of interest; the arrangement above is noise.
        recorder.Clear();

        await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .AlterAsync(new AlterVoucherRequest(
                posted.VoucherId,
                InFy2026,
                BalancedPayment(books, 650m).Lines,
                ChangeReason: "Counter recount")));

        // C1.18. The outcome-level assertions in Alter_PostedVoucher_WritesAppendOnlyHistory would
        // still pass if these two statements swapped places, but between an insert that claims the
        // number and an update that releases it there would be a window with two live revisions on
        // one number — and the filtered unique index would reject the insert outright. This is the
        // test that survives an EF Core upgrade reordering commands, or someone collapsing spec
        // §8.2's two saves into one.
        var supersedes = recorder.PositionsMatching(
            "UPDATE", "\"VoucherRevisions\"", "SupersededByRevisionId");
        var inserts = recorder.PositionsMatching("INSERT", "\"VoucherRevisions\"");

        // Named so a failure says which statements *were* emitted; without it the message is just
        // "should not be empty", which tells the next reader nothing about what changed.
        var seen = string.Join(" | ", recorder.Commands.Select(command => Summarise(command.CommandText)));

        supersedes.ShouldNotBeEmpty(
            $"the alteration must supersede the previous revision; saw: {seen}");
        inserts.ShouldNotBeEmpty(
            $"the alteration must insert a successor revision; saw: {seen}");

        supersedes[0].ShouldBeLessThan(
            inserts[0],
            "the superseding UPDATE must be issued before the successor INSERT (ADR-0002)");

        // …and both reach the database on one transaction, so a failure between them cannot leave
        // a voucher with no current revision.
        var commands = recorder.Commands;
        var supersedeTransaction = commands[supersedes[0]].TransactionId;
        var insertTransaction = commands[inserts[0]].TransactionId;

        supersedeTransaction.ShouldNotBeNull("the superseding UPDATE must run inside a transaction");
        insertTransaction.ShouldBe(
            supersedeTransaction,
            "supersede and insert must share one transaction");
    }

    [Fact]
    public async Task BalanceSheet_WithABalanceOnEachSide_ShowsLiabilitiesPositiveAndBalances()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var creditorGroupId = await GroupIdByNameAsync(host, "Sundry Creditors");

        var creditor = await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .CreateLedgerAsync(new CreateLedgerRequest("Crompton Greaves Ltd", creditorGroupId)));

        var purchases = await host.InScopeAsync(async sp =>
        {
            var ledgers = await sp.GetRequiredService<IUnitOfWork>().Repository<Ledger>().ListAsync();

            return ledgers.Single(ledger => ledger.Name == "Purchases").Id;
        });

        // Stock bought on credit: the only voucher needed to put a real figure on each side.
        await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(new PostVoucherRequest(
                books.PaymentTypeId,
                InFy2026,
                [
                    new PostVoucherLine(purchases, Debit: 40_000m, Credit: 0m),
                    new PostVoucherLine(creditor.Id, Debit: 0m, Credit: 40_000m),
                ],
                Narration: "Pumps bought on credit")));

        var balanceSheet = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetBalanceSheetAsync(new DateOnly(2026, 4, 30)));

        // The regression for the double-negated liability side. Both the query and the report
        // mapper used to flip a liability's sign, so the two cancelled: a creditor the business
        // owes ₹40,000 to showed as -40,000.00, and the sheet balanced only when both sides were
        // zero — which is the one case Post_BalancedPayment_UpdatesEveryDerivedStatement happens
        // to produce, so nothing failed.
        balanceSheet.Liabilities
            .Single(row => row.LedgerName == "Crompton Greaves Ltd")
            .Amount.ShouldBe(40_000m);

        // The loss sits on the assets side as a positive figure, the same side and sign as before.
        balanceSheet.Assets
            .Single(row => row.LedgerName == "Profit & Loss A/c")
            .Amount.ShouldBe(40_000m);

        // And it appears once. The derived ledger used to be listed by the query as well as
        // injected, giving two rows of the same name.
        balanceSheet.Assets
            .Concat(balanceSheet.Liabilities)
            .Count(row => row.LedgerName == "Profit & Loss A/c")
            .ShouldBe(1);

        balanceSheet.TotalAssets.ShouldBe(balanceSheet.TotalLiabilities);
        balanceSheet.IsBalanced.ShouldBeTrue();
    }

    [Fact]
    public async Task ListBankLedgers_ForALedgerUnderBankAccounts_ReturnsIt()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);
        await AddBankLedgerAsync(host);

        var banks = await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .ListBankLedgersAsync());

        // Same root cause as the Contra defect, and the reason it mattered more: this list feeds
        // the bank-reconciliation screen's picker, so reconciliation — a §2 deliverable — had no
        // account to reconcile against on any install.
        banks.Select(bank => bank.Name).ShouldContain(BankLedgerName);

        // Cash is deliberately not a bank: it is not reconciled against a statement.
        banks.ShouldNotContain(bank => bank.Id == books.CashLedgerId);
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

    /// <summary>One SQL statement on one line, clipped, for an assertion message.</summary>
    private static string Summarise(string commandText)
    {
        var flattened = commandText.ReplaceLineEndings(" ");

        return flattened.Length <= 120 ? flattened : flattened[..120];
    }

    private const string BankLedgerName = "Karnataka Bank — Current A/c";

    /// <summary>A ledger under the seeded "Bank Accounts" group, for the two bank tests.</summary>
    private static async Task<Guid> AddBankLedgerAsync(SqliteTestHost host)
    {
        var bankGroupId = await GroupIdByNameAsync(host, "Bank Accounts");

        var bank = await host.InScopeAsync(sp => sp
            .GetRequiredService<IChartOfAccountsService>()
            .CreateLedgerAsync(new CreateLedgerRequest(BankLedgerName, bankGroupId)));

        return bank.Id;
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
