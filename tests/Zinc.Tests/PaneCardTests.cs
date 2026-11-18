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
    // Backgrounds from black to white. A dark scheme gets a lighter header, and a light scheme a darker one.
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
        Assert.InRange(ratio, PaneCard.HeaderContrast - 0.01, PaneCard.HeaderContrast + 0.02); // channels move in whole steps, so the ratio can't land exactly
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
    public void PaneHeaderColorReplacesTheDerivedFill()
    {
        var config = new Config().With("colors.pane-header", "#334455")!;
        Assert.Equal("334455", Hex(PaneCard.HeaderFill(config)));
        Assert.Equal(new Config().Theme.Background, PaneCard.HeaderFill(new Config())); // by default, the header is the terminal's own background
        Assert.Equal(PaneCard.HeaderFill(new Config().Theme.Background), PaneCard.HeaderFill(new Config().With("colors.pane-header", "standard")!));
        Assert.Null(new Config().With("colors.pane-header", "blue-ish"));
        Assert.Null(new Config().With("colors.pane-header", "medium"));
    }

    [Theory]
    [InlineData("terminal", HeaderLevel.Terminal)]
    [InlineData("subtle", HeaderLevel.Subtle)]
    [InlineData("standard", HeaderLevel.Standard)]
    [InlineData("auto", HeaderLevel.Standard)] // its older name
    [InlineData("strong", HeaderLevel.Strong)]
    public void PaneHeaderLevelsByName(string value, HeaderLevel level)
    {
        var config = new Config().With("colors.pane-header", "#334455")!.With("colors.pane-header", value)!;
        Assert.Equal(level, config.PaneHeaderLevel);
        Assert.Null(config.PaneHeaderColor); // the last one set wins
        Assert.Equal(PaneCard.HeaderFill(config.Theme.Background, level), PaneCard.HeaderFill(config));
    }

    [Theory]
    [InlineData(0x0C110E)]
    [InlineData(0x282A36)]
    [InlineData(0xEFF1F5)]
    [InlineData(0xFFFFFF)]
    public void EachLevelStandsItsContrastApart(int background)
    {
        Assert.Equal(Rgb(background), PaneCard.HeaderFill(Rgb(background), HeaderLevel.Terminal));
        foreach (var level in new[] { HeaderLevel.Subtle, HeaderLevel.Standard, HeaderLevel.Strong })
        {
            var ratio = PaneCard.Ratio(PaneCard.HeaderFill(Rgb(background), level), Rgb(background));
            Assert.InRange(ratio, PaneCard.ContrastOf(level) - 0.01, PaneCard.ContrastOf(level) + 0.03);
        }
    }

    [Theory]
    // A border stands one level further from the terminal than its header does, and 1.6 past Strong.
    [InlineData(0x0C110E, HeaderLevel.Subtle, 1.15)]
    [InlineData(0x0C110E, HeaderLevel.Standard, 1.3)]
    [InlineData(0x0C110E, HeaderLevel.Strong, 1.6)]
    [InlineData(0xEFF1F5, HeaderLevel.Subtle, 1.15)]
    [InlineData(0xEFF1F5, HeaderLevel.Standard, 1.3)]
    [InlineData(0xEFF1F5, HeaderLevel.Strong, 1.6)]
    public void TheBorderIsOneStepPastTheHeader(int background, HeaderLevel level, double border)
    {
        var edge = PaneCard.EdgeFill(PaneCard.HeaderFill(Rgb(background), level), Rgb(background));
        Assert.InRange(PaneCard.Ratio(edge, Rgb(background)), border - 0.01, border + 0.04);
    }

    [Fact]
    public void ASeamlessHeaderHasNoBorderAndFlipsTheTabs()
    {
        var config = new Config().With("colors.pane-header", "standard")!.With("colors.pane-header", "terminal")!;
        Assert.Equal(new Config(), config); // the default
        Assert.Equal(config.Theme.Background, PaneCard.EdgeFill(config));
        Assert.Equal(PaneCard.HeaderFill(config.Theme.Background), PaneCard.HiddenTabFill(config));
        Assert.Null(PaneCard.HiddenTabFill(new Config().With("colors.pane-header", "standard")!)); // with a derived fill, hidden tabs have none of their own and the header shows through
    }

    [Fact]
    public void ACustomHeaderNearTheTerminalCountsAsSeamless()
    {
        var background = new Config().Theme.Background;
        var near = new GhosttyColorRgb((byte)(background.R + 1), background.G, background.B);
        var config = new Config().With("colors.pane-header", $"#{Hex(near)}")!;
        Assert.NotNull(PaneCard.HiddenTabFill(config));
        Assert.Equal(background, PaneCard.EdgeFill(config));
    }

    [Fact]
    public void ACustomHeadersBorderKeepsItsHue()
    {
        // A purple header on a dark terminal gets a lighter purple border, which still reads as purple.
        var edge = PaneCard.EdgeFill(Rgb(0x5A3A7A), Rgb(0x0C110E));
        Assert.True(PaneCard.Ratio(edge, Rgb(0x0C110E)) > PaneCard.Ratio(Rgb(0x5A3A7A), Rgb(0x0C110E)));
        Assert.True(edge.B > edge.G && edge.R > edge.G);
    }

    private static GhosttyColorRgb Rgb(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
