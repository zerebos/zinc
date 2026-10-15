using Zinc.Ghostty;
using Zinc.Panes;

namespace Zinc.Tests;

public class PaneCardTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(6, true)]
    public void PanesAreCardsAboveGapZero(int gap, bool card) => Assert.Equal(card, PaneCard.At(gap));

    [Theory]
    // background -> header fill (the mockup's dark #0F1311 -> #181C1A and light #FBFBF8 -> #EFEFEB, within a step)
    [InlineData(0x0F1311, 0x191C1B)]
    [InlineData(0x000000, 0x0A0A0A)]
    [InlineData(0x1E1E2E, 0x272736)]
    [InlineData(0xFBFBF8, 0xEEEEEC)]
    [InlineData(0xFFFFFF, 0xF2F2F2)]
    public void HeaderFillLiftsDarkSchemesAndLowersLightOnes(int background, int fill) =>
        Assert.Equal(Rgb(fill), PaneCard.HeaderFill(Rgb(background)));

    [Fact]
    public void MixMovesPartWay()
    {
        Assert.Equal(Rgb(0x808080), PaneCard.Mix(Rgb(0x000000), Rgb(0xFFFFFF), 0.5));
        Assert.Equal(Rgb(0x102030), PaneCard.Mix(Rgb(0x102030), Rgb(0xFFFFFF), 0));
        Assert.Equal(Rgb(0xFFFFFF), PaneCard.Mix(Rgb(0x102030), Rgb(0xFFFFFF), 1));
    }

    private static GhosttyColorRgb Rgb(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
