using Zinc.Ghostty;
using Zinc.Terminal;

namespace Zinc.Tests;

public class ColorSettingsTests
{
    [Fact]
    public void ContrastRatiosAreWcags()
    {
        Assert.Equal(21, MinimumContrast.Ratio((0, 0, 0), (255, 255, 255)), 2);
        Assert.Equal(1, MinimumContrast.Ratio((120, 80, 200), (120, 80, 200)), 2);
        Assert.Equal(4.54, MinimumContrast.Ratio((0x76, 0x76, 0x76), (255, 255, 255)), 2); // the usual AA gray
    }

    [Fact]
    public void TextBelowTheMinimumTurnsBlackOrWhite()
    {
        (byte, byte, byte) navy = (0x10, 0x20, 0x40), blue = (0x20, 0x40, 0x80), paleYellow = (0xFF, 0xF5, 0xC0);
        Assert.Equal(blue, MinimumContrast.Ensure(blue, navy, 1)); // off
        Assert.Equal(((byte)255, (byte)255, (byte)255), MinimumContrast.Ensure(blue, navy, 4.5)); // dark background: white
        Assert.Equal(((byte)0, (byte)0, (byte)0), MinimumContrast.Ensure((0xFF, 0xE0, 0x60), paleYellow, 4.5)); // light one: black
        Assert.Equal(paleYellow, MinimumContrast.Ensure(paleYellow, navy, 4.5)); // reads already: kept
    }

    [Fact]
    public void TheyReadFromConfigAndRejectWhatIsOutOfRange()
    {
        var config = new Config()
            .With("colors.minimum-contrast", 4.5)!
            .With("colors.bold", "#ffffff")!
            .With("colors.bold-is-bright", true)!
            .With("colors.faint-opacity", 0.7)!
            .With("colors.cursor-opacity", 0.5)!
            .With("colors.find-highlight", "#334455")!
            .With("window.divider-color", "#101010")!;
        Assert.Equal((4.5, true, 0.7, 0.5), (config.MinimumContrast, config.BoldIsBright, Math.Round(config.FaintOpacity, 2), config.CursorOpacity));
        Assert.Equal(new GhosttyColorRgb(0xFF, 0xFF, 0xFF), config.BoldColor);
        Assert.Equal(new GhosttyColorRgb(0x33, 0x44, 0x55), config.SearchBackground);
        Assert.Equal(new GhosttyColorRgb(0x10, 0x10, 0x10), config.DividerColor);

        Assert.Null(new Config().With("colors.minimum-contrast", 0.5));
        Assert.Null(new Config().With("colors.minimum-contrast", 22));
        Assert.Null(new Config().With("colors.faint-opacity", 0));
        Assert.Null(new Config().With("colors.cursor-opacity", 1.5));

        var none = new Config(); // today's looks
        Assert.Equal((1.0, false, 0.5, 1.0), (none.MinimumContrast, none.BoldIsBright, none.FaintOpacity, none.CursorOpacity));
        Assert.Null(none.BoldColor);
    }

    [Fact]
    public void ASchemeDoesNotTouchThem()
    {
        // These are settings, not scheme keys, so a scheme applied later keeps them.
        var file = Path.Combine(Path.GetTempPath(), $"zinc-scheme-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(file, "background = #000000\nminimum-contrast = 7\nbold-color = #ff0000\n");
            var themed = ThemeLibrary.ApplyFile(new Config().With("colors.minimum-contrast", 3)!, file);
            Assert.Equal(3, themed.MinimumContrast);
            Assert.Null(themed.BoldColor);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
