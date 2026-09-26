using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Phantom.Pty;
using Phantom.Terminal;

namespace Phantom.Tests;

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
    public async Task ChildOutputReachesScreen()
    {
        using var s = new TestSession("cmd.exe /d /c echo phantom-hello");
        Assert.Equal(0u, await s.ExitCode);
        Assert.Contains("phantom-hello", s.Session.ReadScreenText());
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

        session.WriteToScreen("\x1b[6n"); // DSR cursor position: ConPTY waits on this at startup
        Assert.Contains("\x1b[1;1R", Joined(replies));
    }

    [Fact]
    public void SizeReportsAreAnswered()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;
        session.Resize(100, 30, 9, 18);

        // XTWINOPS cell size / text area in chars, and the report sent on enabling
        // in-band resize. Regression: all were dropped without the OPT_SIZE callback.
        session.WriteToScreen("\x1b[16t\x1b[18t\x1b[?2048h");
        var all = Joined(replies);
        Assert.Contains("\x1b[6;18;9t", all);
        Assert.Contains("\x1b[8;30;100t", all);
        Assert.Contains("\x1b[48;30;100;540;900t", all);
    }

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
