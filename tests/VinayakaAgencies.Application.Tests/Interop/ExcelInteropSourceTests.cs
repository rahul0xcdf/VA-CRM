using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Interop;
using Shouldly;

namespace VinayakaAgencies.Application.Tests.Interop;

/// <summary>
/// The column-mapping step: the owner's own sheet layout read into the canonical model.
/// </summary>
/// <remarks>
/// C8.3 is the headline — an item import with a non-standard column order succeeds — and it is
/// tested by putting the columns in an order nothing could have anticipated, with two unmapped
/// columns in the middle for good measure. The rest of these are the failures that make the
/// difference between a mapping step and a guess: a header the sheet does not have, a field the
/// target does not have, and a required field nobody mapped.
/// </remarks>
public sealed class ExcelInteropSourceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static ExcelColumnMapping Mapping(
        ExcelImportTarget target,
        (string Header, string Field)[] columns,
        int headerRow = 1,
        string? sheet = null)
    {
        var mapping = new ExcelColumnMapping(CompanyId, "Supplier sheet", target, sheet, headerRow);

        mapping.SetColumns(columns.Select(column =>
            new ExcelColumnMappingColumn(CompanyId, mapping.Id, column.Header, column.Field)));

        return mapping;
    }

    private static Task<List<InteropRecord>> ReadAsync(IWorkbookReader reader, ExcelColumnMapping mapping) =>
        new ExcelInteropSource(reader, "prices.xlsx", mapping).ReadAllAsync();

    /// <summary>C8.3 — the columns are in the supplier's order, not ours, and two are not ours at all.</summary>
    [Fact]
    public async Task ReadAsync_ItemsInANonStandardColumnOrder_ImportsThemAnyway()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Warranty", "UOM", "HSN", "Part No", "Our Ref", "Description of Goods"],
            ["24 months", "Nos", "8413", "OW-1HP", "ignored", "Openwell Pump 1 HP"],
            ["12 months", "Nos", "8413", "SUB-2HP", "ignored", "Submersible 2 HP"]);

        var mapping = Mapping(
            ExcelImportTarget.Items,
            [
                ("Description of Goods", InteropFieldCatalogue.Name),
                ("UOM", InteropFieldCatalogue.BaseUnit),
                ("Part No", InteropFieldCatalogue.Code),
                ("HSN", InteropFieldCatalogue.HsnSac),
            ]);

        var records = await ReadAsync(reader, mapping);

        records.OfType<InteropRecord.Unreadable>().ShouldBeEmpty();

        var items = records.OfType<InteropRecord.Item>().Select(record => record.Value).ToList();
        items.Count.ShouldBe(2);
        items[0].Name.ShouldBe("Openwell Pump 1 HP");
        items[0].BaseUnitCode.ShouldBe("Nos");
        items[0].Code.ShouldBe("OW-1HP");
        items[0].HsnSacCode.ShouldBe("8413");
        items[1].Name.ShouldBe("Submersible 2 HP");
    }

    [Fact]
    public async Task ReadAsync_HeadersOnARowOtherThanTheFirst_StillFindsThem()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Price list — April 2026"],
            [],
            ["Item", "Unit"],
            ["Openwell Pump", "Nos"]);

        var mapping = Mapping(
            ExcelImportTarget.Items,
            [("Item", InteropFieldCatalogue.Name), ("Unit", InteropFieldCatalogue.BaseUnit)],
            headerRow: 3);

        var records = await ReadAsync(reader, mapping);

        records.OfType<InteropRecord.Item>().ShouldHaveSingleItem().Value.Name.ShouldBe("Openwell Pump");
    }

    /// <summary>Header matching ignores case and extra spacing, as a real sheet requires.</summary>
    [Fact]
    public async Task ReadAsync_HeaderDifferingOnlyInCaseAndSpacing_StillMatches()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["  ITEM   NAME ", "unit"],
            ["Openwell Pump", "Nos"]);

        var mapping = Mapping(
            ExcelImportTarget.Items,
            [("Item Name", InteropFieldCatalogue.Name), ("Unit", InteropFieldCatalogue.BaseUnit)]);

        (await ReadAsync(reader, mapping)).OfType<InteropRecord.Item>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ReadAsync_MappedHeaderMissingFromTheSheet_SaysWhichOne()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Item Name"],
            ["Openwell Pump"]);

        var mapping = Mapping(
            ExcelImportTarget.Items,
            [("Item Name", InteropFieldCatalogue.Name), ("Unit", InteropFieldCatalogue.BaseUnit)]);

        var problem = (await ReadAsync(reader, mapping))
            .ShouldHaveSingleItem()
            .ShouldBeOfType<InteropRecord.Unreadable>();

        problem.Reason.ShouldContain("'Unit'");
    }

    [Fact]
    public async Task ReadAsync_MappingLeavingARequiredFieldUnmapped_IsRefusedBeforeAnyRowIsRead()
    {
        var reader = FakeWorkbookReader.WithRows(["Item Name"], ["Openwell Pump"]);
        var mapping = Mapping(ExcelImportTarget.Items, [("Item Name", InteropFieldCatalogue.Name)]);

        (await ReadAsync(reader, mapping))
            .ShouldHaveSingleItem()
            .ShouldBeOfType<InteropRecord.Unreadable>()
            .Reason.ShouldContain(InteropFieldCatalogue.BaseUnit);
    }

    [Fact]
    public async Task ReadAsync_BlankRequiredCell_ReportsThatRowAndKeepsTheOthers()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Item", "Unit"],
            ["Openwell Pump", "Nos"],
            ["", "Nos"],
            ["Submersible", "Nos"]);

        var mapping = Mapping(
            ExcelImportTarget.Items,
            [("Item", InteropFieldCatalogue.Name), ("Unit", InteropFieldCatalogue.BaseUnit)]);

        var records = await ReadAsync(reader, mapping);

        records.OfType<InteropRecord.Item>().Count().ShouldBe(2);
        var problem = records.OfType<InteropRecord.Unreadable>().ShouldHaveSingleItem();
        problem.Location.Description.ShouldContain("row 3");
    }

    [Fact]
    public async Task ReadAsync_BlankRow_IsSkippedRatherThanReported()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Item", "Unit"],
            ["Openwell Pump", "Nos"],
            [null, null],
            ["Submersible", "Nos"]);

        var mapping = Mapping(
            ExcelImportTarget.Items,
            [("Item", InteropFieldCatalogue.Name), ("Unit", InteropFieldCatalogue.BaseUnit)]);

        var records = await ReadAsync(reader, mapping);

        records.Count.ShouldBe(2);
        records.OfType<InteropRecord.Unreadable>().ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- vouchers across rows

    [Fact]
    public async Task ReadAsync_VoucherSpanningTwoRows_BecomesOneVoucherWithTwoLines()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Vch No", "Vch Date", "Type", "Ledger", "Dr", "Cr"],
            ["JRN/0001", "2026-04-15", "Journal", "Cash", "1500.00", ""],
            ["JRN/0001", "2026-04-15", "Journal", "Sales", "", "1500.00"],
            ["JRN/0002", "2026-04-16", "Journal", "Cash", "250.50", ""],
            ["JRN/0002", "2026-04-16", "Journal", "Sales", "", "250.50"]);

        var mapping = Mapping(
            ExcelImportTarget.Vouchers,
            [
                ("Vch No", InteropFieldCatalogue.Number),
                ("Vch Date", InteropFieldCatalogue.Date),
                ("Type", InteropFieldCatalogue.VoucherType),
                ("Ledger", InteropFieldCatalogue.Ledger),
                ("Dr", InteropFieldCatalogue.Debit),
                ("Cr", InteropFieldCatalogue.Credit),
            ]);

        var vouchers = (await ReadAsync(reader, mapping))
            .OfType<InteropRecord.Voucher>()
            .Select(record => record.Value)
            .ToList();

        vouchers.Count.ShouldBe(2);
        vouchers[0].Number.ShouldBe("JRN/0001");
        vouchers[0].Date.ShouldBe(new DateOnly(2026, 4, 15));
        vouchers[0].Kind.ShouldBe(VoucherKind.Journal);
        vouchers[0].LedgerEntries.Count.ShouldBe(2);
        vouchers[0].LedgerEntries[0].AmountMinor.ShouldBe(MoneyScale.ToMinor(1_500m));
        vouchers[0].LedgerEntries[1].AmountMinor.ShouldBe(MoneyScale.ToMinor(-1_500m));
        vouchers[0].IsBalanced.ShouldBeTrue();
        vouchers[1].LedgerEntries[0].AmountMinor.ShouldBe(MoneyScale.ToMinor(250.50m));
    }

    /// <summary>A sheet that fills the voucher's own fields only on its first line still works.</summary>
    [Fact]
    public async Task ReadAsync_VoucherHeaderFieldsOnTheFirstRowOnly_AreCarriedDown()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Vch No", "Vch Date", "Type", "Ledger", "Dr", "Cr"],
            ["JRN/0001", "2026-04-15", "Journal", "Cash", "1500.00", ""],
            ["JRN/0001", "", "", "Sales", "", "1500.00"]);

        var mapping = Mapping(
            ExcelImportTarget.Vouchers,
            [
                ("Vch No", InteropFieldCatalogue.Number),
                ("Vch Date", InteropFieldCatalogue.Date),
                ("Type", InteropFieldCatalogue.VoucherType),
                ("Ledger", InteropFieldCatalogue.Ledger),
                ("Dr", InteropFieldCatalogue.Debit),
                ("Cr", InteropFieldCatalogue.Credit),
            ]);

        var voucher = (await ReadAsync(reader, mapping))
            .OfType<InteropRecord.Voucher>()
            .ShouldHaveSingleItem()
            .Value;

        voucher.Date.ShouldBe(new DateOnly(2026, 4, 15));
        voucher.VoucherTypeName.ShouldBe("Journal");
        voucher.LedgerEntries.Count.ShouldBe(2);
    }

    /// <summary>Amounts with separators and a currency symbol are read, not refused.</summary>
    [Fact]
    public async Task ReadAsync_AmountWithSeparatorsAndSymbol_IsRead()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Vch No", "Vch Date", "Type", "Ledger", "Dr", "Cr"],
            ["JRN/0001", "15/04/2026", "Journal", "Cash", "₹ 1,23,456.78", ""],
            ["JRN/0001", "15/04/2026", "Journal", "Sales", "", "1,23,456.78"]);

        var mapping = Mapping(
            ExcelImportTarget.Vouchers,
            [
                ("Vch No", InteropFieldCatalogue.Number),
                ("Vch Date", InteropFieldCatalogue.Date),
                ("Type", InteropFieldCatalogue.VoucherType),
                ("Ledger", InteropFieldCatalogue.Ledger),
                ("Dr", InteropFieldCatalogue.Debit),
                ("Cr", InteropFieldCatalogue.Credit),
            ]);

        var voucher = (await ReadAsync(reader, mapping))
            .OfType<InteropRecord.Voucher>()
            .ShouldHaveSingleItem()
            .Value;

        voucher.Date.ShouldBe(new DateOnly(2026, 4, 15));
        voucher.LedgerEntries[0].AmountMinor.ShouldBe(MoneyScale.ToMinor(123_456.78m));
        voucher.IsBalanced.ShouldBeTrue();
    }

    [Fact]
    public async Task ReadAsync_RowWithALedgerButNoAmount_IsReported()
    {
        var reader = FakeWorkbookReader.WithRows(
            ["Vch No", "Vch Date", "Type", "Ledger", "Dr", "Cr"],
            ["JRN/0001", "2026-04-15", "Journal", "Cash", "", ""]);

        var mapping = Mapping(
            ExcelImportTarget.Vouchers,
            [
                ("Vch No", InteropFieldCatalogue.Number),
                ("Vch Date", InteropFieldCatalogue.Date),
                ("Type", InteropFieldCatalogue.VoucherType),
                ("Ledger", InteropFieldCatalogue.Ledger),
                ("Dr", InteropFieldCatalogue.Debit),
                ("Cr", InteropFieldCatalogue.Credit),
            ]);

        (await ReadAsync(reader, mapping))
            .OfType<InteropRecord.Unreadable>()
            .ShouldHaveSingleItem()
            .Reason.ShouldContain("neither a debit nor a credit");
    }

    // ---------------------------------------------------------------- the mapping itself

    [Fact]
    public void SetColumns_TwoColumnsFeedingOneField_IsRefused()
    {
        var mapping = new ExcelColumnMapping(CompanyId, "Bad", ExcelImportTarget.Items);

        Should.Throw<InvalidOperationException>(() => mapping.SetColumns(
        [
            new ExcelColumnMappingColumn(CompanyId, mapping.Id, "Item", InteropFieldCatalogue.Name),
            new ExcelColumnMappingColumn(CompanyId, mapping.Id, "Description", InteropFieldCatalogue.Name),
        ])).Message.ShouldContain(InteropFieldCatalogue.Name);
    }

    [Fact]
    public void SetColumns_OneHeaderMappedTwice_IsRefused()
    {
        var mapping = new ExcelColumnMapping(CompanyId, "Bad", ExcelImportTarget.Items);

        Should.Throw<InvalidOperationException>(() => mapping.SetColumns(
        [
            new ExcelColumnMappingColumn(CompanyId, mapping.Id, "Item", InteropFieldCatalogue.Name),
            new ExcelColumnMappingColumn(CompanyId, mapping.Id, "ITEM", InteropFieldCatalogue.BaseUnit),
        ]));
    }

    [Fact]
    public async Task ReadAsync_MappingNamingAFieldTheTargetDoesNotHave_IsRefused()
    {
        var reader = FakeWorkbookReader.WithRows(["Item"], ["Openwell Pump"]);

        // Saving this through ExcelMappingService would be refused; constructing it directly is how
        // a mapping saved before a field was renamed would look when it is next used.
        var mapping = Mapping(ExcelImportTarget.Items, [("Item", InteropFieldCatalogue.Debit)]);

        (await ReadAsync(reader, mapping))
            .ShouldHaveSingleItem()
            .ShouldBeOfType<InteropRecord.Unreadable>()
            .Reason.ShouldContain(InteropFieldCatalogue.Debit);
    }

    [Fact]
    public void Catalogue_EveryTarget_HasAtLeastOneRequiredField()
    {
        foreach (var target in Enum.GetValues<ExcelImportTarget>())
        {
            InteropFieldCatalogue.RequiredFields(target)
                .ShouldNotBeEmpty($"{target} can be mapped to nothing at all.");
        }
    }
}
