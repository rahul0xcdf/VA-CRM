using System.Globalization;
using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using VinayakaAgencies.Application.Interop;

namespace VinayakaAgencies.Infrastructure.Interop;

/// <summary>
/// The ClosedXML adapter behind <see cref="IWorkbookReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every cell is handed up as a string, already normalised: a real date cell as <c>yyyy-MM-dd</c>, a
/// numeric cell in invariant form with no thousands separators, everything else as its text. That
/// normalisation is the reason this adapter exists rather than the Application layer holding a
/// workbook directly. Excel stores a date as a serial number and a currency as a formatted double,
/// and a reader that passed either through raw would produce "45231" or "₹1,234.00" depending on the
/// machine's locale — a difference nothing above this line could see and every import would carry.
/// </para>
/// <para>
/// ClosedXML loads the whole workbook into memory; it has no streaming reader. That is acceptable
/// here and worth stating: §4a D2's volume concern is about the Tally history, which arrives as XML
/// and is genuinely streamed. A spreadsheet is bounded by what a person can maintain in one.
/// </para>
/// </remarks>
internal sealed class ClosedXmlWorkbookReader : IWorkbookReader
{
    public Task<IReadOnlyList<string>> ListSheetsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        using var workbook = new XLWorkbook(path);

        return Task.FromResult<IReadOnlyList<string>>(
            [.. workbook.Worksheets.Select(sheet => sheet.Name)]);
    }

    public async IAsyncEnumerable<WorkbookRow> ReadRowsAsync(
        string path,
        string? sheetName = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var workbook = new XLWorkbook(path);

        var worksheet = string.IsNullOrWhiteSpace(sheetName)
            ? workbook.Worksheets.First()
            : workbook.Worksheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"'{Path.GetFileName(path)}' has no sheet called '{sheetName}'. It has: "
                    + string.Join(", ", workbook.Worksheets.Select(sheet => sheet.Name)) + ".");

        var used = worksheet.RangeUsed();

        if (used is null)
        {
            yield break;
        }

        var lastColumn = used.LastColumn().ColumnNumber();

        foreach (var row in used.RowsUsed())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cells = new List<string?>(lastColumn);

            for (var column = 1; column <= lastColumn; column++)
            {
                cells.Add(TextOf(row.Worksheet.Cell(row.RowNumber(), column)));
            }

            yield return new WorkbookRow(row.RowNumber(), cells);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>One cell as invariant text, whatever Excel is storing underneath.</summary>
    private static string? TextOf(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        return cell.Value.Type switch
        {
            XLDataType.DateTime => cell.Value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => NumberText(cell),
            XLDataType.Boolean => cell.Value.GetBoolean() ? "Yes" : "No",
            _ => cell.GetString().Trim(),
        };
    }

    /// <summary>
    /// A numeric cell in invariant form.
    /// </summary>
    /// <remarks>
    /// A date formatted as a date is reported by ClosedXML as a number carrying a date format, so the
    /// format is checked before the value is treated as a quantity. Missing that turns every date
    /// column in the sheet into a five-digit serial number.
    /// </remarks>
    private static string NumberText(IXLCell cell)
    {
        if (cell.Style.NumberFormat.Format.Contains('y', StringComparison.OrdinalIgnoreCase)
            && cell.TryGetValue<DateTime>(out var asDate))
        {
            return asDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return cell.Value.GetNumber().ToString("0.####", CultureInfo.InvariantCulture);
    }
}
