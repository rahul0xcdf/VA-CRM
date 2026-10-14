using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Xunit.Abstractions;

namespace VinayakaAgencies.Infrastructure.Tests.Maintenance;

/// <summary>
/// Checkpoint C10.5: the named hot screens meet their response budget on a realistic dataset
/// (phase-10-hardening.md §2 and §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// The dataset is built once per class, because building it is the expensive part and four
/// criteria are assertions about one book. Its shape comes from §2: the ≥5,000-SKU catalogue
/// Phase 2 asked for, plus vouchers spread over several financial years rather than one.
/// </para>
/// <para>
/// <b>Each measurement is a median of repeated runs after a warm-up, and the assertion is at the
/// budget.</b> That combination is deliberate. A single timing on a developer machine that is also
/// running a browser is noise, and a test that fails on noise gets disabled, which is worse than
/// having no test. The median discards the one run that collided with something else; the warm-up
/// discards the first, which pays for the query plan and the connection. The budgets in D3 are
/// generous for exactly this reason — failing one means a real regression, not a busy afternoon.
/// </para>
/// <para>
/// The measured figures are written to <c>VA_EVIDENCE_DIR</c> when it is set, following the Phase 1
/// fixture, so the evidence file can quote what was actually observed rather than only that a
/// ceiling was not breached.
/// </para>
/// </remarks>
public sealed class HotPathPerformanceTests(RealisticDatasetFixture fixture, ITestOutputHelper output)
    : IClassFixture<RealisticDatasetFixture>
{
    /// <summary>Runs discarded before measuring, so a query plan is not timed as a query.</summary>
    private const int WarmUpRuns = 2;

    /// <summary>Measured runs. Odd, so the median is a measurement rather than an average.</summary>
    private const int MeasuredRuns = 5;

    private readonly RealisticDatasetFixture _fixture = fixture
        ?? throw new ArgumentNullException(nameof(fixture));

    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));

    [Fact]
    public async Task ItemLookupDuringBilling_OnTheRealisticCatalogue_MeetsThe300MsBudget()
    {
        var matches = await _fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IStockReportService>()
            .SearchItemsAsync("PUMP-25", take: 50));

        // The budget is only meaningful if the query actually matched something: a lookup that
        // returns nothing is fast for the wrong reason.
        matches.ShouldNotBeEmpty();

        var median = await MeasureAsync("Item lookup during billing", () => _fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IStockReportService>()
            .SearchItemsAsync("PUMP-25", take: 50)));

        // §4a D3. The one budget on the critical path of billing: the operator types this on every
        // line of every invoice, and past about 300 ms they outrun the list and stop trusting it.
        median.ShouldBeLessThan(300);
    }

    [Fact]
    public async Task DayBook_OverAMonthOfTheRealisticBook_MeetsThe1500MsBudget()
    {
        var from = new DateOnly(2026, 4, 1);
        var to = new DateOnly(2026, 4, 30);

        var rows = await _fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetDayBookAsync(from, to));

        rows.ShouldNotBeEmpty();

        var median = await MeasureAsync("Day book (one month)", () => _fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetDayBookAsync(from, to)));

        median.ShouldBeLessThan(1_500);
    }

    [Fact]
    public async Task TrialBalance_OverTheWholeLatestYear_MeetsThe2000MsBudget()
    {
        var trialBalance = await _fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetTrialBalanceAsync(RealisticDatasetFixture.LatestYearEnd));

        trialBalance.Rows.ShouldNotBeEmpty();

        // Worth asserting here as well as in the Phase 1 suite: a performance pass that made the
        // aggregate fast by making it wrong would otherwise pass.
        trialBalance.IsBalanced.ShouldBeTrue();

        var median = await MeasureAsync("Trial balance (year)", () => _fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<IAccountingReportService>()
            .GetTrialBalanceAsync(RealisticDatasetFixture.LatestYearEnd)));

        median.ShouldBeLessThan(2_000);
    }

    [Fact]
    public async Task CustomerStatement_OverThreeYearsOfPostings_MeetsThe1500MsBudget()
    {
        var customerId = _fixture.BusiestCustomerId;

        var statement = await _fixture.Host.InScopeAsync(sp => sp
            .GetRequiredService<ICustomerStatementService>()
            .CustomerStatementAsync(
                customerId,
                RealisticDatasetFixture.EarliestYearStart,
                RealisticDatasetFixture.LatestYearEnd));

        statement.ShouldNotBeNull();
        statement.Rows.ShouldNotBeEmpty();

        var median = await MeasureAsync(
            "Customer statement (three years)",
            () => _fixture.Host.InScopeAsync(sp => sp
                .GetRequiredService<ICustomerStatementService>()
                .CustomerStatementAsync(
                    customerId,
                    RealisticDatasetFixture.EarliestYearStart,
                    RealisticDatasetFixture.LatestYearEnd)));

        median.ShouldBeLessThan(1_500);
    }

    [Fact]
    public void TheDatasetIsActuallyRealistic()
    {
        // The budgets above are worthless if the book they ran against was small. This test states
        // the shape of the fixture as an assertion, so a future edit that quietly shrank it to make
        // the suite faster would fail here rather than silently weaken four other tests.
        _fixture.ItemCount.ShouldBeGreaterThanOrEqualTo(5_000);
        _fixture.VoucherCount.ShouldBeGreaterThanOrEqualTo(5_000);
        _fixture.FinancialYearCount.ShouldBeGreaterThanOrEqualTo(3);
        _fixture.CustomerCount.ShouldBeGreaterThanOrEqualTo(20);
        _fixture.StockLineCount.ShouldBeGreaterThanOrEqualTo(100);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> a few times and returns the median elapsed milliseconds.
    /// </summary>
    private async Task<long> MeasureAsync<T>(string label, Func<Task<T>> operation)
    {
        for (var run = 0; run < WarmUpRuns; run++)
        {
            await operation();
        }

        var timings = new List<long>(MeasuredRuns);

        for (var run = 0; run < MeasuredRuns; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            await operation();
            stopwatch.Stop();

            timings.Add(stopwatch.ElapsedMilliseconds);
        }

        timings.Sort();

        var median = timings[timings.Count / 2];

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{label,-34} median {median,6} ms   (runs: {string.Join(", ", timings)})");

        _output.WriteLine(line);
        RealisticDatasetFixture.RecordMeasurement(line);

        return median;
    }
}

/// <summary>
/// The realistic dataset C10.5 is measured against: a ≥5,000-SKU catalogue, three financial years
/// of vouchers posted through the real journal engine, customers with ledger history, and stock on
/// hand (phase-10-hardening.md §2).
/// </summary>
/// <remarks>
/// Vouchers go through <see cref="IVoucherPostingService"/> one at a time rather than being bulk
/// inserted, because the point is to measure reports over a book that was built the way a real one
/// is — with numbering, financial years and audit rows all present. The catalogue is bulk inserted,
/// because 5,000 items exercise the lookup's query and nothing about how they arrived.
/// </remarks>
public sealed class RealisticDatasetFixture : IAsyncLifetime, IAsyncDisposable
{
    /// <summary>Above the 5,000 §2 names.</summary>
    public const int CatalogueSize = 5_000;

    /// <summary>
    /// Vouchers spread over the three years below. Two thousand a year is a counter posting
    /// roughly seven a working day — below a busy shop, but enough that the aggregates are reading
    /// thousands of journal lines rather than hundreds.
    /// </summary>
    public const int TargetVoucherCount = 6_000;

    /// <summary>Items that receive stock, so the on-hand report has rows to aggregate.</summary>
    public const int StockedItemCount = 150;

    private const int CustomerTarget = 24;

    private const int Seed = 20261014;

    public static DateOnly EarliestYearStart => new(2024, 4, 1);

    public static DateOnly LatestYearEnd => new(2027, 3, 31);

    private static readonly List<string> Measurements = [];

    private static readonly (DateOnly Start, DateOnly End)[] Years =
    [
        (new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31)),
        (new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31)),
        (new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)),
    ];

    private SqliteTestHost _host = null!;

    public SqliteTestHost Host => _host;

    public Guid CompanyId { get; private set; }

    /// <summary>The customer with the most postings, so the statement test is a worst case.</summary>
    public Guid BusiestCustomerId { get; private set; }

    public int ItemCount { get; private set; }

    public int VoucherCount { get; private set; }

    public int CustomerCount { get; private set; }

    public int FinancialYearCount { get; private set; }

    public int StockLineCount { get; private set; }

    /// <summary>Collects a measurement for the evidence file (see <see cref="FlushMeasurements"/>).</summary>
    public static void RecordMeasurement(string line)
    {
        lock (Measurements)
        {
            Measurements.Add(line);
        }
    }

    public async Task InitializeAsync()
    {
        _host = new SqliteTestHost();
        await _host.MigrateAsync();

        CompanyId = await ArrangeCompanyAsync();
        ItemCount = await SeedCatalogueAsync();

        var customers = await SeedCustomersAsync();
        CustomerCount = customers.Count;

        VoucherCount = await PostVouchersAsync(customers);
        StockLineCount = await PostStockAsync();

        FinancialYearCount = await _host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<IFinancialYearProvider>().ListAsync()).Count);
    }

    public async Task DisposeAsync()
    {
        FlushMeasurements();
        await _host.DisposeAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    private async Task<Guid> ArrangeCompanyAsync()
    {
        var companyId = await _host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");

            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();

            return company.Id;
        });

        await _host.InScopeAsync(async sp =>
        {
            var provisioning = sp.GetRequiredService<ICompanyProvisioningService>();

            await provisioning.ProvisionAsync(companyId);
            await provisioning.EnsureInventorySeedAsync(companyId);
            await provisioning.EnsurePipelineSeedAsync(companyId);
            await provisioning.EnsureCustomerSeedAsync(companyId);
        });

        return companyId;
    }

    private Task<int> SeedCatalogueAsync() => _host.InScopeAsync(async sp =>
    {
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

        var unit = (await unitOfWork.Repository<UnitOfMeasure>().ListAsync())
            .Single(candidate => candidate.CompanyId == CompanyId && candidate.Code == "NOS");

        var added = 0;

        // In chunks, like the Phase 2 C2.6 fixture: one SaveChanges per 5,000 rows would hold the
        // whole change tracker in memory, and one per row would spend the fixture's time in EF.
        foreach (var chunk in Enumerable.Range(0, CatalogueSize).Chunk(500))
        {
            var items = chunk
                .Select(index => new Item(
                    CompanyId,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Cisno PUMP-{index / 10:D3}{index % 10} HP Item"),
                    unit.Id,
                    code: string.Create(CultureInfo.InvariantCulture, $"SKU{index:D5}")))
                .ToList();

            await unitOfWork.Repository<Item>().AddRangeAsync(items);
            await unitOfWork.SaveChangesAsync();

            added += items.Count;
        }

        return added;
    });

    /// <summary>
    /// Customers through the real service, so each one gets its auto-created Sundry Debtors ledger
    /// (Phase 4) and the statement under test has a party ledger to walk.
    /// </summary>
    private async Task<List<(Guid CustomerId, Guid LedgerId)>> SeedCustomersAsync()
    {
        var categoryId = await _host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<ICustomerCategoryService>().ListAsync())[0].Id);

        var created = new List<(Guid, Guid)>(CustomerTarget);

        for (var index = 0; index < CustomerTarget; index++)
        {
            var saved = await _host.InScopeAsync(sp => sp
                .GetRequiredService<ICustomerService>()
                .SaveAsync(new SaveCustomerRequest(
                    Id: null,
                    string.Create(CultureInfo.InvariantCulture, $"Counter Customer {index:D3}"),
                    categoryId)));

            created.Add((saved.Id, saved.LedgerId));
        }

        return created;
    }

    private async Task<int> PostVouchersAsync(List<(Guid CustomerId, Guid LedgerId)> customers)
    {
        var seed = await _host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var cash = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(candidate => candidate.CompanyId == CompanyId && candidate.Name == "Cash");
            var sales = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(candidate => candidate.CompanyId == CompanyId && candidate.Name == "Sales");
            var journal = (await unitOfWork.Repository<VoucherType>().ListAsync())
                .Single(candidate => candidate.CompanyId == CompanyId && candidate.Name == "Journal");
            var receipt = (await unitOfWork.Repository<VoucherType>().ListAsync())
                .Single(candidate => candidate.CompanyId == CompanyId && candidate.Name == "Receipt");

            return (CashId: cash.Id, SalesId: sales.Id, JournalId: journal.Id, ReceiptId: receipt.Id);
        });

        // The busiest customer takes every fourth voucher, so the statement test measures a party
        // ledger with real depth instead of one with three rows.
        BusiestCustomerId = customers[0].CustomerId;
        var busiestLedgerId = customers[0].LedgerId;

        var random = new Random(Seed);
        var posted = 0;

        for (var index = 0; index < TargetVoucherCount; index++)
        {
            var year = Years[index % Years.Length];
            var span = year.End.DayNumber - year.Start.DayNumber + 1;
            var date = year.Start.AddDays(random.Next(span));
            var amount = Math.Round((decimal)(random.NextDouble() * 9_000d) + 100m, 2);

            var partyLedgerId = index % 4 == 0
                ? busiestLedgerId
                : customers[random.Next(customers.Count)].LedgerId;

            // A credit sale to a party, then nothing else: debit the party, credit Sales. Balanced,
            // and it leaves the party ledger with the history a statement reads.
            var request = index % 2 == 0
                ? new PostVoucherRequest(
                    seed.JournalId,
                    date,
                    [
                        new PostVoucherLine(partyLedgerId, Debit: amount, Credit: 0m),
                        new PostVoucherLine(seed.SalesId, Debit: 0m, Credit: amount),
                    ],
                    "credit sale")
                : new PostVoucherRequest(
                    seed.ReceiptId,
                    date,
                    [
                        new PostVoucherLine(seed.CashId, Debit: amount, Credit: 0m),
                        new PostVoucherLine(partyLedgerId, Debit: 0m, Credit: amount),
                    ],
                    "receipt against account");

            await _host.InScopeAsync(sp => sp
                .GetRequiredService<IVoucherPostingService>()
                .PostAsync(request));

            posted++;
        }

        return posted;
    }

    private async Task<int> PostStockAsync()
    {
        var seed = await _host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var godown = (await unitOfWork.Repository<Godown>().ListAsync())
                .Single(candidate => candidate.CompanyId == CompanyId);
            var cash = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(candidate => candidate.CompanyId == CompanyId && candidate.Name == "Cash");

            var items = (await unitOfWork.Repository<Item>().ListAsync())
                .Where(candidate => candidate.CompanyId == CompanyId)
                .OrderBy(candidate => candidate.Code)
                .Take(StockedItemCount)
                .Select(candidate => candidate.Id)
                .ToList();

            return (GodownId: godown.Id, CashId: cash.Id, ItemIds: items);
        });

        // One receipt carrying every stocked line, rather than one voucher per item: the on-hand
        // report aggregates stock ledger rows, and this produces the same rows far faster.
        var lines = seed.ItemIds
            .Select((itemId, index) => new StockVoucherLineRequest(
                StockMovementDirection.Increase,
                itemId,
                seed.GodownId,
                Quantity: 5m + (index % 7),
                UnitCost: 100m + (index % 50)))
            .ToList();

        await _host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                new DateOnly(2026, 4, 10),
                lines,
                "opening counter stock",
                seed.CashId)));

        return lines.Count;
    }

    /// <summary>
    /// Writes the measured figures where the evidence file can quote them, following the Phase 1
    /// fixture's <c>VA_EVIDENCE_DIR</c> convention.
    /// </summary>
    private void FlushMeasurements()
    {
        var directory = Environment.GetEnvironmentVariable("VA_EVIDENCE_DIR");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        lock (Measurements)
        {
            if (Measurements.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(directory);

            var text = new List<string>
            {
                "Phase 10 C10.5 - hot-path response times",
                "========================================",
                string.Empty,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dataset: {ItemCount} items, {VoucherCount} vouchers across {FinancialYearCount} financial years,"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"         {CustomerCount} customers, {StockLineCount} stocked items. Seed {Seed}."),
                string.Empty,
            };

            text.AddRange(Measurements.OrderBy(line => line, StringComparer.Ordinal));

            File.WriteAllLines(Path.Combine(directory, "phase-10-hot-path-timings.txt"), text);
        }
    }
}
