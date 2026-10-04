using Windows.System;
using static Zinc.QuickTerminalLayout;

namespace Zinc.Tests;

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
    public void ReadsTheQuickTerminalSection()
    {
        var file = ConfigFile.Parse("""
            [quick-terminal]
            position = "center"
            screen = "mouse"
            size = ["60%", "50%"]
            autohide = false
            hotkey = "ctrl+alt+t"
            """);

        var q = file.Global.QuickTerminal;
        Assert.Equal(QuickTerminalPosition.Center, q.Position);
        Assert.Equal(QuickTerminalScreen.Mouse, q.Screen);
        Assert.Equal(new QuickTerminalLength(60, true), q.Primary);
        Assert.Equal(new QuickTerminalLength(50, true), q.Secondary);
        Assert.False(q.Autohide);
        Assert.Equal("ctrl+alt+t", q.Hotkey);
        Assert.Empty(file.Warnings);
        Assert.Contains(ConfigFile.Parse("[quick-terminal]\nposition = \"sideways\"").Warnings, w => w.Contains("sideways"));
    }

    [Fact]
    public void CenteredAndSlidingByDefault()
    {
        var q = new Config().QuickTerminal;
        Assert.Equal(QuickTerminalPosition.Center, q.Position);
        Assert.Equal(0.2, q.AnimationDuration);
        Assert.Equal(0, ConfigFile.Parse("[quick-terminal]\nanimation-duration = 0").Global.QuickTerminal.AnimationDuration);
        Assert.NotEmpty(ConfigFile.Parse("[quick-terminal]\nanimation-duration = -1").Warnings);
    }

    [Fact]
    public void ADraggedSizeKeepsItsPlace()
    {
        // Centered stays centered; against an edge stays there (and centered along it); too big = the screen.
        Assert.Equal(new Rect(1920 + 460, 170, 1000, 700), Place(Work, S(QuickTerminalPosition.Center), (1000, 700)));
        Assert.Equal(new Rect(1920 + 460, 0, 1000, 300), Place(Work, S(QuickTerminalPosition.Top), (1000, 300)));
        Assert.Equal(new Rect(1920, 740, 1920, 300), Place(Work, S(QuickTerminalPosition.Bottom), (1920, 300)));
        Assert.Equal(new Rect(3840 - 500, 270, 500, 500), Place(Work, S(QuickTerminalPosition.Right), (500, 500)));
        Assert.Equal(new Rect(1920, 0, 1920, 1040), Place(Work, S(QuickTerminalPosition.Center), (9000, 9000)));
    }

    [Fact]
    public void EdgesAgainstTheScreenDontDrag()
    {
        Assert.Equal(8, QuickTerminalResize.Edges(QuickTerminalPosition.Center).Count);
        Assert.Equal(
            [QuickTerminalEdge.Left, QuickTerminalEdge.Right, QuickTerminalEdge.Bottom, QuickTerminalEdge.BottomLeft, QuickTerminalEdge.BottomRight],
            QuickTerminalResize.Edges(QuickTerminalPosition.Top));
        Assert.DoesNotContain(QuickTerminalEdge.TopRight, QuickTerminalResize.Edges(QuickTerminalPosition.Right));
    }

    [Fact]
    public void DraggingAnEdgeResizes()
    {
        (int, int) start = (1000, 600), min = (300, 150), max = (1920, 1040);
        // Centered: both sides move, so the dragged edge follows the pointer.
        Assert.Equal((1100, 600), QuickTerminalResize.Size(QuickTerminalPosition.Center, QuickTerminalEdge.Right, start, 50, 30, min, max));
        Assert.Equal((1100, 600), QuickTerminalResize.Size(QuickTerminalPosition.Center, QuickTerminalEdge.Left, start, -50, 30, min, max));
        Assert.Equal((1000, 540), QuickTerminalResize.Size(QuickTerminalPosition.Center, QuickTerminalEdge.Top, start, 0, 30, min, max));
        Assert.Equal((900, 660), QuickTerminalResize.Size(QuickTerminalPosition.Center, QuickTerminalEdge.BottomLeft, start, 50, 30, min, max));
        // Against the top: the bottom edge moves alone; the sides still grow both ways.
        Assert.Equal((1000, 630), QuickTerminalResize.Size(QuickTerminalPosition.Top, QuickTerminalEdge.Bottom, start, 0, 30, min, max));
        Assert.Equal((1100, 630), QuickTerminalResize.Size(QuickTerminalPosition.Top, QuickTerminalEdge.BottomRight, start, 50, 30, min, max));
        // Against the left: the right edge moves alone.
        Assert.Equal((1050, 600), QuickTerminalResize.Size(QuickTerminalPosition.Left, QuickTerminalEdge.Right, start, 50, 0, min, max));
        // Limits.
        Assert.Equal((300, 600), QuickTerminalResize.Size(QuickTerminalPosition.Center, QuickTerminalEdge.Right, start, -2000, 0, min, max));
        Assert.Equal((1920, 1040), QuickTerminalResize.Size(QuickTerminalPosition.Center, QuickTerminalEdge.BottomRight, start, 2000, 2000, min, max));
    }

    [Fact]
    public void SizesAreRememberedPerMonitorWhileTheConfigStands()
    {
        var center = S(QuickTerminalPosition.Center);
        var sizes = new QuickTerminalSizes();
        sizes.Remember("0,0,2560x1440", center, 1200, 700);
        sizes.Remember("2560,0,2560x1440", center, 900, 500);
        Assert.Equal((1200, 700), sizes.For("0,0,2560x1440", center));
        Assert.Equal((900, 500), sizes.For("2560,0,2560x1440", center));
        Assert.Null(sizes.For("elsewhere", center));

        // Dragged again: replaced, not added.
        sizes.Remember("0,0,2560x1440", center, 1300, 750);
        Assert.Equal(2, sizes.All.Count);
        Assert.Equal((1300, 750), sizes.For("0,0,2560x1440", center));

        // The config moved it or gave it another size: the config's size again.
        Assert.Null(sizes.For("0,0,2560x1440", S(QuickTerminalPosition.Top)));
        Assert.Null(sizes.For("0,0,2560x1440", S(QuickTerminalPosition.Center, "60%,50%")));

        Assert.True(sizes.Forget("0,0,2560x1440"));
        Assert.False(sizes.Forget("0,0,2560x1440"));
        Assert.Null(sizes.For("0,0,2560x1440", center));

        for (var i = 0; i < 20; i++)
        {
            sizes.Remember($"m{i}", center, 800, 400);
        }

        Assert.Equal(QuickTerminalSizes.Capacity, sizes.All.Count);
        Assert.Equal("center 60% 50%", QuickTerminalSizes.BaseOf(S(QuickTerminalPosition.Center, "60%,50%")));
    }

    [Fact]
    public void SizesSurviveTheSessionFile()
    {
        var state = new Zinc.Session.SessionState(Zinc.Session.SessionState.CurrentVersion, [],
            QuickTerminalSizes: [new QuickTerminalSize("0,0,2560x1440", 1200, 700, "center - -")]);
        var back = Zinc.Session.SessionStore.Deserialize(Zinc.Session.SessionStore.Serialize(state));
        Assert.Equal(state.QuickTerminalSizes, back!.QuickTerminalSizes);
    }

    [Fact]
    public void AppearsFromWhereItIs()
    {
        Assert.Equal((0.7, 0.0, 0.0), QuickTerminalAppear.Start(QuickTerminalPosition.Center, 1200, 600));
        Assert.Equal((1.0, 0.0, -120.0), QuickTerminalAppear.Start(QuickTerminalPosition.Top, 1200, 600)); // at most 120
        Assert.Equal((1.0, 0.0, 50.0), QuickTerminalAppear.Start(QuickTerminalPosition.Bottom, 1200, 200)); // a quarter
        Assert.Equal((1.0, -120.0, 0.0), QuickTerminalAppear.Start(QuickTerminalPosition.Left, 900, 1000));
        Assert.Equal((1.0, 100.0, 0.0), QuickTerminalAppear.Start(QuickTerminalPosition.Right, 400, 1000));
    }
}
