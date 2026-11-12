using System.Text;
using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>libghostty-vt options Zinc sets: who we are, synchronized output, ConPTY-friendly resizing.</summary>
public class TerminalOptionTests
{
    private static string Joined(List<byte[]> replies) => string.Concat(replies.Select(Encoding.UTF8.GetString));

    [Fact]
    public void XtversionSaysZinc()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;
        session.WriteToScreen("\x1b[>q");
        Assert.Contains("\x1bP>|zinc ", Joined(replies));
        Assert.StartsWith("zinc ", TerminalSession.Version);
        // The version comes from Directory.Build.props, either the release ("0.1.0+a1b2c3d") or a dev build ("0.1.0-dev.812+a1b2c3d").
        Assert.Matches(@"^zinc \d+\.\d+\.\d+(-dev\.\d+)?(\+[0-9a-f]{7})?$", TerminalSession.Version);
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

        session.SetCursorDefaults(Zinc.Ghostty.TerminalCursorStyle.Bar, blink: true);
        Assert.Contains("5 q", CursorReport()); // blinking bar, at once

        session.WriteToScreen("\x1b[2 q"); // a program picks a steady block
        session.SetCursorDefaults(Zinc.Ghostty.TerminalCursorStyle.Underline, blink: false);
        Assert.Contains("2 q", CursorReport()); // a program's own choice stays

        session.WriteToScreen("\x1b[0 q"); // and its reset goes back to the setting
        Assert.Contains("4 q", CursorReport());
    }

    [Fact]
    public void ThePromptCanHaveItsOwnCursor()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;
        string CursorReport()
        {
            replies.Clear();
            session.WriteToScreen("\x1bP$q q\x1b\\");
            return Joined(replies);
        }

        session.SetCursorDefaults(Zinc.Ghostty.TerminalCursorStyle.Block, blink: false, atPrompt: Zinc.Ghostty.TerminalCursorStyle.Bar);
        Assert.Contains("2 q", CursorReport()); // no prompt yet: the usual block

        session.WriteToScreen("\x1b]133;A\a$ \x1b]133;B\a");
        Assert.Contains("6 q", CursorReport()); // at the prompt: a steady bar

        session.WriteToScreen("ls\r\n\x1b]133;C\a");
        Assert.Contains("2 q", CursorReport()); // running: the block again

        session.WriteToScreen("\x1b]133;D;0\a\x1b]133;A\a$ \x1b]133;B\a\x1b[4 q"); // a vi mode picks its own
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
        Assert.False(session.GetMode(Zinc.Ghostty.GhosttyMode.SyncOutput));
    }

    [Fact]
    public void GrowingDoesNotPullScrollbackBackIntoView()
    {
        // ConPTY keeps its own screen without scrollback, so rows it never saw coming back would
        // leave the two disagreeing about what's on screen.
        // Pulled back, the rows would reappear above the cursor and push it down. With them kept in the
        // scrollback, blank rows are added below and the cursor stays on its row.
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
    [InlineData("block-hollow", Zinc.Ghostty.TerminalCursorStyle.BlockHollow)]
    [InlineData("block_hollow", Zinc.Ghostty.TerminalCursorStyle.BlockHollow)] // Ghostty's spelling
    [InlineData("Bar", Zinc.Ghostty.TerminalCursorStyle.Bar)]
    public void CursorStylesRead(string value, Zinc.Ghostty.TerminalCursorStyle style) =>
        Assert.Equal(style, new Config().With("terminal.cursor-style", value)!.CursorStyle);

    [Fact]
    public void CursorTextComesFromConfigOrScheme()
    {
        Assert.Equal(new Zinc.Ghostty.GhosttyColorRgb(0x11, 0x22, 0x33), new Config().With("colors.cursor-text", "#112233")!.Theme.CursorText);

        var scheme = Path.Combine(Path.GetTempPath(), $"zinc-scheme-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(scheme, "background = #102040\ncursor-color = #007acc\ncursor-text = #bfdbfe\n");
            Assert.Equal(new Zinc.Ghostty.GhosttyColorRgb(0xBF, 0xDB, 0xFE),
                ThemeLibrary.ApplyFile(new Config { Theme = TerminalTheme.SchemeBase }, scheme).Theme.CursorText);

            File.WriteAllText(scheme, "background = #102040\n");
            Assert.Null(ThemeLibrary.ApplyFile(new Config { Theme = TerminalTheme.SchemeBase }, scheme).Theme.CursorText); // the background, as before
        }
        finally
        {
            File.Delete(scheme);
        }
    }

    [Fact]
    public void ValuesOutsideTheirRangeAreRejected()
    {
        Assert.Null(new Config().With("terminal.cursor-style", "3"));
        Assert.Null(new Config().With("terminal.scrollback-lines", -1));
        Assert.Null(new Config().With("terminal.scrollback-lines", Config.MaxScrollbackLines + 1));
        Assert.Null(new Config().With("terminal.bell", "loud"));
        Assert.Equal(0, new Config().With("terminal.scrollback-lines", 0)!.ScrollbackLines);
    }

    [Fact]
    public void DefaultsKeepTodaysBehavior()
    {
        var config = new Config();
        Assert.Equal((Zinc.Ghostty.TerminalCursorStyle.Block, false), (config.CursorStyle, config.CursorBlink));
        Assert.Equal((CloseOnExit.Success, BellMode.Attention, false, true),
            (config.CloseOnExit, config.Bell, config.BellSound, config.ProgramTitles));
        Assert.Same(TerminalTheme.Default, config.Theme); // Lit Cell, selection colors included
    }
}
