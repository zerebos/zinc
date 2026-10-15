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
    // background (dark schemes get lighter headers, light ones darker), from black to white
    [InlineData(0x000000, false)]
    [InlineData(0x0C0C0C, false)]
    [InlineData(0x0F1311, false)]
    [InlineData(0x282A36, false)]
    [InlineData(0x002B36, false)]
    [InlineData(0xEFF1F5, true)]
    [InlineData(0xFBFBF8, true)]
    [InlineData(0xFFFFFF, true)]
    public void HeaderFillStandsTheSameDistanceFromEveryScheme(int background, bool darker)
    {
        var fill = PaneCard.HeaderFill(Rgb(background));
        var ratio = PaneCard.Ratio(fill, Rgb(background));
        Assert.InRange(ratio, PaneCard.HeaderContrast - 0.01, PaneCard.HeaderContrast + 0.02); // (whole channel steps)
        Assert.Equal(darker, fill.R + fill.G + fill.B < (background >> 16) + ((background >> 8) & 0xFF) + (background & 0xFF));
    }

    [Fact]
    public void HeaderFillOfTheMockupsSchemes()
    {
        Assert.Equal("1E211F", Hex(PaneCard.HeaderFill(Rgb(0x0F1311))));
        Assert.Equal("EBEBE9", Hex(PaneCard.HeaderFill(Rgb(0xFBFBF8))));
    }

    [Fact]
    public void MixMovesPartWay()
    {
        Assert.Equal(Rgb(0x808080), PaneCard.Mix(Rgb(0x000000), Rgb(0xFFFFFF), 0.5));
        Assert.Equal(Rgb(0x102030), PaneCard.Mix(Rgb(0x102030), Rgb(0xFFFFFF), 0));
        Assert.Equal(Rgb(0xFFFFFF), PaneCard.Mix(Rgb(0x102030), Rgb(0xFFFFFF), 1));
    }

    private static string Hex(GhosttyColorRgb c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    [Fact]
    public void PaneHeaderColorReplacesTheMadeFill()
    {
        var config = new Config().Apply("pane-header-color", "#334455")!;
        Assert.Equal("334455", Hex(PaneCard.HeaderFill(config)));
        Assert.Equal(PaneCard.HeaderFill(new Config().Theme.Background), PaneCard.HeaderFill(new Config()));
        Assert.Null(new Config().Apply("pane-header-color", "blue-ish"));
    }

    private static GhosttyColorRgb Rgb(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
