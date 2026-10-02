using Zinc.Terminal;

namespace Zinc.Tests;

public class GridLayoutTests
{
    [Fact]
    public void LeftoverGoesRightAndBottomByDefault()
    {
        // 100 - 2 - 2 = 96 DIPs: 10 cells of 9 (90), 6 left over; 50 - 4 - 4 = 42: 2 rows of 17 (34), 8 left over.
        var p = GridLayout.Place(100, 50, 2, 2, 4, 4, 9, 17, 1, balance: false);
        Assert.Equal((10, 2, 2f, 4f), (p.Columns, p.Rows, p.Left, p.Top));
    }

    [Fact]
    public void BalanceSplitsTheLeftoverBetweenBothSides()
    {
        var p = GridLayout.Place(100, 50, 2, 2, 4, 4, 9, 17, 1, balance: true);
        Assert.Equal((10, 2, 5f, 8f), (p.Columns, p.Rows, p.Left, p.Top));
    }

    [Fact]
    public void BalanceMovesTheGridByWholeDevicePixels()
    {
        // At 150%: 103 - 1 - 1 = 101 DIPs, 12 cells of 8 (96), 5 over = 7.5 px; half = 3.75 px -> 3 px. The padding's
        // own offset (1 DIP = 1.5 px) keeps its half pixel.
        var p = GridLayout.Place(103, 50, 1, 1, 0, 0, 8, 17, 1.5f, balance: true);
        Assert.Equal(12, p.Columns);
        Assert.Equal(1.5f + 3, p.Left * 1.5f, 3);
    }

    [Fact]
    public void ATooSmallPaneIsNotShiftedLeft()
    {
        // Two columns at least, even when they don't fit: no negative offset.
        var p = GridLayout.Place(10, 10, 1, 1, 1, 1, 8, 17, 1, balance: true);
        Assert.Equal((2, 1, 1f, 1f), (p.Columns, p.Rows, p.Left, p.Top));
    }
}
