using Shouldly;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Tests.TestDoubles;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Application.Tests.Inventory;

/// <summary>
/// Checkpoint C2.4: FIFO and weighted-average valuations are each verified against a
/// hand-computed fixture, worked out on paper before the code was written.
/// </summary>
public sealed class StockValuationServiceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly DateOnly Day1 = new(2026, 4, 1);

    // The fixture's ledger, in posting order:
    //   seq 1  IN  10 @ 100.0000   (value 1_000.0000)
    //   seq 2  IN   5 @ 120.0000   (value   600.0000)
    private static readonly long CostMinorA = 1_000_000L;
    private static readonly long CostMinorB = 1_200_000L;

    [Fact]
    public async Task ValueIssue_FifoAcrossTwoLayers_CostsOldestFirst()
    {
        var (service, itemId) = await ArrangeAsync(ValuationMethod.Fifo, itemOverride: null);

        // Hand-computed: 12 units consume all 10 @ 100 and 2 of the 5 @ 120 →
        // value 10_000_000 + 2_400_000 = 12_400_000 minor; per unit 1_033_333.33… minor.
        var valued = await service.ValueIssueAsync(itemId, 12m, Day1);

        valued.Method.ShouldBe(ValuationMethod.Fifo);
        valued.UnitCostMinor.ShouldBe(1_033_333L);
        valued.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ValueIssue_FifoAfterFirstIssue_ContinuesFromTheRemainingLayer()
    {
        var unitOfWork = await BuildLedgerAsync(ValuationMethod.Fifo, itemOverride: null);

        // Record the first issue as history, exactly as posting would: 12 units valued at
        // 1_033_333 minor per unit leave only the second layer on the shelf.
        var item = unitOfWork.FakeRepositoryFor<Item>().Items.Single();
        var godownId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await unitOfWork.Repository<StockLedgerEntry>().AddAsync(new StockLedgerEntry(
            CompanyId,
            globalSequence: 3,
            Day1,
            StockMovementKind.Issue,
            item.Id,
            godownId,
            quantity: 12m,
            unitCostMinor: 1_033_333L,
            voucherId,
            Guid.NewGuid(),
            batchNumber: null));

        var service = new StockValuationService(unitOfWork, new StubCompanyContext(CompanyId));

        // Hand-computed: what remains is only the second layer.
        var valued = await service.ValueIssueAsync(item.Id, 3m, Day1);

        valued.UnitCostMinor.ShouldBe(CostMinorB);
    }

    [Fact]
    public async Task ValueIssue_WeightedAverage_CostsAtRunningAverage()
    {
        var (service, itemId) = await ArrangeAsync(ValuationMethod.WeightedAverage, itemOverride: null);

        // Hand-computed: position is 15 units worth 16_000_000 minor → average 1_066_666.67,
        // rounded once to 1_066_667 minor per unit.
        var valued = await service.ValueIssueAsync(itemId, 15m, Day1);

        valued.Method.ShouldBe(ValuationMethod.WeightedAverage);
        valued.UnitCostMinor.ShouldBe(1_066_667L);
    }

    [Fact]
    public async Task ValueIssue_ItemOverride_WinsOverCompanyDefault()
    {
        var (service, itemId) = await ArrangeAsync(
            ValuationMethod.Fifo,
            itemOverride: ValuationMethod.WeightedAverage);

        var valued = await service.ValueIssueAsync(itemId, 15m, Day1);

        valued.Method.ShouldBe(ValuationMethod.WeightedAverage);
        valued.UnitCostMinor.ShouldBe(1_066_667L);
    }

    [Fact]
    public async Task EffectiveMethod_NoOverride_ReturnsCompanyDefault()
    {
        var (service, itemId) = await ArrangeAsync(ValuationMethod.Fifo, itemOverride: null);

        (await service.EffectiveMethodAsync(itemId)).ShouldBe(ValuationMethod.Fifo);
    }

    /// <summary>
    /// Builds the two-layer ledger through a fake unit of work and returns the service over it.
    /// GlobalSequence is assigned in list order exactly as the posting path would.
    /// </summary>
    private static async Task<(StockValuationService Service, Guid ItemId)> ArrangeAsync(
        ValuationMethod defaultMethod,
        ValuationMethod? itemOverride)
    {
        var unitOfWork = await BuildLedgerAsync(defaultMethod, itemOverride);

        var itemId = unitOfWork.FakeRepositoryFor<Item>().Items.Single().Id;

        return (new StockValuationService(unitOfWork, new StubCompanyContext(CompanyId)), itemId);
    }

    private static async Task<FakeUnitOfWork> BuildLedgerAsync(
        ValuationMethod defaultMethod,
        ValuationMethod? itemOverride)
    {
        var unitOfWork = new FakeUnitOfWork();
        var companyContext = new StubCompanyContext(CompanyId);

        var settings = new InventorySettings(CompanyId, defaultMethod);
        await unitOfWork.Repository<InventorySettings>().AddAsync(settings);

        var baseUnitId = Guid.NewGuid();
        var item = new Item(CompanyId, "Cisno Pump 1 HP", baseUnitId);
        item.SetValuationOverride(itemOverride);
        await unitOfWork.Repository<Item>().AddAsync(item);

        var godownId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await AddEntryAsync(unitOfWork, sequence: 1, item.Id, godownId, voucherId,
            StockMovementKind.Receipt, quantityIn: 10m, costMinor: CostMinorA);
        await AddEntryAsync(unitOfWork, sequence: 2, item.Id, godownId, voucherId,
            StockMovementKind.Receipt, quantityIn: 5m, costMinor: CostMinorB);

        return unitOfWork;
    }

    private static async Task AddEntryAsync(
        FakeUnitOfWork unitOfWork,
        long sequence,
        Guid itemId,
        Guid godownId,
        Guid voucherId,
        StockMovementKind kind,
        decimal quantityIn,
        long costMinor)
    {
        var lineId = Guid.NewGuid();

        await unitOfWork.Repository<StockLedgerEntry>().AddAsync(new StockLedgerEntry(
            CompanyId,
            sequence,
            Day1,
            kind,
            itemId,
            godownId,
            quantityIn,
            costMinor,
            voucherId,
            lineId,
            batchNumber: null));
    }
}

