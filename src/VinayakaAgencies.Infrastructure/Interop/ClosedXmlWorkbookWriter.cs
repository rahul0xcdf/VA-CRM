using ClosedXML.Excel;
using VinayakaAgencies.Application.Interop;

namespace VinayakaAgencies.Infrastructure.Interop;

/// <summary>
/// The ClosedXML adapter behind <see cref="IWorkbookWriter"/>.
/// </summary>
/// <remarks>
/// Every cell is written as text, deliberately. A workbook whose amounts are typed as numbers looks
/// better and is worse: Excel holds a number as a double, so ₹1,234.5678 written as a number and
/// read back is not necessarily ₹1,234.5678 — and C8.5 says amounts survive a round trip. Text is
/// exact, and the owner can format a column in Excel in two keystrokes if they want to.
/// </remarks>
internal sealed class ClosedXmlWorkbookWriter : IWorkbookWriter
{
    public Task WriteAsync(
        WorkbookDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        using var workbook = new XLWorkbook();

        foreach (var sheet in document.Sheets)
        {
            var worksheet = workbook.Worksheets.Add(SafeSheetName(sheet.Name));

            for (var column = 0; column < sheet.Headers.Count; column++)
            {
                var cell = worksheet.Cell(1, column + 1);
                cell.Value = sheet.Headers[column];
                cell.Style.Font.Bold = true;
            }

            for (var row = 0; row < sheet.Rows.Count; row++)
            {
                var values = sheet.Rows[row];

                for (var column = 0; column < values.Count; column++)
                {
                    worksheet.Cell(row + 2, column + 1).SetValue(values[column] ?? string.Empty);
                }
            }

            if (sheet.Headers.Count > 0)
            {
                worksheet.SheetView.FreezeRows(1);
                worksheet.Columns().AdjustToContents();
            }
        }

        workbook.SaveAs(destination);

        return Task.CompletedTask;
    }

    /// <summary>
    /// A sheet name Excel will accept.
    /// </summary>
    /// <remarks>
    /// Excel refuses a sheet name over 31 characters or containing <c>: \ / ? * [ ]</c>, and refuses
    /// it by throwing when the file is saved rather than when the name is set. Trimming here means an
    /// export named after a long report still produces a file.
    /// </remarks>
    private static string SafeSheetName(string name)
    {
        var cleaned = new string([.. name.Select(c => c is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? ' ' : c)]).Trim();

        if (cleaned.Length == 0)
        {
            cleaned = "Sheet";
        }

        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
