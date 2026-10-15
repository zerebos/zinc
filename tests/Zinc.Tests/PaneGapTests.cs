using Zinc.Panes;

namespace Zinc.Tests;

public class PaneGapTests
{
    [Theory]
    // gap -> band, line
    [InlineData(0, 5, true)]
    [InlineData(-3, 5, true)]
    [InlineData(1, 1, false)]
    [InlineData(6, 6, false)]
    [InlineData(32, 32, false)]
    public void TheBandIsTheGapOrTheDivider(int gap, double band, bool line)
    {
        Assert.Equal(band, PaneGap.Band(gap));
        Assert.Equal(line, PaneGap.Line(gap));
    }

    [Fact]
    public void TheBandsMiddleFollowsThePointer()
    {
        // 805 px: 800 for the panes and a 5 px band; the band's middle at 402.5 splits them evenly.
        Assert.Equal(0.5, PaneGap.RatioAt(402.5, 805, 5));
        Assert.Equal(0.25, PaneGap.RatioAt(212, 824, 24));
        Assert.Equal(0, PaneGap.RatioAt(3, 806, 6));
        Assert.Null(PaneGap.RatioAt(10, 6, 6));
        Assert.Null(PaneGap.RatioAt(10, 0, 5));
    }

    [Theory]
    // gap, edges, workspace tabs above -> left, top, right, bottom
    [InlineData(0, true, true, 0, 0, 0, 0)]
    [InlineData(6, false, true, 0, 0, 0, 0)]
    [InlineData(6, true, true, 6, 2, 6, 6)]
    [InlineData(1, true, true, 1, 1, 1, 1)]
    [InlineData(16, true, true, 16, 2, 16, 16)]
    [InlineData(6, true, false, 6, 6, 6, 6)]   // workspaces listed on the left: nothing flows down into the panes
    [InlineData(0, true, false, 0, 0, 0, 0)]
    public void TheEdgesTakeTheGapAndTwoUnderTheTabs(int gap, bool edges, bool tabsAbove, double left, double top, double right,
        double bottom) =>
        Assert.Equal(new GapEdges(left, top, right, bottom), PaneGap.Edges(gap, edges, tabsAbove));
}
