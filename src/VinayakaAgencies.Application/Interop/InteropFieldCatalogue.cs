using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.Application.Interop;

/// <summary>One field of the canonical model that a spreadsheet column can feed.</summary>
public sealed record InteropField(string Name, bool IsRequired, string Description);

/// <summary>
/// Which fields each import target accepts, and which of them a sheet must supply.
/// </summary>
/// <remarks>
/// <para>
/// This is the list the mapping screen shows on one side and the sheet's own headers on the other.
/// It exists as data rather than as a switch buried in the reader because two things need it: the
/// screen, to offer the choices, and the mapping validator, to refuse a saved mapping that names a
/// field its target does not have.
/// </para>
/// <para>
/// Required means "the record cannot be built without it", not "the column must be non-empty on
/// every row" — a blank name on row 40 is a reported row (C8.4), while a sheet with no name column
/// at all is a mapping that was never going to work and is refused before a single row is read.
/// </para>
/// </remarks>
public static class InteropFieldCatalogue
{
    public const string Name = "Name";
    public const string Code = "Code";
    public const string Parent = "Parent";
    public const string Nature = "Nature";
    public const string AffectsGrossProfit = "AffectsGrossProfit";
    public const string Description = "Description";
    public const string Alias = "Alias";
    public const string Gstin = "GSTIN";
    public const string IsActive = "IsActive";
    public const string DecimalPlaces = "DecimalPlaces";
    public const string Address = "Address";
    public const string BaseUnit = "BaseUnit";
    public const string StockGroup = "StockGroup";
    public const string HsnSac = "HSN/SAC";

    public const string VoucherType = "VoucherType";
    public const string Date = "Date";
    public const string Number = "Number";
    public const string Ledger = "Ledger";
    public const string Debit = "Debit";
    public const string Credit = "Credit";
    public const string Narration = "Narration";
    public const string Reference = "Reference";
    public const string ReferenceDate = "ReferenceDate";
    public const string CostCentre = "CostCentre";
    public const string StockItem = "StockItem";
    public const string Quantity = "Quantity";
    public const string Rate = "Rate";
    public const string Godown = "Godown";
    public const string Batch = "Batch";

    private static readonly Dictionary<ExcelImportTarget, IReadOnlyList<InteropField>> Fields = new()
    {
        [ExcelImportTarget.AccountGroups] =
        [
            new(Name, true, "The group's name."),
            new(Parent, false, "The group it sits under. Blank puts it at the top of the tree."),
            new(Nature, false, "Asset, Liability, Income or Expense. Inherited from the parent when blank."),
            new(AffectsGrossProfit, false, "Yes or No."),
            new(IsActive, false, "Yes or No. Blank means active."),
        ],
        [ExcelImportTarget.Ledgers] =
        [
            new(Name, true, "The ledger's name."),
            new(Parent, true, "The account group it belongs to."),
            new(Alias, false, "A short alternative name."),
            new(Description, false, "Free text."),
            new(Gstin, false, "The party's GSTIN, for a customer or supplier ledger."),
            new(IsActive, false, "Yes or No. Blank means active."),
        ],
        [ExcelImportTarget.Units] =
        [
            new(Code, true, "The unit symbol, as printed against a quantity."),
            new(DecimalPlaces, false, "How many decimals a quantity in this unit carries. Blank means none."),
            new(Description, false, "Free text."),
            new(IsActive, false, "Yes or No. Blank means active."),
        ],
        [ExcelImportTarget.StockGroups] =
        [
            new(Name, true, "The stock group's name."),
            new(Description, false, "Free text."),
            new(IsActive, false, "Yes or No. Blank means active."),
        ],
        [ExcelImportTarget.Godowns] =
        [
            new(Name, true, "The godown's name."),
            new(Address, false, "Where it is."),
            new(IsActive, false, "Yes or No. Blank means active."),
        ],
        [ExcelImportTarget.Items] =
        [
            new(Name, true, "The item's name."),
            new(BaseUnit, true, "The unit it is stocked in."),
            new(Code, false, "The item code or part number."),
            new(StockGroup, false, "The stock group it belongs to."),
            new(HsnSac, false, "Its HSN or SAC code."),
            new(IsActive, false, "Yes or No. Blank means active."),
        ],
        [ExcelImportTarget.Vouchers] =
        [
            new(Number, true, "The voucher number. Rows sharing one must be consecutive."),
            new(Date, true, "The voucher date."),
            new(VoucherType, true, "Payment, Receipt, Sales, or any voucher type this company has."),
            new(Ledger, false, "The ledger this row posts to."),
            new(Debit, false, "Amount debited by this row."),
            new(Credit, false, "Amount credited by this row."),
            new(Narration, false, "The voucher's narration."),
            new(Reference, false, "The party's document number."),
            new(ReferenceDate, false, "The party's document date."),
            new(CostCentre, false, "The cost centre this row is charged to."),
            new(StockItem, false, "An item this voucher moves."),
            new(Quantity, false, "How much of it. Negative for stock going out."),
            new(Rate, false, "Cost or price per unit."),
            new(Godown, false, "Which godown it moves in or out of."),
            new(Batch, false, "The batch number, for a batch-tracked item."),
        ],
    };

    /// <summary>The fields this target accepts.</summary>
    public static IReadOnlyList<InteropField> For(ExcelImportTarget target) =>
        Fields.TryGetValue(target, out var fields)
            ? fields
            : throw new ArgumentOutOfRangeException(nameof(target), target, "No field list for this target.");

    /// <summary>Whether a saved mapping's field name is one this target has.</summary>
    public static bool Knows(ExcelImportTarget target, string fieldName) =>
        For(target).Any(field => string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> RequiredFields(ExcelImportTarget target) =>
        [.. For(target).Where(field => field.IsRequired).Select(field => field.Name)];
}
