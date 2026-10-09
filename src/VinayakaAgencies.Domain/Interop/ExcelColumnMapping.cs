using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Interop;

/// <summary>
/// A saved, reusable answer to "which column in my sheet is which field".
/// </summary>
/// <remarks>
/// <para>
/// §2: the import follows Tally's own pattern, a column-mapping step, so users bring their own sheet
/// layout instead of being forced into a fixed template — and the mappings are saveable and
/// reusable. Saving them is what makes the second month's import a two-key operation rather than a
/// repeat of the first month's puzzle, and §4a D2's realistic migration runs the same file several
/// times over.
/// </para>
/// <para>
/// A mapping matches the owner's column <i>headers</i>, not column positions. A supplier who adds a
/// column in the middle of their price list should not silently shift every field one to the right,
/// which is exactly what position matching would do and would do without any error.
/// </para>
/// </remarks>
public sealed class ExcelColumnMapping : Entity, IAuditStamped
{
    private readonly List<ExcelColumnMappingColumn> _columns = [];

    private ExcelColumnMapping()
    {
        // EF Core materialisation.
    }

    public ExcelColumnMapping(
        Guid companyId,
        string name,
        ExcelImportTarget target,
        string? sheetName = null,
        int headerRowNumber = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (headerRowNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(headerRowNumber), headerRowNumber, "Spreadsheet rows are numbered from 1.");
        }

        CompanyId = companyId;
        SetName(name);
        Target = target;
        SheetName = Trimmed(sheetName);
        HeaderRowNumber = headerRowNumber;
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Comparison form of <see cref="Name"/>, which the unique index is built over.</summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public ExcelImportTarget Target { get; private set; }

    /// <summary>The sheet to read. Null means the first one in the workbook.</summary>
    public string? SheetName { get; private set; }

    /// <summary>
    /// The row the headers sit on, 1-based.
    /// </summary>
    /// <remarks>
    /// Configurable because real sheets rarely start at row 1 — a title, a date range and a blank
    /// line above the headers is the normal shape of a report someone exported and forwarded on.
    /// </remarks>
    public int HeaderRowNumber { get; private set; }

    public bool IsActive { get; private set; } = true;

    public IReadOnlyList<ExcelColumnMappingColumn> Columns => _columns;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void Rename(string name) => SetName(name);

    public void SetSheet(string? sheetName) => SheetName = Trimmed(sheetName);

    public void SetHeaderRow(int headerRowNumber)
    {
        if (headerRowNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(headerRowNumber), headerRowNumber, "Spreadsheet rows are numbered from 1.");
        }

        HeaderRowNumber = headerRowNumber;
    }

    /// <summary>
    /// Replaces the column list wholesale.
    /// </summary>
    /// <remarks>
    /// Wholesale rather than add/remove because a mapping is edited as a grid: the screen shows every
    /// column of the sheet against the field it feeds, and the owner presses save once. Reconciling
    /// that into individual operations would invent an ordering the user never expressed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Two columns feed the same field. A field can only take one column's value, and silently
    /// keeping the last would make the import depend on the order the grid happened to be in.
    /// </exception>
    public void SetColumns(IEnumerable<ExcelColumnMappingColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var replacement = columns.ToList();
        var duplicateField = replacement
            .GroupBy(column => column.TargetField, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Skip(1).Any());

        if (duplicateField is not null)
        {
            throw new InvalidOperationException(
                $"Two columns are mapped to '{duplicateField.Key}'. A field takes one column.");
        }

        var duplicateHeader = replacement
            .GroupBy(column => NameNormaliser.Normalise(column.SourceHeader), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Skip(1).Any());

        if (duplicateHeader is not null)
        {
            throw new InvalidOperationException(
                $"Column '{replacement.First(c => NameNormaliser.Normalise(c.SourceHeader) == duplicateHeader.Key).SourceHeader}' "
                + "is mapped twice.");
        }

        _columns.Clear();
        _columns.AddRange(replacement);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private void SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(Name);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
