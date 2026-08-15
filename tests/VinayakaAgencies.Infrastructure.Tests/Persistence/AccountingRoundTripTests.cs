using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// A full chart-of-accounts-to-journal-line graph written through <see cref="IUnitOfWork"/> and read
/// back in a new scope.
/// </summary>
/// <remarks>
/// A migration that has only ever been generated has not been tested. This one is exercised by
/// pushing real data through every table it creates, on a real SQLite file with foreign keys
/// enforced: enum-to-string conversion, <see cref="DateOnly"/> business dates, the signed minor-unit
/// amount, and the filtered unique index that enforces voucher numbering.
/// <para>
/// The posting service arrives in a later step of this phase. These writes therefore go through the
/// repository directly, which is the seam every service will use anyway.
/// </para>
/// </remarks>
public sealed class AccountingRoundTripTests
{
    private static readonly DateOnly YearStart = new(2026, 4, 1);

    [Fact]
    public async Task SaveChangesAsync_ForAPostedVoucher_RoundTripsEveryAccountingTable()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var books = await ArrangeBooksAsync(host);

        var lines = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<JournalLine>()
            .ListAsync());

        var posted = lines.Where(line => line.VoucherRevisionId == books.RevisionId)
            .OrderBy(line => line.LineNumber)
            .ToList();

        posted.Count.ShouldBe(2);

        // ₹1,250.5075 — four decimal places, which is precisely the scale a double would lose.
        posted[0].LedgerId.ShouldBe(books.CashLedgerId);
        posted[0].AmountMinor.ShouldBe(12_505_075L);
        posted[0].Amount.ShouldBe(1250.5075m);
        posted[0].IsDebit.ShouldBeTrue();

        posted[1].LedgerId.ShouldBe(books.SalesLedgerId);
        posted[1].AmountMinor.ShouldBe(-12_505_075L);
        posted[1].IsCredit.ShouldBeTrue();

        // Exact integer arithmetic, no epsilon (ADR-0003).
        posted.Sum(line => line.AmountMinor).ShouldBe(0L);
    }

    [Fact]
    public async Task SaveChangesAsync_ForAPostedVoucher_StoresBusinessDatesAndEnumsReadably()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var books = await ArrangeBooksAsync(host);

        var revision = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<VoucherRevision>()
            .GetByIdAsync(books.RevisionId));

        revision.ShouldNotBeNull();
        revision.VoucherDate.ShouldBe(new DateOnly(2026, 4, 15));
        revision.Status.ShouldBe(VoucherStatus.Posted);
        revision.SupersededByRevisionId.ShouldBeNull();

        // The denormalised key columns the filtered unique index is built over (spec §5).
        revision.CompanyId.ShouldBe(books.CompanyId);
        revision.VoucherTypeId.ShouldBe(books.VoucherTypeId);
        revision.SeriesId.ShouldBe(books.SeriesId);
        revision.FinancialYearId.ShouldBe(books.FinancialYearId);

        // Read straight out of the file: EF would convert on the way back and hide the storage form.
        // There is exactly one revision in this fixture.
        var stored = await ReadScalarAsync(
            host.DatabasePath,
            "SELECT Status || '|' || VoucherDate FROM VoucherRevisions;");

        // Not "1" and not an instant: an enum reordering must not rewrite history (§5), and a
        // business date must survive as the local calendar date the operator typed (§12).
        stored.ShouldBe("Posted|2026-04-15");
    }

    [Fact]
    public async Task SaveChangesAsync_ForADuplicateCurrentVoucherNumber_IsRejectedByTheFilteredIndex()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var books = await ArrangeBooksAsync(host);

        // §7.2: the filtered unique index is the last line of defence behind the posting validation.
        // Two live vouchers sharing an invoice number in one series and financial year is a GST
        // problem, so the database refuses it even if a validation is bypassed.
        var duplicate = await Should.ThrowAsync<DbUpdateException>(async () =>
            await host.InScopeAsync(async sp =>
            {
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

                var voucher = new Voucher(
                    books.CompanyId, books.VoucherTypeId, books.SeriesId, books.FinancialYearId);

                var revision = new VoucherRevision(
                    voucher,
                    revisionNumber: 1,
                    voucherDate: new DateOnly(2026, 4, 16),
                    number: "SAL/0001");

                await unitOfWork.Repository<Voucher>().AddAsync(voucher);
                await unitOfWork.Repository<VoucherRevision>().AddAsync(revision);
                await unitOfWork.SaveChangesAsync();
            }));

        duplicate.ShouldNotBeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_ForASupersededRevision_FreesTheNumberForItsSuccessor()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var books = await ArrangeBooksAsync(host);

        // ADR-0002: an alteration is a new revision carrying the same number, with the old one
        // stamped as superseded. The index filter is what makes that expressible — a plain unique
        // index would reject every alteration the product is required to support.
        var successorId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var repository = unitOfWork.Repository<VoucherRevision>();

            var original = await repository.GetByIdAsync(books.RevisionId);
            original.ShouldNotBeNull();

            var voucher = await unitOfWork.Repository<Voucher>().GetByIdAsync(books.VoucherId);
            voucher.ShouldNotBeNull();

            var successor = new VoucherRevision(
                voucher,
                revisionNumber: 2,
                voucherDate: new DateOnly(2026, 4, 15),
                number: "SAL/0001",
                changeReason: "Amount corrected against the counterfoil.");

            original.SupersedeBy(successor.Id);

            await repository.AddAsync(successor);
            unitOfWork.Repository<VoucherRevision>().Update(original);
            await unitOfWork.SaveChangesAsync();

            return successor.Id;
        });

        var revisions = await host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<VoucherRevision>()
            .ListAsync());

        var forVoucher = revisions.Where(revision => revision.VoucherId == books.VoucherId).ToList();

        forVoucher.Count.ShouldBe(2);
        forVoucher.Count(revision => revision.IsCurrent).ShouldBe(1);
        forVoucher.Single(revision => revision.IsCurrent).Id.ShouldBe(successorId);
    }

    [Fact]
    public async Task Remove_ForALedgerThatHasAJournalLine_IsRefusedByTheForeignKey()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var books = await ArrangeBooksAsync(host);

        // §4.6 allows a hard delete only when the row has no journal line. DeleteBehavior.Restrict
        // makes that a database guarantee rather than a validation someone has to remember, and
        // ADR-0002 forbids any delete path reaching a posted line at all.
        var refused = await Should.ThrowAsync<DbUpdateException>(async () =>
            await host.InScopeAsync(async sp =>
            {
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var repository = unitOfWork.Repository<Ledger>();

                var ledger = await repository.GetByIdAsync(books.CashLedgerId);
                ledger.ShouldNotBeNull();

                repository.Remove(ledger);
                await unitOfWork.SaveChangesAsync();
            }));

        refused.ShouldNotBeNull();
    }

    private static async Task<Books> ArrangeBooksAsync(SqliteTestHost host)
    {
        return await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies", "29");
            await unitOfWork.Repository<Company>().AddAsync(company);

            var assets = new AccountGroup(
                company.Id, "Current Assets", AccountNature.Asset, isPrimary: true, isSystem: true);
            assets.SetMaterialisedPath(null);

            var cashInHand = new AccountGroup(
                company.Id, "Cash-in-Hand", AccountNature.Asset, parentGroupId: assets.Id,
                isSystem: true);
            cashInHand.SetMaterialisedPath(assets.MaterialisedPath);

            var salesAccounts = new AccountGroup(
                company.Id, "Sales Accounts", AccountNature.Income, affectsGrossProfit: true,
                isPrimary: true, isSystem: true);
            salesAccounts.SetMaterialisedPath(null);

            await unitOfWork.Repository<AccountGroup>()
                .AddRangeAsync([assets, cashInHand, salesAccounts]);

            var cash = new Ledger(company.Id, "Cash", cashInHand.Id);
            var sales = new Ledger(company.Id, "Sales", salesAccounts.Id);
            await unitOfWork.Repository<Ledger>().AddRangeAsync([cash, sales]);

            var year = new FinancialYear(company.Id, YearStart, new DateOnly(2027, 3, 31));
            await unitOfWork.Repository<FinancialYear>().AddAsync(year);

            var periodLock = new PeriodLock(company.Id);
            await unitOfWork.Repository<PeriodLock>().AddAsync(periodLock);

            var category = new CostCategory(company.Id, "Branch");
            await unitOfWork.Repository<CostCategory>().AddAsync(category);

            var centre = new CostCentre(company.Id, "Head Office", category.Id);
            centre.SetMaterialisedPath(null);
            await unitOfWork.Repository<CostCentre>().AddAsync(centre);

            var voucherType = new VoucherType(company.Id, "Sales", VoucherKind.Sales, isSystem: true);
            await unitOfWork.Repository<VoucherType>().AddAsync(voucherType);

            var series = new VoucherNumberSeries(
                company.Id, voucherType.Id, "Sales", prefix: "SAL/");
            await unitOfWork.Repository<VoucherNumberSeries>().AddAsync(series);

            await unitOfWork.SaveChangesAsync();

            // DefaultSeriesId carries no foreign key (spec §5), so it is set in a second save
            // rather than forcing EF to break a VoucherType ↔ VoucherNumberSeries cycle.
            voucherType.SetDefaultSeries(series.Id);
            unitOfWork.Repository<VoucherType>().Update(voucherType);

            var sequence = new VoucherNumberSequence(series.Id, year.Id);
            var number = series.Format(sequence.Allocate(series.StartNumber));
            await unitOfWork.Repository<VoucherNumberSequence>().AddAsync(sequence);

            var voucher = new Voucher(company.Id, voucherType.Id, series.Id, year.Id);
            var revision = new VoucherRevision(
                voucher,
                revisionNumber: 1,
                voucherDate: new DateOnly(2026, 4, 15),
                number: number,
                narration: "Counter sale, 1.5 HP monoblock pump.");

            await unitOfWork.Repository<Voucher>().AddAsync(voucher);
            await unitOfWork.Repository<VoucherRevision>().AddAsync(revision);

            await unitOfWork.Repository<JournalLine>().AddRangeAsync(
            [
                new JournalLine(company.Id, revision.Id, 1, cash.Id, 1250.5075m, centre.Id),
                new JournalLine(company.Id, revision.Id, 2, sales.Id, -1250.5075m),
            ]);

            await unitOfWork.SaveChangesAsync();

            number.ShouldBe("SAL/0001");

            return new Books(
                company.Id,
                cash.Id,
                sales.Id,
                year.Id,
                voucherType.Id,
                series.Id,
                voucher.Id,
                revision.Id);
        });
    }

    private static async Task<string?> ReadScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync() as string;
    }

    private sealed record Books(
        Guid CompanyId,
        Guid CashLedgerId,
        Guid SalesLedgerId,
        Guid FinancialYearId,
        Guid VoucherTypeId,
        Guid SeriesId,
        Guid VoucherId,
        Guid RevisionId);
}
