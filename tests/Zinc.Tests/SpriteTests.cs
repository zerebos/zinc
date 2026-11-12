using Zinc.Ghostty;

namespace Zinc.Tests;

/// <summary>Ghostty's sprite rasterizer (zinc-sprite.dll) through Zinc's binding.</summary>
public class SpriteTests
{
    [Theory]
    [InlineData(0x2500u)]   // ─ box drawing
    [InlineData(0x2588u)]   // █ block elements
    [InlineData(0x1FB45u)]  // flow's tab edges (Symbols for Legacy Computing)
    [InlineData(0x1FB82u)]  // flow's palette edge
    [InlineData(0x1FBA0u)]  // one of the diagonals the bundled font lacks
    [InlineData(0xE0B0u)]   // Powerline
    [InlineData(0x2800u)]   // Braille
    public void DrawsItsShapes(uint codepoint) => Assert.True(Sprites.Has(codepoint));

    [Theory]
    [InlineData('A')]
    [InlineData(0x263Au)]   // ☺: fonts draw it
    [InlineData(0x1F600u)]  // emoji
    [InlineData(0xE5FEu)]   // a Nerd Font icon (private use, like Powerline's neighbors)
    public void LeavesTheRestToFonts(uint codepoint)
    {
        Assert.False(Sprites.Has(codepoint));
        Assert.Null(Sprites.Draw(codepoint, 10, 16, 4, 1));
    }

    [Fact]
    public void AFullBlockFillsTheCellExactly()
    {
        var full = Sprites.Draw(0x2588, 10, 16, 4, 1)!;
        Assert.Equal((0, 0, 10, 20), (full.X, full.Y, full.Width, full.Height));
        Assert.All(full.Coverage, c => Assert.Equal(255, c));
    }

    [Fact]
    public void AnUpperQuarterBlockIsTheTopQuarter()
    {
        var quarter = Sprites.Draw(0x1FB82, 10, 16, 4, 1)!;
        Assert.Equal((0, 0, 10, 5), (quarter.X, quarter.Y, quarter.Width, quarter.Height));
    }

    [Fact]
    public void ARoundedCornerReachesTheEdgesItJoins()
    {
        // ╭ joins the cell to its right and the one below, so its lines end on those edges.
        var corner = Sprites.Draw(0x256D, 10, 16, 4, 2)!;
        Assert.Equal(10, corner.X + corner.Width);
        Assert.Equal(20, corner.Y + corner.Height);
    }

    [Fact]
    public void SwitchableGloballyAndPerProfile()
    {
        var off = ConfigFile.Parse("[font]\nbuiltin-glyphs = false\n\n[profile.x]\ncommand = \"cmd.exe\"\nfont.builtin-glyphs = true\n");
        Assert.Empty(off.Warnings);
        Assert.False(off.Global.BuiltinGlyphs);
        Assert.True(off.Profiles.Single(p => p.Id == "x").Config.BuiltinGlyphs); // per profile, like the rest of [font]
    }

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("adjust-box-thickness = 1", 1, 2)]
    [InlineData("adjust-box-thickness = -1", 2, 1)]
    [InlineData("adjust-box-thickness = -5", 2, 1)]       // never below 1
    [InlineData("adjust-box-thickness = \"2px\"", 1, 3)]
    [InlineData("adjust-box-thickness = \"50%\"", 2, 3)]
    [InlineData("adjust-box-thickness = \"-50%\"", 2, 1)]
    public void BoxThicknessCanBeAdjusted(string line, int thickness, int adjusted)
    {
        var config = ConfigFile.Parse($"[font]\n{line}\n");
        Assert.Empty(config.Warnings);
        Assert.Equal(adjusted, config.Global.AdjustedBoxThickness(thickness));
    }

    [Fact]
    public void ThicknessFollowsTheFont()
    {
        static int Rows(SpriteImage s) => Enumerable.Range(0, s.Height).Count(r => s.Coverage[r * s.Width + s.Width / 2] > 128);
        Assert.Equal(1, Rows(Sprites.Draw(0x2500, 10, 16, 4, 1)!));
        Assert.Equal(2, Rows(Sprites.Draw(0x2500, 10, 16, 4, 2)!));
    }
}
