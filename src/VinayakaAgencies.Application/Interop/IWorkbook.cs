namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Reads a spreadsheet as rows of plain text.
/// </summary>
/// <remarks>
/// <para>
/// A port, satisfied by a ClosedXML adapter in Infrastructure — the same arrangement as
/// <c>IIrpClient</c> in Phase 6, and for the same two reasons. It keeps a file-format library out of
/// a layer whose job is to declare what it needs; and it lets the column-mapping and validation
/// rules, which is where every decision worth testing lives, be exercised against an in-memory grid
/// rather than a real workbook. C8.3 and C8.4 are both about those rules.
/// </para>
/// <para>
/// Cells arrive as strings, already normalised by the adapter: a date cell as <c>yyyy-MM-dd</c>, a
/// numeric cell in invariant form. Excel's typing is the adapter's problem precisely because it is
/// the part that differs between one spreadsheet library and the next, and a date that read as
/// "45231" on one machine and "15/04/2026" on another would be a defect nothing above this line
/// could see.
/// </para>
/// </remarks>
public interface IWorkbookReader
{
    /// <summary>The sheets in the file, in workbook order.</summary>
    Task<IReadOnlyList<string>> ListSheetsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Reads a sheet's used rows, skipping none — the header is row 1 like any other.</summary>
    IAsyncEnumerable<WorkbookRow> ReadRowsAsync(
        string path,
        string? sheetName = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Writes a workbook.</summary>
public interface IWorkbookWriter
{
    Task WriteAsync(WorkbookDocument document, Stream destination, CancellationToken cancellationToken = default);
}

/// <summary>One row of a sheet, with its 1-based number so an error can name it.</summary>
public sealed record WorkbookRow(int RowNumber, IReadOnlyList<string?> Cells)
{
    public string? Cell(int index) =>
        index >= 0 && index < Cells.Count ? Cells[index] : null;

    /// <summary>True when every cell is blank — a spacer row, not a record.</summary>
    public bool IsBlank => Cells.All(string.IsNullOrWhiteSpace);
}

/// <summary>A workbook about to be written.</summary>
public sealed record WorkbookDocument(IReadOnlyList<WorkbookSheet> Sheets);

/// <summary>One sheet: a header row, then the data.</summary>
public sealed record WorkbookSheet(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string?>> Rows);
