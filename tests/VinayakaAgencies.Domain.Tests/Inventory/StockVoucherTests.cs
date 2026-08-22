using VinayakaAgencies.Domain.Inventory;

namespace VinayakaAgencies.Domain.Tests.Inventory;

public class StockVoucherTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid GodownId = Guid.NewGuid();
    private static readonly Guid CounterLedgerId = Guid.NewGuid();
    private static readonly DateOnly VoucherDate = new(2026, 4, 15);

    [Fact]
    public void AddLine_ReceiptWithDecreaseLine_Throws()
    {
        var voucher = NewVoucher(StockVoucherPurpose.Receipt);

        Should.Throw<InvalidOperationException>(() => AddLine(
            voucher,
            StockVoucherPurpose.Receipt,
            StockMovementDirection.Decrease));
    }

    [Fact]
    public void AddLine_IssueWithIncreaseLine_Throws()
    {
        var voucher = NewVoucher(StockVoucherPurpose.Issue);
        voucher.SetCounterLedger(CounterLedgerId);

        Should.Throw<InvalidOperationException>(() => AddLine(
            voucher,
            StockVoucherPurpose.Issue,
            StockMovementDirection.Increase));
    }

    [Fact]
    public void AddLine_LineNumbersAssignedInOrder()
    {
        var voucher = NewVoucher(StockVoucherPurpose.Receipt);
        voucher.SetCounterLedger(CounterLedgerId);

        var first = AddLine(voucher, StockVoucherPurpose.Receipt, StockMovementDirection.Increase);
        var second = AddLine(voucher, StockVoucherPurpose.Receipt, StockMovementDirection.Increase);

        first.LineNumber.ShouldBe(1);
        second.LineNumber.ShouldBe(2);
    }

    [Fact]
    public void SetCounterLedger_ReceiptWithoutLedger_Throws()
    {
        var voucher = NewVoucher(StockVoucherPurpose.Receipt);

        var rejection = Should.Throw<InvalidOperationException>(
            () => voucher.SetCounterLedger(null));

        rejection.Message.ShouldContain("must name the ledger");
    }

    [Fact]
    public void SetCounterLedger_TransferWithCounterLedger_Throws()
    {
        var voucher = NewVoucher(StockVoucherPurpose.Transfer);

        var rejection = Should.Throw<InvalidOperationException>(
            () => voucher.SetCounterLedger(CounterLedgerId));

        rejection.Message.ShouldContain("no non-stock leg");
    }

    // C2.5 — a transfer balances per item or it does not post.
    [Fact]
    public void ValidateTransferBalance_ItemShortByOneUnit_Throws()
    {
        var voucher = NewVoucher(StockVoucherPurpose.Transfer);
        AddTransferLegs(voucher, outQuantity: 10m, inQuantity: 9m);

        var rejection = Should.Throw<InvalidOperationException>(
            voucher.ValidateTransferBalance);

        rejection.Message.ShouldContain("do not balance");
    }

    [Fact]
    public void ValidateTransferBalanced_BalancedAcrossGodowns_Passes()
    {
        var voucher = NewVoucher(StockVoucherPurpose.Transfer);
        AddTransferLegs(voucher, outQuantity: 10m, inQuantity: 10m);

        Should.NotThrow(voucher.ValidateTransferBalance);
    }

    [Fact]
    public void AttachPostedVoucher_Twice_Throws()
    {
        var voucher = NewVoucher(StockVoucherPurpose.Receipt);
        voucher.AttachPostedVoucher(Guid.NewGuid(), "SJ/0001");

        var rejection = Should.Throw<InvalidOperationException>(
            () => voucher.AttachPostedVoucher(Guid.NewGuid(), "SJ/0002"));

        rejection.Message.ShouldContain("already been posted");
    }

    private static StockVoucher NewVoucher(StockVoucherPurpose purpose) =>
        new(CompanyId, purpose, VoucherDate, "test narration");

    private static StockVoucherLine AddLine(
        StockVoucher voucher,
        StockVoucherPurpose purpose,
        StockMovementDirection direction) =>
        voucher.AddLine(
            direction,
            ItemId,
            GodownId,
            quantity: 1m,
            isSerialTracked: false,
            isBatchTracked: false,
            unitCost: 0m,
            batchNumber: null);

    private static void AddTransferLegs(
        StockVoucher voucher,
        decimal outQuantity,
        decimal inQuantity)
    {
        voucher.AddLine(
            StockMovementDirection.Decrease,
            ItemId,
            Guid.NewGuid(),
            outQuantity,
            isSerialTracked: false,
            isBatchTracked: false);
        voucher.AddLine(
            StockMovementDirection.Increase,
            ItemId,
            Guid.NewGuid(),
            inQuantity,
            isSerialTracked: false,
            isBatchTracked: false);
    }
}
