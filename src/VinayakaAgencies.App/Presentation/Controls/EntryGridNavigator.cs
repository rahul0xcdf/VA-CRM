namespace VinayakaAgencies.App.Presentation.Controls;

/// <summary>
/// Where the caret goes when Enter is pressed in a dense entry grid (ADR-0007).
/// </summary>
/// <param name="Row">The row to land on.</param>
/// <param name="Column">The column to land on.</param>
/// <param name="NeedsNewRow">True when the grid must grow by one row before the move can happen.</param>
public readonly record struct EntryGridMove(int Row, int Column, bool NeedsNewRow);

/// <summary>
/// The Enter-key rule for a voucher line grid (ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// A WPF <c>DataGrid</c> moves <em>down</em> on Enter, which is right for a spreadsheet and wrong for
/// a voucher: the operator is filling one line's fields left to right, not one column top to bottom.
/// Tally walks across the row and wraps onto the next, adding a line when it runs out — so an entire
/// voucher is typed without the hand leaving the keyboard or a row ever being added on purpose.
/// </para>
/// <para>
/// The arithmetic is separated from <see cref="EntryGrid"/> for the same reason
/// <see cref="LookupFilter"/> is separated from <see cref="LookupBox"/>: the behaviour needs a live
/// visual tree, and the rule that matters does not.
/// </para>
/// </remarks>
public static class EntryGridNavigator
{
    /// <summary>
    /// The cell Enter moves to from (<paramref name="row"/>, <paramref name="column"/>).
    /// </summary>
    /// <remarks>
    /// Out-of-range input holds position rather than throwing. This is called from a key handler on
    /// every Enter, including ones that arrive while a grid is empty or mid-rebuild, and a keystroke
    /// that crashes the screen is a far worse answer than a keystroke that does nothing.
    /// </remarks>
    /// <param name="row">The current row index.</param>
    /// <param name="column">The current column index.</param>
    /// <param name="rowCount">How many rows the grid has.</param>
    /// <param name="columnCount">How many editable columns the grid has.</param>
    /// <returns>The destination, and whether a row must be appended first.</returns>
    public static EntryGridMove Advance(int row, int column, int rowCount, int columnCount)
    {
        if (rowCount <= 0 || columnCount <= 0 || row < 0 || column < 0
            || row >= rowCount || column >= columnCount)
        {
            return new EntryGridMove(row, column, NeedsNewRow: false);
        }

        // Still fields left on this line: step right.
        if (column < columnCount - 1)
        {
            return new EntryGridMove(row, column + 1, NeedsNewRow: false);
        }

        // End of the last line: the next line does not exist yet, so ask for it.
        if (row == rowCount - 1)
        {
            return new EntryGridMove(rowCount, 0, NeedsNewRow: true);
        }

        // End of a line with more below: wrap to the start of the next.
        return new EntryGridMove(row + 1, 0, NeedsNewRow: false);
    }
}
