using Phosphor.Panes;

namespace Phosphor.Tests;

public class StartLayoutTests
{
    private static Workspace<string> Build(StartLayout layout)
    {
        var n = 0;
        return StartLayouts.Build(layout, () => $"p{++n}");
    }

    [Theory]
    [InlineData(StartLayout.One, 1)]
    [InlineData(StartLayout.SideBySide, 2)]
    [InlineData(StartLayout.Stacked, 2)]
    [InlineData(StartLayout.BigAndTwo, 3)]
    [InlineData(StartLayout.Grid, 4)]
    public void EachLayoutHasItsPanesAndFocusesTheFirst(StartLayout layout, int panes)
    {
        var workspace = Build(layout);
        Assert.Equal(panes, workspace.Layout.Count);
        Assert.Equal(panes, StartLayouts.PaneCount(layout));
        Assert.Equal("p1", workspace.Active);
    }

    [Fact]
    public void SplitsGoTheRightWay()
    {
        Assert.Equal(SplitOrientation.Horizontal, ((PaneSplit)Build(StartLayout.SideBySide).Layout.Root!).Orientation);
        Assert.Equal(SplitOrientation.Vertical, ((PaneSplit)Build(StartLayout.Stacked).Layout.Root!).Orientation);

        // One big on the left; the right half split top and bottom.
        var big = (PaneSplit)Build(StartLayout.BigAndTwo).Layout.Root!;
        Assert.Equal(SplitOrientation.Horizontal, big.Orientation);
        Assert.IsType<PaneLeaf<TabGroup<string>>>(big.First);
        Assert.Equal(SplitOrientation.Vertical, ((PaneSplit)big.Second).Orientation);

        // Two columns, each split in half: four equal quarters.
        var grid = (PaneSplit)Build(StartLayout.Grid).Layout.Root!;
        Assert.Equal(SplitOrientation.Horizontal, grid.Orientation);
        Assert.All(new[] { grid.First, grid.Second }, side =>
        {
            var column = Assert.IsType<PaneSplit>(side);
            Assert.Equal((SplitOrientation.Vertical, 0.5), (column.Orientation, column.Ratio));
        });
    }
}
