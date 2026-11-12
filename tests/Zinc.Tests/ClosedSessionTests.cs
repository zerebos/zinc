using System.Text.RegularExpressions;
using Zinc.Ghostty;
using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>
/// A pane's UI can still call into its session after the pane closed (a late resize, wheel or key event), so every
/// public member must do nothing on a closed session instead of touching the freed terminal.
/// </summary>
public class ClosedSessionTests
{
    private static TerminalSession Closed()
    {
        var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("some output\r\nmore");
        session.Dispose();
        return session;
    }

    [Fact]
    public void AClosedSessionSaysSo()
    {
        using var open = new TerminalSession(40, 5, TerminalTheme.Default);
        Assert.False(open.IsDisposed);
        Assert.True(Closed().IsDisposed);
    }

    [Fact]
    public void ReadsOfAClosedSessionReturnNothing()
    {
        var session = Closed();
        Assert.Equal(0, session.RowsUsed());
        Assert.Equal(default, session.GetScrollbar());
        Assert.False(session.OnAlternateScreen());
        Assert.False(session.UpdateRenderState(0));
        Assert.False(session.GetMode(GhosttyMode.BracketedPaste));
        Assert.False(session.IsMouseTracking);
        Assert.Equal(0, session.KittyKeyboardFlags);
        Assert.Equal(0, session.CursorRow);
        Assert.Equal("", session.ReadScreenText());
        Assert.Equal("", session.ReadText());
        Assert.Equal("", session.ReadText(lines: 3, styled: true));
        Assert.Null(session.GetSelectionText());
        Assert.Null(session.GetSelectionHtml("x", 10));
        Assert.Null(session.LinkAt(0, 0));
        Assert.Null(session.ClickToMove(0, 0, assumeArrows: true));
        Assert.Null(session.TakeTitle());
        Assert.Empty(session.Commands());
        Assert.Null(session.CommandInView());
        Assert.Null(session.CommandAt(0));
        Assert.False(session.JumpToPrompt(-1));

        var matches = new List<SearchMatch>();
        session.Search(new Regex("output"), matches);
        Assert.Empty(matches);

        var images = new List<ImagePlacement>();
        session.CollectImages(images, (_, _) => true);
        Assert.Empty(images);
    }

    [Fact]
    public void InputToAClosedSessionEncodesNothing()
    {
        var session = Closed();
        Assert.Empty(session.EncodeKey(KeyAction.Press, GhosttyKey.ArrowUp, GhosttyMods.None, GhosttyMods.None, null, 0));
        var size = new GhosttyMouseEncoderSize { ScreenWidth = 320, ScreenHeight = 80, CellWidth = 8, CellHeight = 16 };
        Assert.Empty(session.EncodeMouse(MouseAction.Press, MouseButton.Left, GhosttyMods.None, 4, 4, size, anyButtonPressed: false));
    }

    [Fact]
    public void ChangesToAClosedSessionAreIgnored()
    {
        var session = Closed();
        session.Resize(80, 24, 8, 16);
        session.Resize(80, 24, 8, 16, atOnce: true);
        session.ScrollLines(-3);
        session.ScrollToRow(0);
        session.ScrollViewport(new GhosttyTerminalScrollViewport { Tag = ScrollViewportTag.Bottom });
        session.ClearSelection();
        session.EndRenderHold();
        session.SetScrollback(100);
        session.SetCursorDefaults(TerminalCursorStyle.Bar, blink: true);
        session.SetColorScheme(dark: false);
        session.SetTheme(TerminalTheme.Default);
        session.WriteToScreen("late output");
        session.SendInput("typed late");
        session.Paste("pasted late");
        session.RecheckReportedDirectory();
        Assert.False(session.RenderHeld);
        Assert.Equal(0, session.RowsUsed());
    }

    [Fact]
    public void ASelectionGestureOnAClosedSessionDoesNothing()
    {
        var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("some output");
        var gesture = new SelectionGesture(session);
        session.Dispose();
        var geometry = new GhosttySelectionGestureGeometry { Columns = 40, CellWidth = 8, ScreenHeight = 5 * 16 };
        gesture.Handle(GesturePhase.Press, 0, 0, 2, 8, geometry, 1_000_000_000, repeatDistancePx: 8);
        Assert.Null(session.GetSelectionText());
    }
}
