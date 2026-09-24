using VinayakaAgencies.Application.Interop;

namespace VinayakaAgencies.Application.Tests.Interop;

/// <summary>
/// A workbook as a grid of strings, standing in for ClosedXML.
/// </summary>
/// <remarks>
/// <see cref="IWorkbookReader"/> exists so the mapping and validation rules can be tested without a
/// real file, and this is that half of the bargain. The adapter's own job — turning Excel's typed
/// cells into invariant text — is tested against a real workbook in the Infrastructure suite, where
/// a fake would prove nothing.
/// </remarks>
internal sealed class FakeWorkbookReader : IWorkbookReader
{
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyList<string?>>> _sheets;

    public FakeWorkbookReader(params (string Name, IReadOnlyList<IReadOnlyList<string?>> Rows)[] sheets)
    {
        _sheets = sheets.ToDictionary(sheet => sheet.Name, sheet => sheet.Rows, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A single unnamed sheet, for the common case.</summary>
    public static FakeWorkbookReader WithRows(params IReadOnlyList<string?>[] rows) =>
        new(("Sheet1", rows));

    public Task<IReadOnlyList<string>> ListSheetsAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. _sheets.Keys]);

    public async IAsyncEnumerable<WorkbookRow> ReadRowsAsync(
        string path,
        string? sheetName = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rows = string.IsNullOrWhiteSpace(sheetName)
            ? _sheets.Values.First()
            : _sheets[sheetName];

        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new WorkbookRow(index + 1, rows[index]);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
