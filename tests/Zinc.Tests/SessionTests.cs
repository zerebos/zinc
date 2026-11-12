using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Zinc.Pty;
using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>
/// Headless end-to-end tests: a real child process on ConPTY feeding a real
/// libghostty terminal, read back through the formatter. No window involved.
/// </summary>
public class SessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public void BundledConptyIsDeployed()
    {
        // Guards the packaging footgun: without x64\OpenConsole.exe next to the binary,
        // ConPty silently falls back to the much slower inbox ConPTY.
        Assert.True(ConPty.UsesBundledConpty);
    }

    [Fact]
    public void AProgramThatDoesNotExistFailsToStartAndLeavesTheSessionUsable()
    {
        // The pane catches this and shows it (a typo, or a shell builtin like `dir` run on its own).
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            session.Start("zinc-no-such-program-4af1", null, 80, 24));
        session.WriteToScreen("still here");
        Assert.Contains("still here", session.ReadScreenText());
    }

    [Fact]
    public async Task ChildOutputReachesScreen()
    {
        using var s = new TestSession("cmd.exe /d /c echo zinc-hello");
        Assert.Equal(0u, await s.ExitCode);
        Assert.Contains("zinc-hello", s.Session.ReadScreenText());
    }

    [Fact]
    public async Task TheChildGetsTheTerminalsVariablesAndOurProcessKeepsItsOwn()
    {
        // As if the tests ran inside Windows Terminal.
        var before = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        Environment.SetEnvironmentVariable("WT_SESSION", "outer");
        try
        {
            // cmd leaves %NAME% as written when the variable isn't set.
            using var s = new TestSession("cmd.exe /d /c echo [%TERM_PROGRAM%][%COLORTERM%][%WT_SESSION%]");
            Assert.Equal(0u, await s.ExitCode);
            Assert.Contains("[zinc][truecolor][%WT_SESSION%]", s.Session.ReadScreenText());

            Assert.Equal(before, Environment.GetEnvironmentVariable("TERM_PROGRAM"));
            Assert.Equal("outer", Environment.GetEnvironmentVariable("WT_SESSION"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WT_SESSION", null);
        }
    }

    [Fact]
    public async Task NonZeroExitCodeIsReported()
    {
        using var s = new TestSession("cmd.exe /d /c exit 7");
        Assert.Equal(7u, await s.ExitCode);
    }

    [Fact]
    public async Task InputRoundTripsThroughChild()
    {
        using var s = new TestSession("cmd.exe /d /q /k prompt $G");
        s.Session.SendInput("echo round-%OS%-trip\r");

        // %OS% expanding proves cmd executed the line rather than us seeing an echo.
        s.WaitForScreen("round-Windows_NT-trip");

        s.Session.SendInput("exit 3\r");
        Assert.Equal(3u, await s.ExitCode);
    }

    [Fact]
    public void ResizeReachesChild()
    {
        using var s = new TestSession("cmd.exe /d /q /k prompt $G");
        s.WaitForScreen(">");
        s.Session.Resize(57, 21, 8, 16);
        Thread.Sleep(TerminalSession.PtyResizeSettle * 3); // the shell hears of it once the size settles
        s.Session.SendInput("mode con\r");
        s.WaitForScreen(new Regex(@"Columns:\s+57"));
        s.WaitForScreen(new Regex(@"Lines:\s+21"));
    }

    [Fact]
    public void DisposeKillsChild()
    {
        var s = new TestSession("cmd.exe /d /q /k");
        var pid = s.Session.ProcessId;
        Assert.NotEqual(0, pid);
        using var child = Process.GetProcessById(pid);

        s.Dispose();
        Assert.True(child.WaitForExit(Timeout), "child still running after Dispose");
    }

    [Fact]
    public void QueryRepliesAreWrittenToPty()
    {
        // No child needed: feed VT straight into the terminal and observe replies.
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;

        session.WriteToScreen("\e[6n"); // DSR cursor position: ConPTY waits on this at startup
        Assert.Contains("\e[1;1R", Joined(replies));
    }

    [Fact]
    public void SizeReportsAreAnswered()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;
        session.Resize(100, 30, 9, 18);

        // XTWINOPS cell size / text area in chars, and the report sent on enabling
        // in-band resize. libghostty drops all of them unless the OPT_SIZE callback is installed.
        session.WriteToScreen("\e[16t\e[18t\e[?2048h");
        var all = Joined(replies);
        Assert.Contains("\e[6;18;9t", all);
        Assert.Contains("\e[8;30;100t", all);
        Assert.Contains("\e[48;30;100;540;900t", all);
    }

    [Fact]
    public void Osc52WritesReachTheClipboardWriter()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var copied = new List<string>();
        session.ClipboardWriter = copied.Add;

        session.WriteToScreen("\e]52;c;aMOpbGxv\x07"); // "héllo"
        session.WriteToScreen("\e]52;p;aWdub3JlZA==\x07"); // primary selection: no Windows counterpart
        Assert.Equal(["héllo"], copied);

        session.ClipboardWriter = null; // denied: nothing arrives, nothing breaks
        session.WriteToScreen("\e]52;c;aGk=\x07");
        Assert.Single(copied);
    }

    [Fact]
    public void Osc52ReadsAreAnsweredOnlyWhenAllowed()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;

        session.WriteToScreen("\e]52;c;?\x07");
        Assert.DoesNotContain("aGk=", Joined(replies)); // denied by default

        session.ClipboardReader = () => "hi";
        session.WriteToScreen("\e]52;c;?\x07");
        Assert.Contains("\e]52;c;aGk=", Joined(replies));
    }

    [Fact]
    public void FindsHyperlinksAndPlainUrls()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("ab \e]8;;https://example.com/x\e\\link text\e]8;;\e\\ cd\r\n");
        session.WriteToScreen("see (https://ghostty.org/docs) here");

        // "link text" occupies columns 3..11.
        Assert.Equal(new LinkMatch("https://example.com/x", 0, 3, 0, 11, Explicit: true), session.LinkAt(7, 0));
        Assert.Null(session.LinkAt(1, 0));
        Assert.Null(session.LinkAt(13, 0));

        // "https://ghostty.org/docs" occupies columns 5..28, and the parentheses stay out.
        Assert.Equal(new LinkMatch("https://ghostty.org/docs", 1, 5, 1, 28, Explicit: false), session.LinkAt(10, 1));
        Assert.Null(session.LinkAt(29, 1));
    }

    [Fact]
    public void UrlsSpanSoftWrappedRows()
    {
        // 20 columns: row 0 is "go https://example.c", row 1 "om/a/long/path ok".
        using var session = new TerminalSession(20, 5, TerminalTheme.Default);
        session.WriteToScreen("go https://example.com/a/long/path ok");

        var expected = new LinkMatch("https://example.com/a/long/path", 0, 3, 1, 13, Explicit: false);
        Assert.Equal(expected, session.LinkAt(5, 0));  // on the first part
        Assert.Equal(expected, session.LinkAt(2, 1));  // on the continuation
        Assert.Null(session.LinkAt(16, 1));             // "ok"
    }

    [Fact]
    public void WrappedUrlsReachIntoTheScrollback()
    {
        // 3 rows: the URL's first row scrolls off the top, its continuation is the top visible row.
        using var session = new TerminalSession(20, 3, TerminalTheme.Default);
        session.WriteToScreen("go https://example.com/a/long/path ok\r\nnext\r\nlast");

        var link = session.LinkAt(2, 0);
        Assert.Equal(new LinkMatch("https://example.com/a/long/path", -1, 3, 0, 13, Explicit: false), link);

        // Scrolled up a row, the whole link is visible: rows 0 and 1.
        session.ScrollLines(-1);
        Assert.Equal(new LinkMatch("https://example.com/a/long/path", 0, 3, 1, 13, Explicit: false), session.LinkAt(2, 1));
    }

    [Fact]
    public void HyperlinksSpanSoftWrappedRows()
    {
        using var session = new TerminalSession(10, 5, TerminalTheme.Default);
        // 10 columns: row 0 is "see click ", row 1 "here now e", so the link text "click here now"
        // runs from column 4 of row 0 to column 7 of row 1.
        session.WriteToScreen("see \e]8;;https://example.com/\e\\click here now\e]8;;\e\\ end");

        var expected = new LinkMatch("https://example.com/", 0, 4, 1, 7, Explicit: true);
        Assert.Equal(expected, session.LinkAt(5, 0));
        Assert.Equal(expected, session.LinkAt(1, 1));
        Assert.Null(session.LinkAt(9, 1)); // the "e" of "end"
    }

    [Fact]
    public void ScrollbackKeepsTenThousandLines()
    {
        // libghostty's default byte limit would prune this to about 700 rows at 120 columns.
        using var session = new TerminalSession(120, 30, TerminalTheme.Default);
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < 12_000; i++)
        {
            text.Append($"line {i} some typical build output text here\r\n");
        }

        session.WriteToScreen(text.ToString());
        var total = session.GetScrollbar().Total;
        Assert.InRange(total, 9_000ul, 11_000ul); // the line limit, give or take a page (pruned whole)
    }

    [Fact]
    public void ScrollbarReportsScrollbackAndScrollsToRow()
    {
        using var session = new TerminalSession(20, 10, TerminalTheme.Default);
        var sb = session.GetScrollbar();
        Assert.Equal(sb.Len, sb.Total); // nothing to scroll yet

        session.WriteToScreen(string.Concat(Enumerable.Range(1, 100).Select(i => $"line {i}\r\n")));
        sb = session.GetScrollbar();
        Assert.Equal(10u, sb.Len);
        Assert.True(sb.Total > sb.Len, $"total {sb.Total}");
        Assert.Equal(sb.Total - sb.Len, sb.Offset); // following the bottom

        session.ScrollToRow(5);
        Assert.Equal(5u, session.GetScrollbar().Offset);
        Assert.StartsWith("line 6", ViewportTopLine(session));

        session.ScrollToRow(0);
        Assert.Equal(0u, session.GetScrollbar().Offset);
        Assert.StartsWith("line 1", ViewportTopLine(session));
    }

    private static unsafe string ViewportTopLine(TerminalSession session)
    {
        // Read the first viewport row's text through a grid ref.
        var text = new System.Text.StringBuilder();
        var cps = stackalloc uint[4];
        lock (session.Lock)
        {
            for (ushort x = 0; x < 8; x++)
            {
                var gridRef = Zinc.Ghostty.GhosttyGridRef.Init();
                var point = new Zinc.Ghostty.GhosttyPoint { Tag = Zinc.Ghostty.PointTag.Viewport, X = x, Y = 0 };
                Zinc.Ghostty.Native.TerminalGridRef(session.Handle, point, &gridRef);
                nuint n;
                if (GridRefGraphemes(&gridRef, cps, 4, &n) == 0 && n > 0)
                {
                    text.Append((char)cps[0]);
                }
            }
        }

        return text.ToString();
    }

    [System.Runtime.InteropServices.DllImport("ghostty-vt", EntryPoint = "ghostty_grid_ref_graphemes")]
    private static extern unsafe int GridRefGraphemes(Zinc.Ghostty.GhosttyGridRef* gridRef, uint* buf, nuint len, nuint* outLen);

    private static string Joined(List<byte[]> replies) => string.Concat(replies.Select(Encoding.UTF8.GetString));

    private sealed class TestSession : IDisposable
    {
        private readonly TaskCompletionSource<uint> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestSession(string commandLine, ushort cols = 80, ushort rows = 24)
        {
            Session = new TerminalSession(cols, rows, TerminalTheme.Default);
            Session.Resize(cols, rows, 8, 16);
            Session.ProcessExited += code => _exit.TrySetResult(code);
            Session.Start(commandLine, Environment.CurrentDirectory, cols, rows);
        }

        public TerminalSession Session { get; }

        public Task<uint> ExitCode => _exit.Task.WaitAsync(Timeout);

        public void WaitForScreen(string text) => WaitForScreen(new Regex(Regex.Escape(text)));

        public void WaitForScreen(Regex pattern)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < Timeout)
            {
                if (pattern.IsMatch(Session.ReadScreenText()))
                {
                    return;
                }

                Thread.Sleep(50);
            }

            Assert.Fail($"'{pattern}' never appeared. Screen:\n{Session.ReadScreenText()}");
        }

        public void Dispose() => Session.Dispose();
    }
}
