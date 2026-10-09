using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Accounting;

/// <summary>
/// The halves of C1.16 and C1.19 that `specs/phase-1-accounting-core.md` §14 spells out and nobody
/// had written: the hash chain naming the <i>first</i> row that no longer verifies, and the
/// one-current-revision invariant holding after an alteration that was rolled back.
/// </summary>
/// <remarks>
/// C1.16's point is the difference between tamper-<i>resistant</i> and tamper-<i>evident</i>
/// (open-questions.md #16). The triggers and the `AuditTrailTamperException` make the log hard to
/// edit through the product; neither helps once someone has the SQLite file and a shell, which is
/// the situation Phases 1–8 actually ship into. What survives that is the chain: `RowHash` covers
/// `PreviousHash`, so editing one row breaks every hash after it and verification can point at
/// where the edit began.
/// <para>
/// So this test does what an attacker with the file would do — drops the triggers and rewrites a
/// row — and then asserts the chain still gives the auditor an answer. The database is the test
/// host's own throwaway file, which is the "throwaway copy" §14 asks for.
/// </para>
/// </remarks>
public sealed class AuditChainTamperTests
{
    private static readonly DateOnly InFy2026 = new(2026, 4, 15);

    [Fact]
    public async Task VerifyChain_AfterARowIsRewrittenWithTheTriggersDropped_NamesThatRowAsTheFirstBreak()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        // Several posts, so the chain is long enough for "first break" to mean something.
        for (var index = 0; index < 5; index++)
        {
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(BalancedPayment(books, 100m + index)));
        }

        var intact = await VerifyAsync(host);
        intact.IsIntact.ShouldBeTrue(intact.Detail);
        intact.RowsChecked.ShouldBeGreaterThan(5);

        // Pick a row in the middle: tampering with the last row would not distinguish "detects a
        // break" from "detects a truncation".
        var rows = await host.InScopeAsync(AuditLogAccess.AllAsync);
        var target = rows[rows.Count / 2];

        await host.InScopeAsync(async sp =>
        {
            var factory = sp.GetRequiredService<IDbConnectionFactory>();
            await using var connection = await factory.OpenConnectionAsync();

            // The triggers are the product's defence; an attacker with the file simply removes
            // them. That is the whole premise of a tamper-*evident* chain.
            await ExecuteAsync(connection, "DROP TRIGGER TR_AuditLogEntries_BlockUpdate;");
            await ExecuteAsync(connection, "DROP TRIGGER TR_AuditLogEntries_BlockDelete;");

            await ExecuteAsync(
                connection,
                $"UPDATE AuditLogEntries SET UserName = 'forged' WHERE Sequence = {target.Sequence};");
        });

        // The row really was rewritten — otherwise the assertion below would pass for the wrong
        // reason, which is how a tamper test quietly stops testing anything.
        var after = await host.InScopeAsync(AuditLogAccess.AllAsync);
        after.Single(row => row.Sequence == target.Sequence).UserName.ShouldBe("forged");

        var broken = await VerifyAsync(host);

        broken.IsIntact.ShouldBeFalse("a rewritten row must not verify");
        broken.FirstBrokenSequence.ShouldBe(
            target.Sequence,
            "verification must name where the edit began, not merely that something is wrong");
        broken.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task VerifyChain_AfterARowIsDeletedWithTheTriggersDropped_ReportsTheChainBroken()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        for (var index = 0; index < 4; index++)
        {
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(BalancedPayment(books, 200m + index)));
        }

        var rows = await host.InScopeAsync(AuditLogAccess.AllAsync);
        var removed = rows[rows.Count / 2];

        await host.InScopeAsync(async sp =>
        {
            var factory = sp.GetRequiredService<IDbConnectionFactory>();
            await using var connection = await factory.OpenConnectionAsync();

            await ExecuteAsync(connection, "DROP TRIGGER TR_AuditLogEntries_BlockUpdate;");
            await ExecuteAsync(connection, "DROP TRIGGER TR_AuditLogEntries_BlockDelete;");
            await ExecuteAsync(
                connection,
                $"DELETE FROM AuditLogEntries WHERE Sequence = {removed.Sequence};");
        });

        // Deleting a row is the likelier attack than editing one — it is what "clean up the log
        // before the audit" looks like. The successor's PreviousHash no longer matches its
        // predecessor, so the gap is evident even though the deleted row itself is gone.
        var broken = await VerifyAsync(host);

        broken.IsIntact.ShouldBeFalse("a hole in the chain must not verify");
        broken.FirstBrokenSequence.ShouldNotBeNull();
        broken.FirstBrokenSequence!.Value.ShouldBeGreaterThan(removed.Sequence - 1);
    }

    [Fact]
    public async Task EveryVoucher_AfterAnAlterationThatWasRefused_StillHasExactlyOneCurrentRevision()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var books = await ArrangeBooksAsync(host);

        var posted = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .PostAsync(BalancedPayment(books, 400m)));

        // Three different refusals, each failing at a different point of AlterAsync: validation of
        // the lines, the missing change reason, and the period lock. A rollback that left an orphan
        // revision behind would most likely do it on one of these and not the others.
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .AlterAsync(new AlterVoucherRequest(
                    posted.VoucherId,
                    InFy2026,
                    [
                        new PostVoucherLine(books.ElectricityLedgerId, Debit: 500m, Credit: 0m),
                        new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: 499.9999m),
                    ],
                    ChangeReason: "Unbalanced on purpose"))));

        await Should.ThrowAsync<ArgumentException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .AlterAsync(new AlterVoucherRequest(
                    posted.VoucherId,
                    InFy2026,
                    BalancedPayment(books, 450m).Lines,
                    ChangeReason: "   "))));

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var periodLock = new PeriodLock(books.CompanyId);
            periodLock.LockThrough(new DateOnly(2026, 4, 30), "Statutory audit in progress");

            await unitOfWork.Repository<PeriodLock>().AddAsync(periodLock);
            await unitOfWork.SaveChangesAsync();
        });

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .AlterAsync(new AlterVoucherRequest(
                    posted.VoucherId,
                    InFy2026,
                    BalancedPayment(books, 475m).Lines,
                    ChangeReason: "Into a locked period"))));

        // C1.19's second half: the invariant the deliberately-absent foreign key used to guarantee
        // (C1.17, ADR-0002 Amendment 1) must hold after a rolled-back transaction too.
        var revisions = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<VoucherRevision>()
            .ListAsync());

        revisions.Count.ShouldBe(1, "no refused alteration may leave a revision behind");
        revisions.Count(revision => revision.IsCurrent).ShouldBe(1);
        revisions[0].VoucherId.ShouldBe(posted.VoucherId);
        revisions[0].RevisionNumber.ShouldBe(1);
        revisions[0].SupersededByRevisionId.ShouldBeNull(
            "the original was never superseded, because no successor was ever written");

        // And the voucher still reads back whole.
        var trialBalance = await host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetTrialBalanceAsync(new DateOnly(2026, 4, 30)));

        trialBalance.IsBalanced.ShouldBeTrue();
        trialBalance.TotalDebit.ShouldBe(400m);
    }

    // ------------------------------------------------------------------------------- helpers

    private static Task<AuditChainVerification> VerifyAsync(SqliteTestHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditLogReader>().VerifyChainAsync());

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static PostVoucherRequest BalancedPayment(Books books, decimal amount) =>
        new(
            books.PaymentTypeId,
            InFy2026,
            [
                new PostVoucherLine(books.ElectricityLedgerId, Debit: amount, Credit: 0m),
                new PostVoucherLine(books.CashLedgerId, Debit: 0m, Credit: amount),
            ]);

    private sealed record Books(
        Guid CompanyId,
        Guid PaymentTypeId,
        Guid CashLedgerId,
        Guid ElectricityLedgerId);

    private static async Task<Books> ArrangeBooksAsync(SqliteTestHost host)
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

        return await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var types = await unitOfWork.Repository<VoucherType>().ListAsync();
            var ledgers = await unitOfWork.Repository<Ledger>().ListAsync();
            var groups = await unitOfWork.Repository<AccountGroup>().ListAsync();

            var electricity = await sp.GetRequiredService<IChartOfAccountsService>()
                .CreateLedgerAsync(new CreateLedgerRequest(
                    "Electricity Expenses",
                    groups.Single(group => group.Name == "Indirect Expenses").Id));

            return new Books(
                companyId,
                types.Single(type => type.Name == "Payment").Id,
                ledgers.Single(ledger => ledger.Name == "Cash").Id,
                electricity.Id);
        });
    }
}
