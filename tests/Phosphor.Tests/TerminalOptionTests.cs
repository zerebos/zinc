using System.Text;
using Phosphor.Terminal;

namespace Phosphor.Tests;

/// <summary>libghostty-vt options Phosphor sets: who we are, synchronized output, ConPTY-friendly resizing.</summary>
public class TerminalOptionTests
{
    private static string Joined(List<byte[]> replies) => string.Concat(replies.Select(Encoding.UTF8.GetString));

    [Fact]
    public void XtversionSaysPhosphor()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;
        session.WriteToScreen("\x1b[>q");
        Assert.Contains("\x1bP>|phosphor ", Joined(replies));
        Assert.StartsWith("phosphor ", TerminalSession.Version);
    }

    [Fact]
    public void TheCursorSettingIsTheDefaultProgramsCanChangeAndReset()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;
        string CursorReport()
        {
            replies.Clear();
            session.WriteToScreen("\x1bP$q q\x1b\\"); // DECRQSS: which DECSCUSR style is in force?
            return Joined(replies);
        }

        session.SetCursorDefaults(Phosphor.Ghostty.TerminalCursorStyle.Bar, blink: true);
        Assert.Contains("5 q", CursorReport()); // blinking bar, at once

        session.WriteToScreen("\x1b[2 q"); // a program picks a steady block
        session.SetCursorDefaults(Phosphor.Ghostty.TerminalCursorStyle.Underline, blink: false);
        Assert.Contains("2 q", CursorReport()); // a program's own choice stays

        session.WriteToScreen("\x1b[0 q"); // and its reset goes back to the setting
        Assert.Contains("4 q", CursorReport());
    }

    [Fact]
    public void ScrollbackKeepsAboutTheLinesAskedFor()
    {
        static ulong Kept(int? lines)
        {
            using var session = new TerminalSession(40, 5, TerminalTheme.Default);
            if (lines is { } n)
            {
                session.SetScrollback(n);
            }

            session.WriteToScreen(string.Concat(Enumerable.Range(0, 5000).Select(i => $"line {i}\r\n")));
            return session.GetScrollbar().Total;
        }

        Assert.True(Kept(null) > 4000, "the default keeps 10,000");
        Assert.True(Kept(100) < 1000, "a small limit prunes (by pages, so roughly)");
    }

    [Fact]
    public void SynchronizedOutputHoldsAndReleases()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var captures = 0;
        var changes = new List<bool>();
        session.RenderHoldBegan = () => captures++;
        session.RenderHoldChanged += changes.Add;

        session.WriteToScreen("\x1b[?2026h");
        Assert.True(session.RenderHeld);
        Assert.Equal(1, captures); // the frame is captured as the hold begins

        session.WriteToScreen("frame\x1b[?2026l");
        Assert.False(session.RenderHeld);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void AHoldThatNeverEndsCanBeCalledOff()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\x1b[?2026h");
        lock (session.Lock)
        {
            session.EndRenderHold();
        }

        Assert.False(session.RenderHeld);
        Assert.False(session.GetMode(Phosphor.Ghostty.GhosttyMode.SyncOutput));
    }

    [Fact]
    public void GrowingDoesNotPullScrollbackBackIntoView()
    {
        // ConPTY keeps its own screen without scrollback; rows it never saw coming back would
        // leave the two disagreeing about what's on screen.
        // Pulled back, the rows would reappear above the cursor and push it down; kept, blank rows
        // are added below and the cursor stays on its row.
        using var session = new TerminalSession(20, 5, TerminalTheme.Default);
        session.WriteToScreen(string.Concat(Enumerable.Range(1, 10).Select(i => $"line{i}\r\n")));
        Assert.Equal(4, session.CursorRow); // the bottom row

        session.Resize(20, 8, 8, 16);
        Assert.Equal(4, session.CursorRow);
    }
}

public class TerminalSettingTests
{
    [Theory]
    [InlineData("block-hollow", Phosphor.Ghostty.TerminalCursorStyle.BlockHollow)]
    [InlineData("block_hollow", Phosphor.Ghostty.TerminalCursorStyle.BlockHollow)] // Ghostty's spelling
    [InlineData("Bar", Phosphor.Ghostty.TerminalCursorStyle.Bar)]
    public void CursorStylesRead(string value, Phosphor.Ghostty.TerminalCursorStyle style) =>
        Assert.Equal(style, new Config().Apply("cursor-style", value)!.CursorStyle);

    [Fact]
    public void ValuesOutsideTheirRangeAreRejected()
    {
        Assert.Null(new Config().Apply("cursor-style", "3"));
        Assert.Null(new Config().Apply("scrollback-lines", "-1"));
        Assert.Null(new Config().Apply("scrollback-lines", (Config.MaxScrollbackLines + 1).ToString()));
        Assert.Null(new Config().Apply("bell", "loud"));
        Assert.Equal(0, new Config().Apply("scrollback-lines", "0")!.ScrollbackLines);
    }

    [Fact]
    public void DefaultsKeepTodaysBehavior()
    {
        var config = new Config();
        Assert.Equal((Phosphor.Ghostty.TerminalCursorStyle.Block, false), (config.CursorStyle, config.CursorBlink));
        Assert.Equal((CloseOnExit.Success, BellMode.Attention, false, true),
            (config.CloseOnExit, config.Bell, config.BellSound, config.ProgramTitles));
        Assert.Null(config.Theme.SelectionBackground); // inverse video, unless a scheme says otherwise
    }
}
