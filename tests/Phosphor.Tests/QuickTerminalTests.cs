using Windows.System;
using static Phosphor.QuickTerminalLayout;

namespace Phosphor.Tests;

public class QuickTerminalTests
{
    // A 1920x1040 work area on a monitor to the right of the primary one.
    private static readonly Rect Work = new(1920, 0, 1920, 1040);

    private static QuickTerminalSettings S(QuickTerminalPosition position, string? size = null)
    {
        var settings = new QuickTerminalSettings { Position = position };
        if (size != null)
        {
            var config = new Config().Apply("quick-terminal-size", size)!;
            settings = settings with { Primary = config.QuickTerminal.Primary, Secondary = config.QuickTerminal.Secondary };
        }

        return settings;
    }

    [Fact]
    public void TopDefaultsToHalfHeightFullWidth()
    {
        Assert.Equal(new Rect(1920, 0, 1920, 520), Place(Work, S(QuickTerminalPosition.Top)));
    }

    [Fact]
    public void BottomSitsOnTheBottomEdge()
    {
        Assert.Equal(new Rect(1920, 520, 1920, 520), Place(Work, S(QuickTerminalPosition.Bottom)));
    }

    [Fact]
    public void LeftAndRightUseWidth()
    {
        Assert.Equal(new Rect(1920, 0, 960, 1040), Place(Work, S(QuickTerminalPosition.Left)));
        Assert.Equal(new Rect(2880, 0, 960, 1040), Place(Work, S(QuickTerminalPosition.Right)));
    }

    [Fact]
    public void CenterIsCentered()
    {
        Assert.Equal(new Rect(1920 + 288, 208, 1344, 624), Place(Work, S(QuickTerminalPosition.Center)));
        Assert.Equal(new Rect(1920 + 460, 170, 1000, 700), Place(Work, S(QuickTerminalPosition.Center, "1000px,700")));
    }

    [Fact]
    public void SizesAreClampedToTheScreen()
    {
        Assert.Equal(new Rect(1920, 0, 1920, 1040), Place(Work, S(QuickTerminalPosition.Center, "5000px,5000px")));
    }

    [Theory]
    [InlineData("60%", 60, true)]
    [InlineData("800px", 800, false)]
    [InlineData("800", 800, false)]
    [InlineData(" 33.5% ", 33.5, true)]
    public void ParsesLengths(string text, double value, bool percent)
    {
        Assert.True(QuickTerminalLength.TryParse(text, out var length));
        Assert.Equal(new QuickTerminalLength(value, percent), length);
    }

    [Theory]
    [InlineData("0%")]
    [InlineData("150%")]
    [InlineData("-5px")]
    [InlineData("big")]
    public void RejectsBadLengths(string text)
    {
        Assert.False(QuickTerminalLength.TryParse(text, out _));
    }

    [Theory]
    [InlineData("win+grave", GlobalHotkey.Win, (VirtualKey)0xC0)]
    [InlineData("win+`", GlobalHotkey.Win, (VirtualKey)0xC0)]
    [InlineData("ctrl+alt+t", GlobalHotkey.Control | GlobalHotkey.Alt, VirtualKey.T)]
    [InlineData("super+shift+space", GlobalHotkey.Win | GlobalHotkey.Shift, VirtualKey.Space)]
    public void ParsesHotkeys(string text, uint mods, VirtualKey key)
    {
        Assert.Equal(new GlobalHotkey(mods, key), GlobalHotkey.Parse(text));
    }

    [Fact]
    public void HotkeyNeedsAModifier()
    {
        Assert.Throws<FormatException>(() => GlobalHotkey.Parse("grave"));
    }

    [Fact]
    public void ConfigKeysFollowGhostty()
    {
        var file = ConfigFile.Parse("""
            quick-terminal-position = center
            quick-terminal-screen = mouse
            quick-terminal-size = 60%,50%
            quick-terminal-autohide = false
            quick-terminal-hotkey = ctrl+alt+t
            quick-terminal-position = sideways
            """.Split('\n'));

        var q = file.Global.QuickTerminal;
        Assert.Equal(QuickTerminalPosition.Center, q.Position);
        Assert.Equal(QuickTerminalScreen.Mouse, q.Screen);
        Assert.Equal(new QuickTerminalLength(60, true), q.Primary);
        Assert.Equal(new QuickTerminalLength(50, true), q.Secondary);
        Assert.False(q.Autohide);
        Assert.Equal("ctrl+alt+t", q.Hotkey);
        Assert.Contains(file.Warnings, w => w.Contains("sideways"));
    }
}
