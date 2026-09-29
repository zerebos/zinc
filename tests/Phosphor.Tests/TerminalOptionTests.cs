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
