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
        // 805 px is 800 for the panes and a 5 px band, so the band's middle at 402.5 splits them evenly.
        Assert.Equal(0.5, PaneGap.RatioAt(402.5, 805, 5));
        Assert.Equal(0.25, PaneGap.RatioAt(212, 824, 24));
        Assert.Equal(0, PaneGap.RatioAt(3, 806, 6));
        Assert.Null(PaneGap.RatioAt(10, 6, 6));
        Assert.Null(PaneGap.RatioAt(10, 0, 5));
    }

    [Theory]
    // gap, edges -> padding on every side
    [InlineData(0, true, 0)]
    [InlineData(6, false, 0)]
    [InlineData(6, true, 6)]
    [InlineData(1, true, 1)]
    [InlineData(16, true, 16)]
    public void TheEdgesTakeTheGap(int gap, bool edges, double padding) => Assert.Equal(padding, PaneGap.Edge(gap, edges));
}
