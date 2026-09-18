using VinayakaAgencies.App.Presentation.Controls;

namespace VinayakaAgencies.App.Tests.Presentation;

/// <summary>
/// Where Enter goes in a voucher line grid (ADR-0007).
/// </summary>
/// <remarks>
/// The rule is "across the line, then onto the next, adding one when it runs out" — Tally's, and the
/// opposite of a DataGrid's default of moving down a column.
/// </remarks>
public sealed class EntryGridNavigatorTests
{
    [Fact]
    public void Advance_WithinALine_StepsRight()
    {
        var move = EntryGridNavigator.Advance(row: 0, column: 0, rowCount: 2, columnCount: 4);

        move.ShouldBe(new EntryGridMove(0, 1, NeedsNewRow: false));
    }

    [Fact]
    public void Advance_AtTheEndOfALine_WrapsToTheStartOfTheNext()
    {
        var move = EntryGridNavigator.Advance(row: 0, column: 3, rowCount: 2, columnCount: 4);

        move.ShouldBe(new EntryGridMove(1, 0, NeedsNewRow: false));
    }

    [Fact]
    public void Advance_AtTheEndOfTheLastLine_AsksForANewOne()
    {
        // This is what makes a voucher typeable without ever reaching for "add line".
        var move = EntryGridNavigator.Advance(row: 1, column: 3, rowCount: 2, columnCount: 4);

        move.ShouldBe(new EntryGridMove(2, 0, NeedsNewRow: true));
    }

    [Fact]
    public void Advance_OnASingleCellGrid_AsksForANewLine()
    {
        EntryGridNavigator.Advance(0, 0, rowCount: 1, columnCount: 1)
            .ShouldBe(new EntryGridMove(1, 0, NeedsNewRow: true));
    }

    [Theory]
    [InlineData(0, 0, 0, 4)]   // no rows yet
    [InlineData(0, 0, 2, 0)]   // no columns yet
    [InlineData(-1, 0, 2, 4)]  // caret outside the grid
    [InlineData(0, -1, 2, 4)]
    [InlineData(5, 0, 2, 4)]
    [InlineData(0, 9, 2, 4)]
    public void Advance_OutOfRange_HoldsPositionRatherThanThrowing(
        int row,
        int column,
        int rowCount,
        int columnCount)
    {
        // This runs from a key handler on every Enter, including ones that land while the grid is
        // empty or mid-rebuild. A keystroke that crashes the screen is worse than one that does
        // nothing.
        var move = EntryGridNavigator.Advance(row, column, rowCount, columnCount);

        move.Row.ShouldBe(row);
        move.Column.ShouldBe(column);
        move.NeedsNewRow.ShouldBeFalse();
    }

    [Fact]
    public void Advance_WalksAWholeTwoLineVoucherWithoutEverSkippingAField()
    {
        // The property that matters: pressing Enter repeatedly visits every cell exactly once, in
        // reading order, before asking for a third line.
        var visited = new List<(int Row, int Column)>();
        var (row, column) = (0, 0);

        for (var press = 0; press < 8; press++)
        {
            visited.Add((row, column));

            var move = EntryGridNavigator.Advance(row, column, rowCount: 2, columnCount: 4);

            if (move.NeedsNewRow)
            {
                break;
            }

            (row, column) = (move.Row, move.Column);
        }

        visited.ShouldBe(
        [
            (0, 0), (0, 1), (0, 2), (0, 3),
            (1, 0), (1, 1), (1, 2), (1, 3),
        ]);
    }
}
