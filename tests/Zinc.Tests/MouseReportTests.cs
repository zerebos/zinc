using System.Text;
using Zinc.Ghostty;
using Zinc.Terminal;

namespace Zinc.Tests;

public class MouseReportTests
{
    // 8x16-pixel cells, so x 0 to 7 is column 1 and x 8 to 15 is column 2.
    private static readonly GhosttyMouseEncoderSize Size = new() { ScreenWidth = 320, ScreenHeight = 80, CellWidth = 8, CellHeight = 16 };

    private static string Move(TerminalSession session, float x, float y, GhosttyMods mods = GhosttyMods.None) =>
        Encoding.ASCII.GetString(session.EncodeMouse(MouseAction.Motion, null, mods, x, y, Size, anyButtonPressed: false));

    [Fact]
    public void MotionIsReportedOncePerCell()
    {
        using var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("\e[?1003h\e[?1006h"); // any-event tracking, SGR format

        Assert.Equal("\e[<35;1;1M", Move(session, 1, 1));
        Assert.Equal("", Move(session, 4, 2));
        Assert.Equal("", Move(session, 7, 15));
        Assert.Equal("\e[<35;2;1M", Move(session, 9, 1));
        Assert.Equal("\e[<39;2;1M", Move(session, 10, 1, GhosttyMods.Shift)); // a modifier change is reported
        Assert.Equal("\e[<35;1;1M", Move(session, 1, 1));                     // and so is moving back
    }

    [Fact]
    public void AClickStartsMotionReportsAfresh()
    {
        using var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("\e[?1003h\e[?1006h");

        Assert.Equal("\e[<35;1;1M", Move(session, 1, 1));
        Assert.NotEmpty(session.EncodeMouse(MouseAction.Press, MouseButton.Left, GhosttyMods.None, 2, 2, Size, anyButtonPressed: false));
        Assert.NotEmpty(session.EncodeMouse(MouseAction.Release, MouseButton.Left, GhosttyMods.None, 2, 2, Size, anyButtonPressed: false));
        Assert.Equal("\e[<35;1;1M", Move(session, 3, 3));
    }

    [Fact]
    public void SgrPixelsReportsEveryMove()
    {
        using var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("\e[?1003h\e[?1016h");

        Assert.Equal("\e[<35;1;1M", Move(session, 1, 1));
        Assert.Equal("\e[<35;4;2M", Move(session, 4, 2));
    }

    [Fact]
    public void NoTrackingReportsNothing()
    {
        using var session = new TerminalSession(40, 5, TerminalTheme.Default);
        Assert.Equal("", Move(session, 1, 1));
    }
}
