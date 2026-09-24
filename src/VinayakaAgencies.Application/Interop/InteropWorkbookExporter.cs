using System.Globalization;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Interop;

/// <summary>Writes the canonical document as a workbook, one sheet per record kind.</summary>
public interface IInteropWorkbookExporter
{
    Task<InteropExportSummary> WriteAsync(
        InteropDocument document,
        Stream destination,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The Excel side of the export (§2).
/// </summary>
/// <remarks>
/// <para>
/// The sheets are laid out with the same headers <see cref="InteropFieldCatalogue"/> names on the
/// import side, and that is not a coincidence — an exported sheet is a file the owner can edit and
/// hand straight back, and it maps onto itself without anyone building a mapping first. It is also
/// the fastest way to produce a template for a supplier who asks what layout to send.
/// </para>
/// <para>
/// Building the sheets is separated from writing them because the layout is what is worth testing
/// and a workbook is not. <see cref="BuildSheets"/> is a pure function over the document.
/// </para>
/// </remarks>
public sealed class InteropWorkbookExporter : IInteropWorkbookExporter
{
    private readonly IWorkbookWriter _writer;

    public InteropWorkbookExporter(IWorkbookWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    public async Task<InteropExportSummary> WriteAsync(
        InteropDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        await _writer
            .WriteAsync(new WorkbookDocument(BuildSheets(document)), destination, cancellationToken)
            .ConfigureAwait(false);

        return InteropExportSummary.For(document, InteropFormat.Excel);
    }

    /// <summary>The sheets the document becomes. Empty kinds get no sheet.</summary>
    public static IReadOnlyList<WorkbookSheet> BuildSheets(InteropDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sheets = new List<WorkbookSheet>(8);

        Add(sheets, "Units", [F.Code, F.DecimalPlaces, F.Description, F.IsActive], document.Units,
            unit =>
            [
                unit.Code,
                unit.DecimalPlaces.ToString(CultureInfo.InvariantCulture),
                unit.Description,
                Flag(unit.IsActive),
            ]);

        Add(sheets, "Stock groups", [F.Name, F.Description, F.IsActive], document.StockGroups,
            group => [group.Name, group.Description, Flag(group.IsActive)]);

        Add(sheets, "Godowns", [F.Name, F.Address, F.IsActive], document.Godowns,
            godown => [godown.Name, godown.Address, Flag(godown.IsActive)]);

        Add(sheets, "Items", [F.Name, F.BaseUnit, F.Code, F.StockGroup, F.HsnSac, F.IsActive], document.Items,
            item =>
            [
                item.Name,
                item.BaseUnitCode,
                item.Code,
                item.StockGroupName,
                item.HsnSacCode,
                Flag(item.IsActive),
            ]);

        Add(sheets, "Account groups", [F.Name, F.Parent, F.Nature, F.AffectsGrossProfit, F.IsActive], document.AccountGroups,
            group =>
            [
                group.Name,
                group.ParentName,
                group.Nature.ToString(),
                Flag(group.AffectsGrossProfit),
                Flag(group.IsActive),
            ]);

        Add(sheets, "Ledgers", [F.Name, F.Parent, F.Alias, F.Description, F.Gstin, F.IsActive], document.Ledgers,
            ledger =>
            [
                ledger.Name,
                ledger.ParentGroupName,
                ledger.Alias,
                ledger.Description,
                ledger.Gstin,
                Flag(ledger.IsActive),
            ]);

        Add(sheets, "Voucher types", [F.Name, F.Parent, "Prefix", F.IsActive], document.VoucherTypes,
            type =>
            [
                type.Name,
                TallyConventions.VoucherTypeName(type.Kind),
                type.NumberPrefix,
                Flag(type.IsActive),
            ]);

        if (document.Vouchers.Count > 0)
        {
            sheets.Add(new WorkbookSheet("Vouchers", VoucherHeaders, [.. VoucherRows(document)]));
        }

        return sheets;
    }

    private static readonly string[] VoucherHeaders =
    [
        F.Number, F.Date, F.VoucherType, F.Ledger, F.Debit, F.Credit, F.CostCentre,
        F.StockItem, F.Quantity, F.Rate, F.Godown, F.Batch, F.Narration, F.Reference, F.ReferenceDate,
    ];

    /// <summary>
    /// One row per line, with the voucher's own fields repeated down its rows.
    /// </summary>
    /// <remarks>
    /// Repeated rather than left blank after the first line, because the sheet is meant to be sorted,
    /// filtered and pivoted by whoever receives it, and any of those operations on a sheet that
    /// carries the number only once destroys the grouping. The importer accepts either shape.
    /// </remarks>
    private static IEnumerable<IReadOnlyList<string?>> VoucherRows(InteropDocument document)
    {
        foreach (var voucher in document.Vouchers)
        {
            var lineCount = Math.Max(1, Math.Max(voucher.LedgerEntries.Count, voucher.InventoryEntries.Count));

            for (var line = 0; line < lineCount; line++)
            {
                var ledger = line < voucher.LedgerEntries.Count ? voucher.LedgerEntries[line] : null;
                var stock = line < voucher.InventoryEntries.Count ? voucher.InventoryEntries[line] : null;

                yield return
                [
                    voucher.Number,
                    voucher.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    voucher.VoucherTypeName,
                    ledger?.LedgerName,
                    ledger is { AmountMinor: > 0 } ? Money(ledger.AmountMinor) : null,
                    ledger is { AmountMinor: < 0 } ? Money(-ledger.AmountMinor) : null,
                    ledger?.CostCentreName,
                    stock?.StockItemName,
                    stock is null ? null : Money(stock.QuantityMinor),
                    stock?.RateMinor is { } rate ? Money(rate) : null,
                    stock?.GodownName,
                    stock?.BatchName,
                    voucher.Narration,
                    voucher.ReferenceNumber,
                    voucher.ReferenceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ];
            }
        }
    }

    private static void Add<T>(
        List<WorkbookSheet> sheets,
        string name,
        string[] headers,
        IReadOnlyList<T> rows,
        Func<T, IReadOnlyList<string?>> project)
    {
        if (rows.Count > 0)
        {
            sheets.Add(new WorkbookSheet(name, headers, [.. rows.Select(project)]));
        }
    }

    private static string Flag(bool value) => value ? "Yes" : "No";

    private static string Money(long minor) => MoneyScale.ToInvariantString(MoneyScale.FromMinor(minor));

    /// <summary>Shorthand for the field names, so the layouts below stay readable.</summary>
    private static class F
    {
        internal const string Name = InteropFieldCatalogue.Name;
        internal const string Code = InteropFieldCatalogue.Code;
        internal const string Parent = InteropFieldCatalogue.Parent;
        internal const string Nature = InteropFieldCatalogue.Nature;
        internal const string AffectsGrossProfit = InteropFieldCatalogue.AffectsGrossProfit;
        internal const string Description = InteropFieldCatalogue.Description;
        internal const string Alias = InteropFieldCatalogue.Alias;
        internal const string Gstin = InteropFieldCatalogue.Gstin;
        internal const string IsActive = InteropFieldCatalogue.IsActive;
        internal const string DecimalPlaces = InteropFieldCatalogue.DecimalPlaces;
        internal const string Address = InteropFieldCatalogue.Address;
        internal const string BaseUnit = InteropFieldCatalogue.BaseUnit;
        internal const string StockGroup = InteropFieldCatalogue.StockGroup;
        internal const string HsnSac = InteropFieldCatalogue.HsnSac;
        internal const string VoucherType = InteropFieldCatalogue.VoucherType;
        internal const string Date = InteropFieldCatalogue.Date;
        internal const string Number = InteropFieldCatalogue.Number;
        internal const string Ledger = InteropFieldCatalogue.Ledger;
        internal const string Debit = InteropFieldCatalogue.Debit;
        internal const string Credit = InteropFieldCatalogue.Credit;
        internal const string Narration = InteropFieldCatalogue.Narration;
        internal const string Reference = InteropFieldCatalogue.Reference;
        internal const string ReferenceDate = InteropFieldCatalogue.ReferenceDate;
        internal const string CostCentre = InteropFieldCatalogue.CostCentre;
        internal const string StockItem = InteropFieldCatalogue.StockItem;
        internal const string Quantity = InteropFieldCatalogue.Quantity;
        internal const string Rate = InteropFieldCatalogue.Rate;
        internal const string Godown = InteropFieldCatalogue.Godown;
        internal const string Batch = InteropFieldCatalogue.Batch;
    }
}
