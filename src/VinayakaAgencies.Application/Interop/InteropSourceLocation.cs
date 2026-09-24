using System.Globalization;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Where a record came from in the file the owner is holding.
/// </summary>
/// <remarks>
/// C8.4 requires an import to report "the offending row and reason". A row index alone is not that:
/// the owner has to be able to open the file and find the line. So every source states its position
/// in its own terms — a line number in XML, a sheet and cell reference in a workbook, an array index
/// in JSON — and the error report prints whichever the source produced.
/// </remarks>
/// <param name="Ordinal">1-based position among the records this source yielded, always meaningful.</param>
/// <param name="Description">How to find it in the file, in the source's own terms.</param>
public sealed record InteropSourceLocation(int Ordinal, string Description)
{
    public static InteropSourceLocation AtLine(int ordinal, int lineNumber) =>
        new(ordinal, $"line {lineNumber.ToString(CultureInfo.InvariantCulture)}");

    public static InteropSourceLocation AtRow(int ordinal, string sheetName, int rowNumber) =>
        new(ordinal, $"{sheetName} row {rowNumber.ToString(CultureInfo.InvariantCulture)}");

    public static InteropSourceLocation AtIndex(int ordinal, string collection, int index) =>
        new(ordinal, $"{collection}[{index.ToString(CultureInfo.InvariantCulture)}]");

    public override string ToString() => Description;
}
