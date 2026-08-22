using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Domain.Tests.Inventory;

public class StockLedgerEntryTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid GodownId = Guid.NewGuid();
    private static readonly Guid VoucherId = Guid.NewGuid();
    private static readonly Guid LineId = Guid.NewGuid();
    private static readonly DateOnly EntryDate = new(2026, 4, 15);

    [Fact]
    public void Constructor_IncreaseKind_FillsQuantityInOnly()
    {
        var entry = NewEntry(StockMovementKind.Receipt, quantity: 2.5m, unitCostMinor: 100_000L);

        entry.Direction.ShouldBe(StockMovementDirection.Increase);
        entry.QuantityIn.ShouldBe(2.5m);
        entry.QuantityOut.ShouldBe(0m);
        entry.SignedQuantity.ShouldBe(2.5m);
    }

    [Fact]
    public void Constructor_DecreaseKind_FillsQuantityOutOnly()
    {
        var entry = NewEntry(StockMovementKind.Issue, quantity: 1.5m, unitCostMinor: 100_000L);

        entry.Direction.ShouldBe(StockMovementDirection.Decrease);
        entry.QuantityIn.ShouldBe(0m);
        entry.QuantityOut.ShouldBe(1.5m);
        entry.SignedQuantity.ShouldBe(-1.5m);
    }

    [Fact]
    public void Constructor_ZeroQuantity_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => NewEntry(StockMovementKind.Receipt, 0m, 0L));
    }

    [Fact]
    public void ComputeValueMinor_RoundsHalfToEvenOnce()
    {
        // 2 units at 10.0005 (100005 minor) = 200010 exactly.
        StockLedgerEntry.ComputeValueMinor(2m, 100_005L).ShouldBe(200_010L);

        // 0.5 unit at 10.0005 = 50002.5 minor — the single explicit rounding point lands on even.
        StockLedgerEntry.ComputeValueMinor(0.5m, 100_005L).ShouldBe(50_002L);
        StockLedgerEntry.ComputeValueMinor(1.5m, 100_005L).ShouldBe(150_008L);
    }

    [Fact]
    public void UnitCostFromValue_RecoversCostWithinRounding()
    {
        var cost = StockLedgerEntry.UnitCostFromValue(200_010L, 2m);

        StockLedgerEntry.ComputeValueMinor(2m, cost).ShouldBe(200_010L);
    }

    private static StockLedgerEntry NewEntry(
        StockMovementKind kind,
        decimal quantity,
        long unitCostMinor) =>
        new(
            CompanyId,
            globalSequence: 1,
            EntryDate,
            kind,
            ItemId,
            GodownId,
            quantity,
            unitCostMinor,
            VoucherId,
            LineId,
            batchNumber: null);
}
