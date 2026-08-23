using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Inventory;

/// <summary>
/// The Phase 2 vertical slice exercised end to end against a real SQLite file: the unified stock
/// ledger, FIFO and weighted-average valuation, serial lifecycle, transfers, kit depletion, the
/// negative-stock policy and the C2.6 search budget (phase-2-inventory.md §3).
/// </summary>
/// <remarks>
/// Everything runs through the public application services over the real DI composition, each
/// operation in its own DI scope exactly as the shell would drive it. The stock side posts through
/// <see cref="IVoucherPostingService"/> internally, so every checkpoint here also proves the two
/// ledgers commit together or not at all.
/// </remarks>
public sealed class InventoryWorkflowTests
{
    private static readonly DateOnly InFy2026 = new(2026, 4, 15);

    // ---------------------------------------------------------------- C2.4 — valuation fixtures

    [Fact]
    public async Task Post_ReceiptsThenIssue_FifoValuationMatchesHandComputation()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var fifoItem = await CreateItemAsync(host, world.CompanyId, "Cisno Pump 1 HP", world.UnitId);
        await PostReceiptAsync(
            host,
            world,
            fifoItem.Id,
            godownId: world.MainGodownId,
            lines: [(10m, 100m), (5m, 120m)]);

        var issue = await PostIssueAsync(
            host,
            world,
            counterLedgerId: world.CashLedgerId,
            [(fifoItem.Id, world.MainGodownId, 12m)]);

        issue.Number.ShouldBe("SJ/0002");

        // Hand-computed FIFO: consumed value 10@100 + 2@120 = ₹1240 exactly; per unit
        // ₹103.3333 after the single per-unit rounding. The control account carries the booked
        // figure ₹1239.9996 net of receipts ₹1600.
        issue.Lines.Single().UnitCost.ShouldBe(103.3333m);

        var controlBalance = await ControlAccountBalanceMinorAsync(host, world.CompanyId);
        controlBalance.ShouldBe(3_600_004L);

        // The edit log watched the whole thing: stock rows are audited like any other.
        var auditTypes = await AuditEntityTypesAsync(host);
        auditTypes.ShouldContain(nameof(StockVoucher));
        auditTypes.ShouldContain(nameof(StockLedgerEntry));
    }

    [Fact]
    public async Task Post_IssueUnderWeightedAverage_CostsAtRunningAverage()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var waItem = await CreateItemAsync(
            host,
            world.CompanyId,
            "Cisno Pump WA",
            world.UnitId,
            ValuationMethod.WeightedAverage);

        await PostReceiptAsync(host, world, waItem.Id, world.MainGodownId, [(10m, 100m), (5m, 120m)]);

        var issue = await PostIssueAsync(
            host,
            world,
            world.CashLedgerId,
            [(waItem.Id, world.MainGodownId, 12m)]);

        // Hand-computed: position 15 units worth ₹1600 → average ₹106.6667 per unit.
        issue.Lines.Single().UnitCost.ShouldBe(106.6667m);

        // Booked at the rounded average: 12 × ₹106.6667 = ₹1280.0004 against ₹1600 received.
        var controlBalance = await ControlAccountBalanceMinorAsync(host, world.CompanyId);
        controlBalance.ShouldBe(3_199_996L);
    }

    // ---------------------------------------------------------------- C2.1 — reconciliation

    [Fact]
    public async Task ReconcileValuation_AfterReceiptIssueTransfer_MatchesControlAccount()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var item = await CreateItemAsync(host, world.CompanyId, "Cisno Pump", world.UnitId);
        await PostReceiptAsync(host, world, item.Id, world.MainGodownId, [(20m, 150m)]);
        await PostIssueAsync(host, world, world.CashLedgerId, [(item.Id, world.MainGodownId, 5m)]);

        var second = await CreateGodownAsync(host, world.CompanyId, "Branch Godown");
        await PostTransferAsync(
            host,
            world,
            [(item.Id, world.MainGodownId, second, 7m)]);

        var result = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockReportService>()
            .ReconcileValuationAsync(InFy2026));

        // Hand-computed: received 20@₹150 = ₹3000; issued 5 (₹750) and transferred 7 (₹1050)
        // out of the main godown — total stock value stays ₹3000 - ₹750 = ₹2250.
        result.TotalStockValueMinor.ShouldBe(22_500_000L);
        result.ControlAccountBalanceMinor.ShouldBe(result.TotalStockValueMinor);
        result.ControlLedgerName.ShouldBe("Stock-in-Hand");
    }

    // ---------------------------------------------------------------- C2.2 + C2.3 — serials

    [Fact]
    public async Task Post_SerialTrackedSale_RecordsExactlyWhichUnitWasSold()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var serialItem = await CreateSerialItemAsync(host, world.CompanyId, "Submersible Pump");
        await PostReceiptWithSerialsAsync(host, world, serialItem.Id, ["SN-001", "SN-002"]);

        var saleLine = await PostIssueWithSerialsAsync(
            host,
            world,
            serialItem.Id,
            world.MainGodownId,
            ["SN-001"]);

        var soldOn = await host.InScopeAsync(async sp =>
        {
            var serials = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<SerialNumber>()
                .ListAsync();

            return serials.Single(serial => serial.ValueNormalised == "SN-001");
        });

        soldOn.Status.ShouldBe(SerialNumberStatus.Sold);
        soldOn.SaleLineId.ShouldBe(saleLine.Line.Id);
        soldOn.SoldOn.ShouldBe(InFy2026);

        var stillInStock = await host.InScopeAsync(async sp =>
        {
            var serials = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<SerialNumber>()
                .ListAsync();

            return serials.Single(serial => serial.ValueNormalised == "SN-002");
        });

        stillInStock.Status.ShouldBe(SerialNumberStatus.InStock);
    }

    [Fact]
    public async Task Post_SecondSaleOfSameSerial_IsRejectedAndNothingMoves()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var serialItem = await CreateSerialItemAsync(host, world.CompanyId, "Submersible Pump");
        await PostReceiptWithSerialsAsync(host, world, serialItem.Id, ["SN-010"]);
        await PostIssueWithSerialsAsync(host, world, serialItem.Id, world.MainGodownId, ["SN-010"]);

        var rejection = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await PostIssueWithSerialsAsync(
                host,
                world,
                serialItem.Id,
                world.MainGodownId,
                ["SN-010"]));

        rejection.Message.ShouldContain("cannot leave stock twice");

        // The rejected attempt consumed nothing and moved nothing.
        var numbers = await PostedNumbersAsync(host, world.CompanyId);
        numbers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Validate_ReceiptRepeatingAnExistingSerial_IsAnErrorNotAWarning()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var serialItem = await CreateSerialItemAsync(host, world.CompanyId, "Submersible Pump");
        await PostReceiptWithSerialsAsync(host, world, serialItem.Id, ["SN-020"]);

        var result = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .ValidateAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                InFy2026,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        serialItem.Id,
                        world.MainGodownId,
                        1m,
                        UnitCost: 500m,
                        SerialNumbers: ["SN-020"]),
                ],
                Narration: "duplicate receipt")));

        result.HasErrors.ShouldBeTrue();
        result.Errors.ShouldContain(error => error.Contains("already exists"));
    }

    // ---------------------------------------------------------------- C2.5 — transfer

    [Fact]
    public async Task Transfer_ChangesLocationWithoutChangingTotalValue()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var item = await CreateItemAsync(host, world.CompanyId, "Openwell Pump", world.UnitId);
        await PostReceiptAsync(host, world, item.Id, world.MainGodownId, [(30m, 80m)]);

        var branch = await CreateGodownAsync(host, world.CompanyId, "Branch Godown");

        var beforeValue = await TotalStockValueMinorAsync(host, world.CompanyId);
        await PostTransferAsync(host, world, [(item.Id, world.MainGodownId, branch, 12m)]);
        var afterValue = await TotalStockValueMinorAsync(host, world.CompanyId);

        afterValue.ShouldBe(beforeValue);

        // The control account is untouched by a pure location change.
        var controlBalance = await ControlAccountBalanceMinorAsync(host, world.CompanyId);
        controlBalance.ShouldBe(24_000_000L); // 30 @ ₹80, no issue ever posted.

        var onHand = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockReportService>()
            .GetOnHandAsync(InFy2026));

        onHand.Single(row => row.GodownName == "Main Location").OnHand.ShouldBe(18m);
        onHand.Single(row => row.GodownName == "Branch Godown").OnHand.ShouldBe(12m);
    }

    // ---------------------------------------------------------------- negative stock (#20)

    [Fact]
    public async Task Post_IssueBeyondOnHand_WarnsThenPostsOnlyWhenConfirmed()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var item = await CreateItemAsync(host, world.CompanyId, "Centrifugal Pump", world.UnitId);
        await PostReceiptAsync(host, world, item.Id, world.MainGodownId, [(4m, 90m)]);

        var unconfirmed = new PostStockVoucherRequest(
            StockVoucherPurpose.Issue,
            InFy2026,
            [
                new StockVoucherLineRequest(
                    StockMovementDirection.Decrease,
                    item.Id,
                    world.MainGodownId,
                    9m),
            ],
            Narration: "more than we hold",
            CounterLedgerId: world.CashLedgerId);

        var validation = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .ValidateAsync(unconfirmed));

        validation.HasErrors.ShouldBeFalse();
        validation.Warnings.ShouldNotBeEmpty();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.InScopeAsync(sp => sp
                .GetRequiredService<IStockVoucherService>()
                .PostAsync(unconfirmed)));

        var confirmed = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .PostAsync(unconfirmed with { ConfirmNegativeStockWarnings = true }));

        confirmed.Number.ShouldNotBeEmpty();

        // Valuation carried the shortfall at the last known cost rather than inventing one.
        MoneyScale.FromMinor(confirmed.TotalValueMinor).ShouldBe(810.0000m);
    }

    // ---------------------------------------------------------------- kits

    [Fact]
    public async Task Post_KitIssue_DepletesComponentsAtTheirOwnCosts()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, world.CompanyId, "Pump Unit", world.UnitId);
        var cable = await CreateItemAsync(host, world.CompanyId, "Cable Set", world.UnitId);
        await PostReceiptAsync(host, world, pump.Id, world.MainGodownId, [(10m, 200m)]);
        await PostReceiptAsync(host, world, cable.Id, world.MainGodownId, [(50m, 10m)]);

        var kit = await CreateItemAsync(host, world.CompanyId, "Pump Kit", world.UnitId, isKit: true);
        await host.InScopeAsync(sp => sp
            .GetRequiredService<IInventoryMasterService>()
            .SetKitComponentsAsync(kit.Id, [(pump.Id, 1m), (cable.Id, 2m)]));

        var issued = await PostIssueAsync(
            host,
            world,
            world.CashLedgerId,
            [(kit.Id, world.MainGodownId, 3m)]);

        // Three kits deplete three pumps (3 × ₹200 = ₹600) and six cables (6 × ₹10 = ₹60).
        issued.Lines.Select(line => line.ItemName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["Cable Set", "Pump Unit"]);
        MoneyScale.FromMinor(issued.TotalValueMinor).ShouldBe(660.0000m);

        var onHand = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockReportService>()
            .GetOnHandAsync(InFy2026));

        onHand.Single(row => row.ItemId == pump.Id).OnHand.ShouldBe(7m);
        onHand.Single(row => row.ItemId == cable.Id).OnHand.ShouldBe(44m);
    }

    // ---------------------------------------------------------------- C2.6 — search budget

    [Fact]
    public async Task SearchItems_OverFiveThousandSkus_ReturnsCorrectRowsWithinBudget()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await SeedCatalogueAsync(host, world.CompanyId, world.UnitId, count: 5_000);

        var stopwatch = Stopwatch.StartNew();

        var matches = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockReportService>()
            .SearchItemsAsync("PUMP-25", take: 500));

        stopwatch.Stop();

        // Every SKU named "... PUMP-25xx" must come back, and nothing else.
        matches.Count.ShouldBe(100);
        matches.ShouldAllBe(row => row.Name.Contains("PUMP-25", StringComparison.OrdinalIgnoreCase));

        // The UI budget for an operator-facing lookup; the query itself resolves in single-digit
        // milliseconds on SQLite, so this generous ceiling catches only real regressions.
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(2_000);
    }

    // ---------------------------------------------------------------- arrange helpers

    private sealed record InventoryWorld(
        Guid CompanyId,
        Guid UnitId,
        Guid MainGodownId,
        Guid CashLedgerId);

    private static async Task<InventoryWorld> ArrangeAsync(SqliteTestHost host)
    {
        var companyId = await AddProvisionedCompanyAsync(host);

        return await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var unit = (await unitOfWork.Repository<UnitOfMeasure>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Code == "NOS");
            var godown = (await unitOfWork.Repository<Godown>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId);
            var cash = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Name == "Cash");

            return new InventoryWorld(companyId, unit.Id, godown.Id, cash.Id);
        });
    }

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

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().ProvisionAsync(companyId));

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureInventorySeedAsync(companyId));

        return companyId;
    }

    /// <summary>What the later helpers need from a saved item, without dragging the entity around.</summary>
    private sealed record ItemProxy(Guid Id, string Name);

    private static Task<ItemProxy> CreateItemAsync(
        SqliteTestHost host,
        Guid companyId,
        string name,
        Guid unitId,
        ValuationMethod? method = null,
        bool isKit = false) =>
        host.InScopeAsync(async sp =>
        {
            var saved = await sp.GetRequiredService<IInventoryMasterService>()
                .SaveItemAsync(new SaveItemRequest(
                    Id: null,
                    name,
                    unitId,
                    ValuationOverride: method,
                    IsKit: isKit));

            return new ItemProxy(saved.Id, saved.Name);
        });

    private static Task<ItemProxy> CreateSerialItemAsync(
        SqliteTestHost host,
        Guid companyId,
        string name) =>
        host.InScopeAsync(async sp =>
        {
            var masters = sp.GetRequiredService<IInventoryMasterService>();
            var unit = (await masters.ListUnitsAsync()).Single(unit => unit.Code == "NOS");

            var saved = await masters.SaveItemAsync(new SaveItemRequest(
                Id: null,
                name,
                unit.Id,
                IsSerialTracked: true));

            return new ItemProxy(saved.Id, saved.Name);
        });

    private static Task<Guid> CreateGodownAsync(
        SqliteTestHost host,
        Guid companyId,
        string name) =>
        host.InScopeAsync<Guid>(async sp =>
        {
            var created = await sp.GetRequiredService<IInventoryMasterService>()
                .CreateGodownAsync(new CreateGodownRequest(name));

            return created.Id;
        });

    private static Task<PostedStockVoucherSummary> PostReceiptAsync(
        SqliteTestHost host,
        InventoryWorld world,
        Guid itemId,
        Guid godownId,
        IReadOnlyList<(decimal Quantity, decimal Cost)> lines) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .PostAsync(BuildIncreaseRequest(
                StockVoucherPurpose.Receipt,
                itemId,
                godownId,
                lines,
                world.CashLedgerId)));

    private static Task<PostedStockVoucherSummary> PostReceiptWithSerialsAsync(
        SqliteTestHost host,
        InventoryWorld world,
        Guid itemId,
        IReadOnlyList<string> serials) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                InFy2026,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        itemId,
                        world.MainGodownId,
                        serials.Count,
                        UnitCost: 500m,
                        SerialNumbers: serials),
                ],
                Narration: "serial goods in",
                CounterLedgerId: world.CashLedgerId)));

    private static Task<PostedStockVoucherSummary> PostIssueAsync(
        SqliteTestHost host,
        InventoryWorld world,
        Guid counterLedgerId,
        IReadOnlyList<(Guid ItemId, Guid GodownId, decimal Quantity)> lines) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Issue,
                InFy2026,
                lines
                    .Select(line => new StockVoucherLineRequest(
                        StockMovementDirection.Decrease,
                        line.ItemId,
                        line.GodownId,
                        line.Quantity))
                    .ToList(),
                Narration: "goods out",
                CounterLedgerId: counterLedgerId)));

    private static Task<(PostedStockVoucherSummary Voucher, StockVoucherLine Line)> PostIssueWithSerialsAsync(
        SqliteTestHost host,
        InventoryWorld world,
        Guid itemId,
        Guid godownId,
        IReadOnlyList<string> serials) =>
        host.InScopeAsync<(PostedStockVoucherSummary, StockVoucherLine)>(async sp =>
        {
            var voucherService = sp.GetRequiredService<IStockVoucherService>();
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var posted = await voucherService.PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Issue,
                InFy2026,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Decrease,
                        itemId,
                        godownId,
                        serials.Count,
                        SerialNumbers: serials),
                ],
                Narration: "serial sale",
                CounterLedgerId: world.CashLedgerId));

            var lines = await unitOfWork.Repository<StockVoucherLine>().ListAsync();
            var line = lines.Single(candidate =>
                candidate.StockVoucherId == posted.Id && candidate.Direction == StockMovementDirection.Decrease);

            return (posted, line);
        });

    private static Task<PostedStockVoucherSummary> PostTransferAsync(
        SqliteTestHost host,
        InventoryWorld world,
        IReadOnlyList<(Guid ItemId, Guid FromGodownId, Guid ToGodownId, decimal Quantity)> legs) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Transfer,
                InFy2026,
                legs
                    .SelectMany(leg => new[]
                    {
                        new StockVoucherLineRequest(
                            StockMovementDirection.Decrease,
                            leg.ItemId,
                            leg.FromGodownId,
                            leg.Quantity),
                        new StockVoucherLineRequest(
                            StockMovementDirection.Increase,
                            leg.ItemId,
                            leg.ToGodownId,
                            leg.Quantity),
                    })
                    .ToList(),
                Narration: "moved between godowns")));

    private static PostStockVoucherRequest BuildIncreaseRequest(
        StockVoucherPurpose purpose,
        Guid itemId,
        Guid godownId,
        IReadOnlyList<(decimal Quantity, decimal Cost)> lines,
        Guid counterLedgerId) =>
        new(
            purpose,
            InFy2026,
            lines
                .Select(line => new StockVoucherLineRequest(
                    StockMovementDirection.Increase,
                    itemId,
                    godownId,
                    line.Quantity,
                    UnitCost: line.Cost))
                .ToList(),
            Narration: purpose.ToString(),
            CounterLedgerId: counterLedgerId);

    private static async Task SeedCatalogueAsync(
        SqliteTestHost host,
        Guid companyId,
        Guid unitId,
        int count)
    {
        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            foreach (var chunk in Chunk(Enumerable.Range(0, count), 500))
            {
                var items = chunk
                    .Select(index => new Item(
                        companyId,
                        $"Cisno PUMP-{index / 10:D3}{index % 10} HP Item",
                        unitId,
                        code: $"SKU{index:D5}"))
                    .ToList();

                await unitOfWork.Repository<Item>().AddRangeAsync(items);
                await unitOfWork.SaveChangesAsync();
            }
        });
    }

    private static IEnumerable<List<int>> Chunk(IEnumerable<int> source, int size)
    {
        var batch = new List<int>(size);

        foreach (var item in source)
        {
            batch.Add(item);

            if (batch.Count == size)
            {
                yield return batch;
                batch = [];
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    // ---------------------------------------------------------------- read helpers

    private static Task<long> ControlAccountBalanceMinorAsync(SqliteTestHost host, Guid companyId) =>
        host.InScopeAsync<long>(async sp =>
        {
            var balances = await sp.GetRequiredService<IAccountingReportService>()
                .GetTrialBalanceAsync(InFy2026.AddDays(15));

            return MoneyScale.ToMinor(
                balances.Rows.Single(row => row.LedgerName == "Stock-in-Hand").Debit
                - balances.Rows.Single(row => row.LedgerName == "Stock-in-Hand").Credit);
        });

    private static Task<long> TotalStockValueMinorAsync(SqliteTestHost host, Guid companyId) =>
        host.InScopeAsync<long>(async sp =>
        {
            var reconciliation = await sp.GetRequiredService<IStockReportService>()
                .ReconcileValuationAsync(InFy2026);

            return reconciliation.TotalStockValueMinor;
        });

    private static Task<IReadOnlyList<string>> PostedNumbersAsync(SqliteTestHost host, Guid companyId) =>
        host.InScopeAsync<IReadOnlyList<string>>(async sp =>
        {
            var recent = await sp.GetRequiredService<IStockVoucherService>()
                .ListRecentAsync(take: 100);

            return recent.Select(voucher => voucher.Number).ToList();
        });

    private static Task<IReadOnlyList<string>> AuditEntityTypesAsync(SqliteTestHost host) =>
        host.InScopeAsync<IReadOnlyList<string>>(async sp =>
        {
            var rows = await sp.GetRequiredService<IAuditLogReader>()
                .ListAsync(maxRows: 10_000);

            return rows
                .Select(row => row.EntityType)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        });
}
