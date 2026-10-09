using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Accounting;

/// <summary>
/// The books that C1.5, C1.6 and C1.16 are asserted against: a randomised set of more than 500
/// posted vouchers, spread across twelve ledgers of five natures, four voucher types and every
/// business date in financial year 2026-27 (spec §14, C1.5).
/// </summary>
/// <remarks>
/// A class fixture rather than a host per test, which is this assembly's usual idiom. Five
/// criteria are assertions about <i>one</i> dataset, and posting it is the expensive part —
/// building it once keeps the suite's runtime honest without collapsing five criteria into one
/// test name the evidence file cannot cite individually.
/// <para>
/// The generator is seeded with a constant. "Randomised" in the criterion means the figures are
/// not hand-picked to balance — it does not mean irreproducible, and evidence that cannot be
/// regenerated is not evidence (`specs/evidence/README.md`). A fixed seed gives both: the amounts
/// are arbitrary with respect to the arithmetic under test, and the owner can re-run this and get
/// the same trial balance as the one filed.
/// </para>
/// </remarks>
public sealed class RandomisedLedgerFixture : IAsyncLifetime, IAsyncDisposable
{
    /// <summary>Above the 500 the criterion demands, and not a round number.</summary>
    public const int VoucherCount = 523;

    private const int Seed = 20261005;

    private static readonly DateOnly YearStart = new(2026, 4, 1);
    private static readonly DateOnly YearEnd = new(2027, 3, 31);

    private SqliteTestHost _host = null!;

    public static DateOnly FromDate => YearStart;

    public static DateOnly ToDate => YearEnd;

    /// <summary>Every voucher posted, in the order it was posted.</summary>
    public IReadOnlyList<PostedVoucher> Posted { get; private set; } = [];

    public SqliteTestHost Host => _host;

    public async Task InitializeAsync()
    {
        _host = new SqliteTestHost();
        await _host.MigrateAsync();

        var books = await ArrangeLedgersAsync(_host);
        Posted = await PostRandomisedVouchersAsync(_host, books);

        await WriteStatementsForEvidenceAsync(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// The same teardown under the interface the analyser requires of a type holding a disposable
    /// field. xUnit drives <see cref="IAsyncLifetime.DisposeAsync"/>; if it reaches this one too,
    /// <see cref="SqliteTestHost"/>'s teardown tolerates being run twice.
    /// </summary>
    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    // ------------------------------------------------------------------ the generated books

    private static async Task<GeneratedBooks> ArrangeLedgersAsync(SqliteTestHost host)
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

        var groups = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var all = await unitOfWork.Repository<AccountGroup>().ListAsync();

            return all
                .Where(group => group.CompanyId == companyId)
                .ToDictionary(group => group.Name, group => group.Id);
        });

        // Two ledgers per nature that the balance sheet and P&L each have a side for, so C1.6 is
        // asserting over a sheet with something on both sides rather than a degenerate one.
        var created = new List<(string Name, Guid Id)>();

        foreach (var (name, groupName) in NewLedgers)
        {
            var ledger = await host.InScopeAsync(sp => sp
                .GetRequiredService<IChartOfAccountsService>()
                .CreateLedgerAsync(new CreateLedgerRequest(name, groups[groupName])));

            created.Add((name, ledger.Id));
        }

        var (seededLedgers, voucherTypes) = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var ledgers = await unitOfWork.Repository<Ledger>().ListAsync();
            var types = await unitOfWork.Repository<VoucherType>().ListAsync();

            return (
                ledgers
                    .Where(ledger => ledger.CompanyId == companyId && ledger.AllowsManualPosting)
                    .ToDictionary(ledger => ledger.Name, ledger => ledger.Id),
                types
                    .Where(type => type.CompanyId == companyId)
                    .ToDictionary(type => type.Name, type => (type.Id, type.Kind)));
        });

        // Profit & Loss A/c is excluded by AllowsManualPosting above: its figure is derived, and a
        // manual posting to it would be a stored balance in all but name (spec §4.5).
        var postable = created
            .Select(pair => pair.Id)
            .Concat(SeededLedgersUsed.Select(name => seededLedgers[name]))
            .ToArray();

        return new GeneratedBooks(
            companyId,
            PostableLedgerIds: postable,
            CashLedgerId: seededLedgers["Cash"],
            BankLedgerId: created.Single(pair => pair.Name == BankLedgerName).Id,
            PaymentTypeId: voucherTypes["Payment"].Id,
            ReceiptTypeId: voucherTypes["Receipt"].Id,
            JournalTypeId: voucherTypes["Journal"].Id,
            ContraTypeId: voucherTypes["Contra"].Id);
    }

    private const string BankLedgerName = "Karnataka Bank — Current A/c";

    /// <summary>
    /// The seeded ledgers (§4.5) the fixture posts to, alongside the ones it creates. Opening
    /// Balance Difference and Stock-in-Hand are left out: both belong to flows of their own.
    /// </summary>
    private static readonly string[] SeededLedgersUsed =
        ["Cash", "Sales", "Purchases", "Discount Allowed", "Discount Received"];

    private static readonly (string Name, string GroupName)[] NewLedgers =
    [
        (BankLedgerName, "Bank Accounts"),
        ("Ravi Motors", "Sundry Debtors"),
        ("Sri Lakshmi Traders", "Sundry Debtors"),
        ("Crompton Greaves Ltd", "Sundry Creditors"),
        ("Kirloskar Brothers Ltd", "Sundry Creditors"),
        ("Electricity Expenses", "Indirect Expenses"),
        ("Shop Rent", "Indirect Expenses"),
        ("Freight Inward", "Direct Expenses"),
        ("Commission Received", "Indirect Incomes"),
        ("Delivery Van", "Fixed Assets"),
    ];

    private static async Task<IReadOnlyList<PostedVoucher>> PostRandomisedVouchersAsync(
        SqliteTestHost host,
        GeneratedBooks books)
    {
        var random = new Random(Seed);
        var dayCount = YearEnd.DayNumber - YearStart.DayNumber + 1;
        var posted = new List<PostedVoucher>(VoucherCount);

        for (var index = 0; index < VoucherCount; index++)
        {
            var date = YearStart.AddDays(random.Next(dayCount));
            var request = NextRequest(random, books, date, index);

            var result = await host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(request));

            posted.Add(new PostedVoucher(result.VoucherId, result.Number, date, result.TotalDebit));
        }

        return posted;
    }

    private static PostVoucherRequest NextRequest(
        Random random,
        GeneratedBooks books,
        DateOnly date,
        int index)
    {
        // One voucher in nine is a Contra, which may only move value within cash and bank
        // (spec §8.3). The rest spread evenly over the three general-purpose types.
        if (index % 9 == 8)
        {
            var amount = NextAmount(random);
            var toBank = random.Next(2) == 0;

            return new PostVoucherRequest(
                books.ContraTypeId,
                date,
                [
                    new PostVoucherLine(
                        toBank ? books.BankLedgerId : books.CashLedgerId,
                        Debit: amount,
                        Credit: 0m),
                    new PostVoucherLine(
                        toBank ? books.CashLedgerId : books.BankLedgerId,
                        Debit: 0m,
                        Credit: amount),
                ],
                Narration: toBank ? "Cash deposited into bank" : "Cash drawn from bank");
        }

        var typeId = (index % 3) switch
        {
            0 => books.PaymentTypeId,
            1 => books.ReceiptTypeId,
            _ => books.JournalTypeId,
        };

        // Two to four lines: one or more on one side, balanced by a single plug on the other, so
        // every voucher balances by construction and no line is zero on both sides.
        var ledgers = PickDistinct(random, books.PostableLedgerIds, random.Next(2, 5));
        var debitSide = random.Next(2) == 0;
        var lines = new List<PostVoucherLine>(ledgers.Length);
        var total = 0m;

        for (var line = 0; line < ledgers.Length - 1; line++)
        {
            var amount = NextAmount(random);
            total += amount;

            lines.Add(debitSide
                ? new PostVoucherLine(ledgers[line], Debit: amount, Credit: 0m)
                : new PostVoucherLine(ledgers[line], Debit: 0m, Credit: amount));
        }

        lines.Add(debitSide
            ? new PostVoucherLine(ledgers[^1], Debit: 0m, Credit: total)
            : new PostVoucherLine(ledgers[^1], Debit: total, Credit: 0m));

        return new PostVoucherRequest(typeId, date, lines, Narration: $"Generated voucher {index + 1}");
    }

    /// <summary>
    /// An amount between ₹1.00 and ₹99,999.99 at two decimal places — the paise the business
    /// actually writes, chosen so that thirds and tenths accumulate across 523 vouchers rather
    /// cancelling neatly (C1.15 at C1.5 volume).
    /// </summary>
    private static decimal NextAmount(Random random) => random.Next(100, 10_000_000) / 100m;

    private static Guid[] PickDistinct(Random random, IReadOnlyList<Guid> source, int count)
    {
        var pool = source.ToList();
        var picked = new Guid[Math.Min(count, pool.Count)];

        for (var index = 0; index < picked.Length; index++)
        {
            var slot = random.Next(pool.Count);
            picked[index] = pool[slot];
            pool.RemoveAt(slot);
        }

        return picked;
    }

    /// <summary>
    /// Writes the three statements this fixture produces to the directory named by
    /// <c>VA_EVIDENCE_DIR</c>, for §15's "the trial balance, P&amp;L and balance sheet produced
    /// from the ≥500-voucher fixture, with the totals". Unset in a normal run, which then writes
    /// nothing — the assertions below are what gate the build; this only transcribes them.
    /// </summary>
    private static async Task WriteStatementsForEvidenceAsync(SqliteTestHost host)
    {
        var directory = Environment.GetEnvironmentVariable("VA_EVIDENCE_DIR");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);

        var reports = await host.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IAccountingReportService>();

            return (
                TrialBalance: await service.GetTrialBalanceAsync(ToDate),
                ProfitAndLoss: await service.GetProfitAndLossAsync(FromDate, ToDate),
                BalanceSheet: await service.GetBalanceSheetAsync(ToDate));
        });

        var text = new StringWriter();

        text.WriteLine($"Fixture: {VoucherCount} randomised vouchers, seed {Seed}, "
            + $"{FromDate:dd MMM yyyy} to {ToDate:dd MMM yyyy}");
        text.WriteLine();

        text.WriteLine($"TRIAL BALANCE as on {reports.TrialBalance.AsOnDate:dd MMM yyyy}");
        text.WriteLine($"{"Ledger",-34}{"Group",-24}{"Debit",18}{"Credit",18}");

        foreach (var row in reports.TrialBalance.Rows)
        {
            text.WriteLine($"{row.LedgerName,-34}{row.AccountGroupName,-24}"
                + $"{row.Debit,18:N2}{row.Credit,18:N2}");
        }

        text.WriteLine($"{"TOTAL",-58}{reports.TrialBalance.TotalDebit,18:N2}"
            + $"{reports.TrialBalance.TotalCredit,18:N2}");
        text.WriteLine($"Balanced: {reports.TrialBalance.IsBalanced}");
        text.WriteLine();

        text.WriteLine($"PROFIT & LOSS {reports.ProfitAndLoss.FromDate:dd MMM yyyy} "
            + $"to {reports.ProfitAndLoss.ToDate:dd MMM yyyy}");

        foreach (var row in reports.ProfitAndLoss.Rows)
        {
            text.WriteLine($"{row.LedgerName,-34}{row.Nature,-12}{row.Amount,18:N2}");
        }

        text.WriteLine($"Total income {reports.ProfitAndLoss.TotalIncome:N2}  "
            + $"total expense {reports.ProfitAndLoss.TotalExpense:N2}  "
            + $"gross {reports.ProfitAndLoss.GrossProfit:N2}  "
            + $"net {reports.ProfitAndLoss.NetProfit:N2}");
        text.WriteLine();

        text.WriteLine($"BALANCE SHEET as on {reports.BalanceSheet.AsOnDate:dd MMM yyyy}");
        text.WriteLine("Assets");

        foreach (var row in reports.BalanceSheet.Assets)
        {
            text.WriteLine($"  {row.LedgerName,-32}{row.Amount,18:N2}");
        }

        text.WriteLine($"  {"TOTAL ASSETS",-32}{reports.BalanceSheet.TotalAssets,18:N2}");
        text.WriteLine("Liabilities");

        foreach (var row in reports.BalanceSheet.Liabilities)
        {
            text.WriteLine($"  {row.LedgerName,-32}{row.Amount,18:N2}");
        }

        text.WriteLine($"  {"TOTAL LIABILITIES",-32}{reports.BalanceSheet.TotalLiabilities,18:N2}");
        text.WriteLine($"Balanced: {reports.BalanceSheet.IsBalanced}");

        await File.WriteAllTextAsync(
            Path.Combine(directory, "phase-1-stress-statements.txt"),
            text.ToString());
    }

    public sealed record PostedVoucher(Guid VoucherId, string Number, DateOnly Date, decimal Total);

    private sealed record GeneratedBooks(
        Guid CompanyId,
        IReadOnlyList<Guid> PostableLedgerIds,
        Guid CashLedgerId,
        Guid BankLedgerId,
        Guid PaymentTypeId,
        Guid ReceiptTypeId,
        Guid JournalTypeId,
        Guid ContraTypeId);
}

/// <summary>
/// C1.5, C1.6, C1.7, C1.16 and C1.19 asserted over the ≥500-voucher fixture (spec §14).
/// </summary>
/// <remarks>
/// Each of these holds trivially for the two- and three-voucher books in
/// <c>PostingWorkflowTests</c>. The point of this class is volume: 523 vouchers of arbitrary
/// two-decimal amounts across twelve ledgers is where a floating-point aggregation, an off-by-one
/// in the balance-sheet derivation, or a hash chain that only verifies for short runs would
/// actually show up.
/// </remarks>
public sealed class RandomisedLedgerFixtureTests(RandomisedLedgerFixture fixture)
    : IClassFixture<RandomisedLedgerFixture>
{
    [Fact]
    public void Fixture_PostsMoreThanFiveHundredVouchers()
    {
        // Guards the criterion's own premise: if the generator ever silently posts fewer, the four
        // tests below would still pass and would no longer be evidence for C1.5.
        fixture.Posted.Count.ShouldBe(RandomisedLedgerFixture.VoucherCount);
        fixture.Posted.Count.ShouldBeGreaterThanOrEqualTo(500);
        fixture.Posted.Select(voucher => voucher.Number).Distinct().Count()
            .ShouldBe(fixture.Posted.Count, "every voucher takes its own number");
    }

    [Fact]
    public async Task TrialBalance_OverFiveHundredRandomisedVouchers_TotalsToZeroExactly()
    {
        var trialBalance = await fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetTrialBalanceAsync(RandomisedLedgerFixture.ToDate));

        // No tolerance, and deliberately not ShouldBe(expected, tolerance): the aggregation is
        // integer arithmetic over minor units (ADR-0003), so anything but exact equality is a
        // defect rather than a rounding artefact.
        trialBalance.TotalDebit.ShouldBe(trialBalance.TotalCredit);
        trialBalance.IsBalanced.ShouldBeTrue();

        // And the same identity at the source, in the minor units the criterion names.
        var netMinor = await fixture.Host.InScopeAsync(async sp =>
        {
            var lines = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<JournalLine>()
                .ListAsync();

            return lines.Sum(line => line.AmountMinor);
        });

        netMinor.ShouldBe(0L);

        trialBalance.Rows.Count.ShouldBeGreaterThan(1);
        trialBalance.TotalDebit.ShouldBeGreaterThan(0m, "the fixture posted real amounts");
    }

    [Fact]
    public async Task BalanceSheet_OverFiveHundredRandomisedVouchers_BalancesExactly()
    {
        var balanceSheet = await fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetBalanceSheetAsync(RandomisedLedgerFixture.ToDate));

        balanceSheet.TotalAssets.ShouldBe(balanceSheet.TotalLiabilities);
        balanceSheet.IsBalanced.ShouldBeTrue();

        // C1.6 requires the P&L figure to come from the balance-sheet query rather than be
        // recomputed here, so the test cannot agree with a wrong sheet by making the same mistake.
        var profitAndLossRow = balanceSheet.Assets
            .Concat(balanceSheet.Liabilities)
            .Single(row => row.LedgerName == "Profit & Loss A/c");

        var profitAndLoss = await fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetProfitAndLossAsync(RandomisedLedgerFixture.FromDate, RandomisedLedgerFixture.ToDate));

        // The sheet carries the period's net result; a sign difference between the two statements
        // is the whole substance of the criterion, so compare magnitudes explicitly.
        Math.Abs(profitAndLossRow.Amount).ShouldBe(Math.Abs(profitAndLoss.NetProfit));
        profitAndLoss.TotalIncome.ShouldBeGreaterThan(0m);
        profitAndLoss.TotalExpense.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task DayBook_OverTheFixturePeriod_MatchesTheSumOfItsVouchers()
    {
        var dayBook = await fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetDayBookAsync(RandomisedLedgerFixture.FromDate, RandomisedLedgerFixture.ToDate));

        // One row per journal line, so the voucher set is what is compared, not the row count.
        dayBook.Select(row => row.VoucherId).Distinct().Count()
            .ShouldBe(fixture.Posted.Count);

        dayBook.Sum(row => row.Debit).ShouldBe(dayBook.Sum(row => row.Credit));
        dayBook.Sum(row => row.Debit).ShouldBe(fixture.Posted.Sum(voucher => voucher.Total));
        dayBook.ShouldNotContain(row => row.IsCancelled, "nothing in this fixture was cancelled");
    }

    [Fact]
    public async Task EditLogChain_OverFiveHundredRandomisedVouchers_VerifiesIntact()
    {
        var verification = await fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAuditLogReader>()
            .VerifyChainAsync());

        verification.IsIntact.ShouldBeTrue(verification.Detail);
        verification.FirstBrokenSequence.ShouldBeNull();

        // Every posted voucher is logged, so the chain being walked is the long one.
        verification.RowsChecked.ShouldBeGreaterThan(fixture.Posted.Count);
    }

    [Fact]
    public async Task EveryVoucher_OverTheFixture_HasExactlyOneCurrentRevision()
    {
        var revisions = await fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IUnitOfWork>()
            .Repository<VoucherRevision>()
            .ListAsync());

        revisions.Select(revision => revision.VoucherId).Distinct().Count()
            .ShouldBe(fixture.Posted.Count);

        foreach (var group in revisions.GroupBy(revision => revision.VoucherId))
        {
            group.Count(revision => revision.IsCurrent)
                .ShouldBe(1, $"voucher {group.Key} must have exactly one current revision");
        }

        // The invariant the deliberately-absent foreign key used to guarantee (C1.17, ADR-0002
        // Amendment 1): a supersession pointer resolves to a revision of the same voucher.
        var byId = revisions.ToDictionary(revision => revision.Id);

        foreach (var revision in revisions.Where(r => r.SupersededByRevisionId is not null))
        {
            byId.ShouldContainKey(revision.SupersededByRevisionId!.Value);
            byId[revision.SupersededByRevisionId!.Value].VoucherId.ShouldBe(revision.VoucherId);
        }
    }
}
