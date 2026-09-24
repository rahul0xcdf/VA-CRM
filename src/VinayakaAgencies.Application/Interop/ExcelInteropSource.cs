using System.Globalization;
using System.Runtime.CompilerServices;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Reads the owner's own spreadsheet through a saved column mapping (§2, C8.3).
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IInteropSource"/> like the Tally readers, producing the same records, so everything
/// downstream — validation, the per-row report, the commit — is the same code (§4a D3). A column
/// order nobody anticipated is handled here and nowhere else, which is the whole point of C8.3:
/// the mapping step absorbs the layout, and the rest of the import never learns there was one.
/// </para>
/// <para>
/// Columns are matched by header text, not by position. A supplier who inserts a column in the
/// middle of their price list would otherwise shift every field one to the right, silently, and the
/// import would succeed with every value in the wrong field.
/// </para>
/// </remarks>
public sealed class ExcelInteropSource : IInteropSource
{
    private readonly IWorkbookReader _reader;
    private readonly string _path;
    private readonly ExcelColumnMapping _mapping;

    public ExcelInteropSource(IWorkbookReader reader, string path, ExcelColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(mapping);

        _reader = reader;
        _path = path;
        _mapping = mapping;
        Description = Path.GetFileName(path);
    }

    public string Description { get; }

    public string FormatName => "Excel";

    public async IAsyncEnumerable<InteropRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var columns = ResolveMappedColumns();

        if (columns.Problem is { } mappingProblem)
        {
            yield return Unreadable(1, mappingProblem);
            yield break;
        }

        Dictionary<string, int>? fieldToIndex = null;
        var ordinal = 0;
        VoucherBuilder? openVoucher = null;

        await foreach (var row in _reader.ReadRowsAsync(_path, _mapping.SheetName, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (row.RowNumber < _mapping.HeaderRowNumber)
            {
                continue;
            }

            if (row.RowNumber == _mapping.HeaderRowNumber)
            {
                var (resolved, missing) = MatchHeaders(row, columns.ByHeader);

                if (missing.Count > 0)
                {
                    yield return Unreadable(
                        row.RowNumber,
                        $"the sheet has no column headed {Join(missing)}. The mapping "
                        + $"'{_mapping.Name}' expects it — either the wrong sheet is selected, or the "
                        + "header row is not row "
                        + _mapping.HeaderRowNumber.ToString(CultureInfo.InvariantCulture) + ".");

                    yield break;
                }

                fieldToIndex = resolved;
                continue;
            }

            if (fieldToIndex is null || row.IsBlank)
            {
                continue;
            }

            ordinal++;
            var location = InteropSourceLocation.AtRow(ordinal, SheetLabel, row.RowNumber);
            var cells = new RowValues(row, fieldToIndex);

            if (_mapping.Target != ExcelImportTarget.Vouchers)
            {
                yield return ReadMaster(cells, location);
                continue;
            }

            // A voucher spans as many rows as it has lines. The open one is emitted when a row with a
            // different number arrives, which is why rows of one voucher have to be consecutive — the
            // alternative is holding the sheet, and §4a D2 rules that out.
            var number = cells.Text(InteropFieldCatalogue.Number);

            if (openVoucher is not null && !openVoucher.Accepts(number))
            {
                yield return openVoucher.Build();
                openVoucher = null;
            }

            openVoucher ??= new VoucherBuilder(number ?? string.Empty, location);

            if (openVoucher.Add(cells, location) is { } rowProblem)
            {
                yield return Unreadable(row.RowNumber, rowProblem);
                openVoucher = null;
            }
        }

        if (openVoucher is not null)
        {
            yield return openVoucher.Build();
        }
    }

    private string SheetLabel => _mapping.SheetName ?? "Sheet";

    private InteropRecord.Unreadable Unreadable(int rowNumber, string reason) =>
        new(
            InteropSourceLocation.AtRow(1, SheetLabel, rowNumber),
            reason,
            FormatName);

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 1
            ? $"'{names[0]}'"
            : string.Join(", ", names.Take(names.Count - 1).Select(n => $"'{n}'")) + $" or '{names[^1]}'";

    /// <summary>The mapping's columns, keyed by normalised header, with any configuration fault.</summary>
    private (Dictionary<string, MappedColumn> ByHeader, string? Problem) ResolveMappedColumns()
    {
        if (_mapping.Columns.Count == 0)
        {
            return ([], $"the mapping '{_mapping.Name}' has no columns configured.");
        }

        var unknown = _mapping.Columns
            .Where(column => !InteropFieldCatalogue.Knows(_mapping.Target, column.TargetField))
            .Select(column => column.TargetField)
            .ToList();

        if (unknown.Count > 0)
        {
            return ([], $"the mapping '{_mapping.Name}' names {Join(unknown)}, which is not a field "
                + $"{_mapping.Target} has. The mapping needs correcting before it can be used.");
        }

        var mapped = _mapping.Columns
            .Select(column => column.TargetField)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingRequired = InteropFieldCatalogue
            .RequiredFields(_mapping.Target)
            .Where(field => !mapped.Contains(field))
            .ToList();

        if (missingRequired.Count > 0)
        {
            return ([], $"the mapping '{_mapping.Name}' leaves {Join(missingRequired)} unmapped, and "
                + $"a {_mapping.Target} record cannot be built without it.");
        }

        return (
            _mapping.Columns.ToDictionary(
                column => column.SourceHeaderNormalised,
                column => new MappedColumn(column.SourceHeader, column.TargetField),
                StringComparer.Ordinal),
            null);
    }

    /// <summary>Turns the sheet's own header row into a field-to-column-index lookup.</summary>
    private static (Dictionary<string, int> Resolved, IReadOnlyList<string> Missing) MatchHeaders(
        WorkbookRow headerRow,
        Dictionary<string, MappedColumn> mappedByHeader)
    {
        var resolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < headerRow.Cells.Count; index++)
        {
            var header = headerRow.Cell(index);

            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var key = NameNormaliser.Normalise(header);

            if (mappedByHeader.TryGetValue(key, out var column))
            {
                resolved[column.Field] = index;
                found.Add(key);
            }
        }

        // Named by the header the owner configured, not by the field behind it. They are looking at
        // their own spreadsheet, where a column called "BaseUnit" does not exist and one called
        // "Unit" is the thing that has been renamed or moved.
        var missing = mappedByHeader
            .Where(pair => !found.Contains(pair.Key))
            .Select(pair => pair.Value.Header)
            .ToList();

        return (resolved, missing);
    }

    private InteropRecord ReadMaster(RowValues cells, InteropSourceLocation location) => _mapping.Target switch
    {
        ExcelImportTarget.Units => ReadUnit(cells, location),
        ExcelImportTarget.StockGroups => ReadStockGroup(cells, location),
        ExcelImportTarget.Godowns => ReadGodown(cells, location),
        ExcelImportTarget.Items => ReadItem(cells, location),
        ExcelImportTarget.AccountGroups => ReadAccountGroup(cells, location),
        ExcelImportTarget.Ledgers => ReadLedger(cells, location),
        _ => new InteropRecord.Unreadable(location, $"{_mapping.Target} cannot be read from a sheet.", FormatName),
    };

    private InteropRecord ReadUnit(RowValues cells, InteropSourceLocation location)
    {
        var code = cells.Text(InteropFieldCatalogue.Code);

        if (string.IsNullOrWhiteSpace(code))
        {
            return Missing(location, InteropFieldCatalogue.Code);
        }

        var places = cells.Integer(InteropFieldCatalogue.DecimalPlaces);

        if (places.Failed)
        {
            return Invalid(location, InteropFieldCatalogue.DecimalPlaces, places.Raw);
        }

        return new InteropRecord.Unit(
            location,
            new InteropUnit(
                code,
                places.Value ?? 0,
                Id: null,
                cells.Text(InteropFieldCatalogue.Description),
                cells.Flag(InteropFieldCatalogue.IsActive)));
    }

    private InteropRecord ReadStockGroup(RowValues cells, InteropSourceLocation location)
    {
        var name = cells.Text(InteropFieldCatalogue.Name);

        return string.IsNullOrWhiteSpace(name)
            ? Missing(location, InteropFieldCatalogue.Name)
            : new InteropRecord.StockGroup(
                location,
                new InteropStockGroup(
                    name,
                    Id: null,
                    cells.Text(InteropFieldCatalogue.Description),
                    cells.Flag(InteropFieldCatalogue.IsActive)));
    }

    private InteropRecord ReadGodown(RowValues cells, InteropSourceLocation location)
    {
        var name = cells.Text(InteropFieldCatalogue.Name);

        return string.IsNullOrWhiteSpace(name)
            ? Missing(location, InteropFieldCatalogue.Name)
            : new InteropRecord.Godown(
                location,
                new InteropGodown(
                    name,
                    Id: null,
                    cells.Text(InteropFieldCatalogue.Address),
                    cells.Flag(InteropFieldCatalogue.IsActive)));
    }

    private InteropRecord ReadItem(RowValues cells, InteropSourceLocation location)
    {
        var name = cells.Text(InteropFieldCatalogue.Name);
        var baseUnit = cells.Text(InteropFieldCatalogue.BaseUnit);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Missing(location, InteropFieldCatalogue.Name);
        }

        return string.IsNullOrWhiteSpace(baseUnit)
            ? Missing(location, InteropFieldCatalogue.BaseUnit)
            : new InteropRecord.Item(
                location,
                new InteropItem(
                    name,
                    baseUnit,
                    Id: null,
                    cells.Text(InteropFieldCatalogue.Code),
                    cells.Text(InteropFieldCatalogue.StockGroup),
                    cells.Text(InteropFieldCatalogue.HsnSac),
                    cells.Flag(InteropFieldCatalogue.IsActive)));
    }

    private InteropRecord ReadAccountGroup(RowValues cells, InteropSourceLocation location)
    {
        var name = cells.Text(InteropFieldCatalogue.Name);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Missing(location, InteropFieldCatalogue.Name);
        }

        var parent = cells.Text(InteropFieldCatalogue.Parent);
        var natureText = cells.Text(InteropFieldCatalogue.Nature);
        var hasNature = Enum.TryParse<AccountNature>(natureText, ignoreCase: true, out var nature);

        if (!hasNature && !string.IsNullOrWhiteSpace(natureText))
        {
            return Invalid(location, InteropFieldCatalogue.Nature, natureText);
        }

        if (!hasNature && string.IsNullOrWhiteSpace(parent))
        {
            return new InteropRecord.Unreadable(
                location,
                $"group '{name}' gives neither a parent nor a nature, so it cannot be placed under "
                + "assets, liabilities, income or expenses.",
                FormatName);
        }

        return new InteropRecord.AccountGroup(
            location,
            new InteropAccountGroup(
                name,
                parent,
                nature,
                Id: null,
                cells.Flag(InteropFieldCatalogue.AffectsGrossProfit, whenBlank: false),
                cells.Flag(InteropFieldCatalogue.IsActive)));
    }

    private InteropRecord ReadLedger(RowValues cells, InteropSourceLocation location)
    {
        var name = cells.Text(InteropFieldCatalogue.Name);
        var parent = cells.Text(InteropFieldCatalogue.Parent);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Missing(location, InteropFieldCatalogue.Name);
        }

        return string.IsNullOrWhiteSpace(parent)
            ? Missing(location, InteropFieldCatalogue.Parent)
            : new InteropRecord.Ledger(
                location,
                new InteropLedger(
                    name,
                    parent,
                    Id: null,
                    cells.Text(InteropFieldCatalogue.Alias),
                    cells.Text(InteropFieldCatalogue.Description),
                    cells.Text(InteropFieldCatalogue.Gstin),
                    cells.Flag(InteropFieldCatalogue.IsActive)));
    }

    private InteropRecord.Unreadable Missing(InteropSourceLocation location, string field) =>
        new(location, $"the '{field}' column is empty on this row, and the record needs it.", FormatName);

    private InteropRecord.Unreadable Invalid(InteropSourceLocation location, string field, string? raw) =>
        new(location, $"'{raw}' is not a value the '{field}' column can take.", FormatName);

    /// <summary>A mapped column: what the sheet calls it, and what it feeds.</summary>
    private readonly record struct MappedColumn(string Header, string Field);

    /// <summary>One row's cells, addressed by field name rather than by column.</summary>
    private sealed class RowValues
    {
        private readonly WorkbookRow _row;
        private readonly Dictionary<string, int> _fieldToIndex;

        public RowValues(WorkbookRow row, Dictionary<string, int> fieldToIndex)
        {
            _row = row;
            _fieldToIndex = fieldToIndex;
        }

        public string? Text(string field)
        {
            if (!_fieldToIndex.TryGetValue(field, out var index))
            {
                return null;
            }

            var value = _row.Cell(index);

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Reads a yes/no column, defaulting when the column is absent or blank.</summary>
        public bool Flag(string field, bool whenBlank = true)
        {
            var text = Text(field);

            if (string.IsNullOrWhiteSpace(text)) return whenBlank;

            return text.Trim().ToUpperInvariant() is "Y" or "YES" or "TRUE" or "1" or "ACTIVE";
        }

        public ParsedInt Integer(string field)
        {
            var text = Text(field);

            if (string.IsNullOrWhiteSpace(text))
            {
                return new ParsedInt(null, false, null);
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? new ParsedInt(parsed, false, text)
                : new ParsedInt(null, true, text);
        }

        /// <summary>Reads a money or quantity column into minor units (ADR-0003).</summary>
        public ParsedAmount Amount(string field)
        {
            var text = Text(field);

            if (string.IsNullOrWhiteSpace(text))
            {
                return new ParsedAmount(null, false, null);
            }

            // Thousands separators and a stray currency symbol are what a real sheet contains; they
            // are not a reason to refuse a row the owner can read perfectly well.
            var cleaned = new string([.. text.Where(c => char.IsDigit(c) || c is '.' or '-' or '+')]);

            return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? new ParsedAmount(MoneyScale.ToMinor(MoneyScale.Round(parsed)), false, text)
                : new ParsedAmount(null, true, text);
        }

        public ParsedDate Date(string field)
        {
            var text = Text(field);

            if (string.IsNullOrWhiteSpace(text))
            {
                return new ParsedDate(null, false, null);
            }

            // The adapter normalises a real date cell to yyyy-MM-dd. The others are what a column
            // typed as text contains, which is how dates arrive from most exports.
            string[] formats = ["yyyy-MM-dd", "yyyyMMdd", "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MMM-yyyy"];

            foreach (var format in formats)
            {
                if (DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    return new ParsedDate(parsed, false, text);
                }
            }

            return new ParsedDate(null, true, text);
        }
    }

    private readonly record struct ParsedInt(int? Value, bool Failed, string? Raw);

    private readonly record struct ParsedAmount(long? Value, bool Failed, string? Raw);

    private readonly record struct ParsedDate(DateOnly? Value, bool Failed, string? Raw);

    /// <summary>Accumulates the consecutive rows that make up one voucher.</summary>
    private sealed class VoucherBuilder
    {
        private readonly List<InteropLedgerEntry> _ledgerEntries = [];
        private readonly List<InteropInventoryEntry> _inventoryEntries = [];
        private readonly string _number;
        private readonly InteropSourceLocation _location;

        private string _voucherTypeName = string.Empty;
        private DateOnly _date;
        private string? _narration;
        private string? _reference;
        private DateOnly? _referenceDate;

        public VoucherBuilder(string number, InteropSourceLocation location)
        {
            _number = number;
            _location = location;
        }

        public bool Accepts(string? number) =>
            string.Equals(NameNormaliser.Normalise(number ?? string.Empty), NameNormaliser.Normalise(_number), StringComparison.Ordinal);

        /// <summary>Folds one row in, or returns why it could not be.</summary>
        public string? Add(RowValues cells, InteropSourceLocation location)
        {
            var date = cells.Date(InteropFieldCatalogue.Date);

            if (date.Failed)
            {
                return $"'{date.Raw}' is not a date.";
            }

            // The header fields are taken from the first row that carries them, so a sheet that
            // repeats them on every line and one that fills them only on the first both work.
            if (date.Value is { } parsedDate && _date == default)
            {
                _date = parsedDate;
            }

            if (cells.Text(InteropFieldCatalogue.VoucherType) is { } type && _voucherTypeName.Length == 0)
            {
                _voucherTypeName = type;
            }

            _narration ??= cells.Text(InteropFieldCatalogue.Narration);
            _reference ??= cells.Text(InteropFieldCatalogue.Reference);

            var referenceDate = cells.Date(InteropFieldCatalogue.ReferenceDate);

            if (referenceDate.Failed)
            {
                return $"'{referenceDate.Raw}' is not a date.";
            }

            _referenceDate ??= referenceDate.Value;

            var ledger = cells.Text(InteropFieldCatalogue.Ledger);

            if (!string.IsNullOrWhiteSpace(ledger))
            {
                var debit = cells.Amount(InteropFieldCatalogue.Debit);
                var credit = cells.Amount(InteropFieldCatalogue.Credit);

                if (debit.Failed)
                {
                    return $"'{debit.Raw}' is not an amount.";
                }

                if (credit.Failed)
                {
                    return $"'{credit.Raw}' is not an amount.";
                }

                var amountMinor = (debit.Value ?? 0L) - (credit.Value ?? 0L);

                if (amountMinor == 0L)
                {
                    return $"the row posts to '{ledger}' with neither a debit nor a credit.";
                }

                _ledgerEntries.Add(new InteropLedgerEntry(
                    ledger,
                    amountMinor,
                    cells.Text(InteropFieldCatalogue.CostCentre)));
            }

            var item = cells.Text(InteropFieldCatalogue.StockItem);

            if (!string.IsNullOrWhiteSpace(item))
            {
                var quantity = cells.Amount(InteropFieldCatalogue.Quantity);
                var rate = cells.Amount(InteropFieldCatalogue.Rate);

                if (quantity.Failed)
                {
                    return $"'{quantity.Raw}' is not a quantity.";
                }

                if (rate.Failed)
                {
                    return $"'{rate.Raw}' is not a rate.";
                }

                if (quantity.Value is not { } quantityMinor || quantityMinor == 0L)
                {
                    return $"the row moves '{item}' with no quantity.";
                }

                _inventoryEntries.Add(new InteropInventoryEntry(
                    item,
                    quantityMinor,
                    Domain.Inventory.StockLedgerEntry.ComputeValueMinor(
                        MoneyScale.FromMinor(quantityMinor),
                        rate.Value ?? 0L),
                    cells.Text(InteropFieldCatalogue.Godown),
                    UnitCode: null,
                    rate.Value,
                    cells.Text(InteropFieldCatalogue.Batch)));
            }

            _ = location;
            return null;
        }

        public InteropRecord.Voucher Build() => new(
            _location,
            new InteropVoucher
            {
                VoucherTypeName = _voucherTypeName,
                Kind = TallyConventions.ParseVoucherKind(_voucherTypeName),
                Date = _date,
                Number = _number,
                Narration = _narration,
                ReferenceNumber = _reference,
                ReferenceDate = _referenceDate,
                LedgerEntries = _ledgerEntries,
                InventoryEntries = _inventoryEntries,
            });
    }
}
