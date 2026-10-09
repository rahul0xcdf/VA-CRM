using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Shouldly;

namespace VinayakaAgencies.Infrastructure.Tests.Accounting;

/// <summary>
/// Checkpoint C9.3 — concurrent allocation produces no duplicate and no skipped voucher number.
/// </summary>
/// <remarks>
/// `open-questions.md` #11 is Confirmed on both halves of that: numbers are never reused, and gaps
/// are blocked. Together they rule out the easy implementations. A <c>SEQUENCE</c> would be
/// duplicate-free and lose numbers on rollback; a <c>SELECT MAX</c> would be gap-free and hand two
/// counters the same number. What is left is a counter row, read under a lock held by the
/// transaction that is writing the voucher.
/// <para>
/// <b>What is proved here and what is not.</b> These prove the allocation rules and the one failure
/// that a single-user build could never show — a caller whose tracked copy of the counter went stale
/// while another transaction moved it. They do not prove C9.3 as written, which is two clients
/// against SQL Server Express; this machine has no instance (#24). The parallel test below is a
/// genuine race on one SQLite file, which is as close as this environment gets.
/// </para>
/// </remarks>
public sealed class VoucherNumberAllocationTests
{
    [Fact]
    public async Task AllocateAsync_CalledRepeatedly_ProducesConsecutiveNumbersWithNoGap()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        var paymentTypeId = await PaymentVoucherTypeIdAsync(host);

        var numbers = new List<long>();

        for (var i = 0; i < 5; i++)
        {
            var allocated = await AllocateAndSaveAsync(host, companyId, paymentTypeId, new DateOnly(2026, 5, 10));

            numbers.Add(allocated.SequenceNumber);
        }

        numbers.ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task AllocateAsync_AfterAnotherScopeAllocated_DoesNotReissueThatNumber()
    {
        // The failure a single-user build can never show. The first scope reads the counter row and
        // holds it tracked; the second allocates and commits; the first then allocates from what it
        // has in memory. Before the lock-and-reconcile step this returned 1 twice.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        var paymentTypeId = await PaymentVoucherTypeIdAsync(host);

        await using var staleScope = host.CreateScope();

        // Put the counter row into this scope's change tracker, then leave it there.
        var staleUnitOfWork = staleScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await staleUnitOfWork.Repository<VoucherNumberSequence>().ListAsync();

        var fromOtherCounter = await AllocateAndSaveAsync(host, companyId, paymentTypeId, new DateOnly(2026, 5, 10));

        var fromStaleScope = await staleScope.ServiceProvider
            .GetRequiredService<IVoucherNumberAllocator>()
            .AllocateAsync(companyId, paymentTypeId, new DateOnly(2026, 5, 10));
        await staleUnitOfWork.SaveChangesAsync();

        fromOtherCounter.SequenceNumber.ShouldBe(1);
        fromStaleScope.SequenceNumber.ShouldBe(2);
    }

    [Fact]
    public async Task AllocateAsync_FromSeveralScopesAtOnce_NeverIssuesTheSameNumberTwice()
    {
        // A real race on one database file. SQLite serialises writers, so some of these may be
        // refused outright rather than queued — which is why the assertion is about the numbers
        // that were issued rather than about all eight succeeding. "No duplicates" is the
        // guarantee; "nobody is ever turned away" is not one this phase makes, and on SQL Server
        // the row lock turns a refusal into a wait.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        var paymentTypeId = await PaymentVoucherTypeIdAsync(host);

        // Allocated once up front so the sequence row exists: racing to *create* it is separated by
        // the unique index rather than by the lock, which is a different mechanism and is asserted
        // by its own test below.
        await AllocateAndSaveAsync(host, companyId, paymentTypeId, new DateOnly(2026, 5, 10));

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                var allocated = await AllocateAndSaveAsync(host, companyId, paymentTypeId, new DateOnly(2026, 5, 10));

                return (long?)allocated.SequenceNumber;
            }
            catch (Exception)
            {
                // Refused under contention. Allocation happens after validation, so a refused
                // attempt consumes no number and leaves no gap.
                return null;
            }
        });

        var issued = (await Task.WhenAll(attempts))
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToList();

        issued.ShouldNotBeEmpty();
        issued.Distinct().Count().ShouldBe(issued.Count);
        issued.ShouldAllBe(number => number > 1);
    }

    [Fact]
    public async Task AllocateAsync_TheSameSeriesInTwoFinancialYears_CountsEachYearSeparately()
    {
        // The reset-each-year rule of decision D2, realised as one counter row per (series, year)
        // rather than a job somebody has to remember to run in April.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        var paymentTypeId = await PaymentVoucherTypeIdAsync(host);

        var firstYear = await AllocateAndSaveAsync(host, companyId, paymentTypeId, new DateOnly(2026, 5, 10));

        var secondYear = await AllocateAndSaveAsync(host, companyId, paymentTypeId, new DateOnly(2027, 5, 10));

        firstYear.SequenceNumber.ShouldBe(1);
        secondYear.SequenceNumber.ShouldBe(1);
        secondYear.FinancialYearId.ShouldNotBe(firstYear.FinancialYearId);
    }

    [Fact]
    public async Task LockAndReadAsync_NoRowForTheSeriesYet_ReturnsNull()
    {
        // The first allocation of a financial year. The allocator distinguishes "no counter yet"
        // from "counter at zero" so it can create the row before trying to lock it.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await AddProvisionedCompanyAsync(host);

        var mark = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherNumberSequenceLock>()
            .LockAndReadAsync(Guid.NewGuid(), Guid.NewGuid()));

        mark.ShouldBeNull();
    }

    [Fact]
    public async Task LockAndReadAsync_AfterAnAllocation_ReturnsTheStoredMark()
    {
        // Proves the parameters bind the way the provider stores a GUID. If they did not, this
        // would read as "no sequence yet" and restart a live series at 1 — which is why the lock
        // binds GUIDs as provider parameters rather than formatting them into text.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        var paymentTypeId = await PaymentVoucherTypeIdAsync(host);

        var allocated = await AllocateAndSaveAsync(host, companyId, paymentTypeId, new DateOnly(2026, 5, 10));

        var mark = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherNumberSequenceLock>()
            .LockAndReadAsync(allocated.SeriesId, allocated.FinancialYearId));

        mark.ShouldBe((long?)allocated.SequenceNumber);
    }

    [Fact]
    public async Task LockAndReadAsync_Always_LeavesTheMarkAndTheTokenAlone()
    {
        // The lock is an UPDATE that sets the token to itself. If it bumped the token, the caller's
        // own save would then look like somebody else's change and fail its own concurrency check —
        // every post would report a phantom conflict against nobody.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var companyId = await AddProvisionedCompanyAsync(host);
        var paymentTypeId = await PaymentVoucherTypeIdAsync(host);

        await AllocateAndSaveAsync(host, companyId, paymentTypeId, new DateOnly(2026, 5, 10));

        var before = await SequenceStateAsync(host);

        await host.InScopeAsync(async sp =>
        {
            var sequenceLock = sp.GetRequiredService<IVoucherNumberSequenceLock>();

            await sequenceLock.LockAndReadAsync(before.SeriesId, before.FinancialYearId);
        });

        var after = await SequenceStateAsync(host);

        after.LastAllocatedNumber.ShouldBe(before.LastAllocatedNumber);
        after.ConcurrencyToken.ShouldBe(before.ConcurrencyToken);
    }

    [Fact]
    public async Task AdvanceTo_ALowerNumber_LeavesTheMarkWhereItIs()
    {
        // A number that has been handed out is spent whatever happens next, so moving the mark
        // backwards is the one operation that could issue it twice.
        var sequence = new VoucherNumberSequence(Guid.NewGuid(), Guid.NewGuid(), lastAllocatedNumber: 7);

        sequence.AdvanceTo(3);

        sequence.LastAllocatedNumber.ShouldBe(7);
        await Task.CompletedTask;
    }

    private static Task<VoucherNumberSequence> SequenceStateAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
        {
            var rows = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<VoucherNumberSequence>()
                .ListAsync();

            return rows.Single();
        });

    /// <summary>
    /// Allocates a number and saves, which is what the posting service does.
    /// </summary>
    /// <remarks>
    /// <c>AllocateAsync</c> deliberately only stages the counter bump — it runs inside the caller's
    /// transaction so that a voucher which fails validation after numbering consumes nothing, which
    /// is how the no-gap rule of <c>open-questions.md</c> #11 is kept. A test that forgot to save
    /// would therefore see every allocation return 1, which is exactly how these tests first failed.
    /// </remarks>
    private static Task<AllocatedVoucherNumber> AllocateAndSaveAsync(
        SqliteTestHost host,
        Guid companyId,
        Guid voucherTypeId,
        DateOnly voucherDate) =>
        host.InScopeAsync(async sp =>
        {
            var allocated = await sp.GetRequiredService<IVoucherNumberAllocator>()
                .AllocateAsync(companyId, voucherTypeId, voucherDate);

            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

            return allocated;
        });

    private static Task<Guid> PaymentVoucherTypeIdAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
        {
            var types = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<VoucherType>()
                .ListAsync();

            return types.Single(type => type.Name == "Payment").Id;
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
}
