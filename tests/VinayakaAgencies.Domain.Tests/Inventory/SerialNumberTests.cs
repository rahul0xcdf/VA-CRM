using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Domain.Tests.Inventory;

public class SerialNumberTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid GodownId = Guid.NewGuid();
    private static readonly Guid ReceiptLineId = Guid.NewGuid();

    [Fact]
    public void Constructor_NormalisesValueCase()
    {
        var serial = NewSerial(" sn-001 ");

        serial.Value.ShouldBe("sn-001");
        serial.ValueNormalised.ShouldBe("SN-001");
        serial.Status.ShouldBe(SerialNumberStatus.InStock);
    }

    // C2.2 — the lifecycle: received → in stock → sold.
    [Fact]
    public void MarkSold_InStockSerial_RecordsSaleAgainstLine()
    {
        var serial = NewSerial("SN-002");
        var saleLineId = Guid.NewGuid();
        var soldOn = new DateOnly(2026, 5, 1);

        serial.MarkSold(saleLineId, soldOn);

        serial.Status.ShouldBe(SerialNumberStatus.Sold);
        serial.SaleLineId.ShouldBe(saleLineId);
        serial.SoldOn.ShouldBe(soldOn);
    }

    // C2.3 — the same serial cannot be sold twice.
    [Fact]
    public void MarkSold_AlreadySold_Throws()
    {
        var serial = NewSerial("SN-003");
        serial.MarkSold(Guid.NewGuid(), new DateOnly(2026, 5, 1));

        var rejection = Should.Throw<InvalidOperationException>(
            () => serial.MarkSold(Guid.NewGuid(), new DateOnly(2026, 6, 1)));

        rejection.Message.ShouldContain("cannot be sold again");
    }

    [Fact]
    public void ReturnToStock_SoldSerial_BackInStockWithSaleLinkCleared()
    {
        var serial = NewSerial("SN-004");
        serial.MarkSold(Guid.NewGuid(), new DateOnly(2026, 5, 1));

        var returnGodownId = Guid.NewGuid();
        serial.ReturnToStock(returnGodownId);

        serial.Status.ShouldBe(SerialNumberStatus.InStock);
        serial.CurrentGodownId.ShouldBe(returnGodownId);
        serial.SaleLineId.ShouldBeNull();
        serial.SoldOn.ShouldBeNull();
    }

    [Fact]
    public void ReturnToStock_InStockSerial_Throws()
    {
        var serial = NewSerial("SN-005");

        Should.Throw<InvalidOperationException>(() => serial.ReturnToStock(Guid.NewGuid()));
    }

    [Fact]
    public void MoveTo_InStockSerial_ChangesGodownWithoutSellingIt()
    {
        var serial = NewSerial("SN-006");
        var destination = Guid.NewGuid();

        serial.MoveTo(destination);

        serial.Status.ShouldBe(SerialNumberStatus.InStock);
        serial.CurrentGodownId.ShouldBe(destination);
    }

    [Fact]
    public void MoveTo_SoldSerial_Throws()
    {
        var serial = NewSerial("SN-007");
        serial.MarkSold(Guid.NewGuid(), new DateOnly(2026, 5, 1));

        Should.Throw<InvalidOperationException>(() => serial.MoveTo(Guid.NewGuid()));
    }

    private static SerialNumber NewSerial(string value) =>
        new(CompanyId, ItemId, value, GodownId, ReceiptLineId, new DateOnly(2026, 4, 15));
}
