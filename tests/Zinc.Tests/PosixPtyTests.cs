#if !WINDOWS
using System.Text.RegularExpressions;
using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>
/// The PTY that builds off Windows use, with a real <c>/bin/sh</c> on it. SessionTests covers the same ground on
/// ConPTY with <c>cmd.exe</c>.
/// </summary>
public class PosixPtyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static void WaitForScreen(TerminalSession session, string pattern)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!Regex.IsMatch(session.ReadScreenText(), pattern))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for /{pattern}/. The screen has:\n{session.ReadScreenText()}");
            Thread.Sleep(20);
        }
    }

    private static (TerminalSession Session, Task<uint> ExitCode) Start(string commandLine)
    {
        var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var exit = new TaskCompletionSource<uint>();
        session.ProcessExited += code => exit.TrySetResult(code);
        session.Start(commandLine, null, 80, 24);
        return (session, exit.Task);
    }

    [Fact]
    public async Task ChildOutputReachesScreenAndItsExitCodeComesBack()
    {
        var (session, exit) = Start("/bin/sh -c 'echo zinc-hello; exit 3'");
        using (session)
        {
            Assert.Equal(3u, await exit.WaitAsync(Timeout, TestContext.Current.CancellationToken));
            Assert.Contains("zinc-hello", session.ReadScreenText());
        }
    }

    [Fact]
    public void InputReachesTheChildWithTheTerminalsVariables()
    {
        var (session, _) = Start("/bin/sh");
        using (session)
        {
            session.SendInput("echo \"[$TERM_PROGRAM][$COLORTERM][$((6*7))]\"\r");
            WaitForScreen(session, @"\[zinc\]\[truecolor\]\[42\]");
        }
    }

    [Fact]
    public void TheChildLearnsItsSizeAndAResize()
    {
        // The script prints its size at the start and on every SIGWINCH, so nothing is typed while the size
        // changes. (A line typed into bash 3.2 as the signal arrives can be dropped, which isn't ours to fix.)
        var (session, _) = Start("/bin/sh -c 'trap \"stty size\" WINCH; stty size; while :; do sleep 0.05; done'");
        using (session)
        {
            WaitForScreen(session, @"(?m)^24 80");
            session.Resize(100, 30, 8, 16, atOnce: true);
            WaitForScreen(session, @"(?m)^30 100");
        }
    }

    [Fact]
    public void TheChildGetsAnAnswerToACursorPositionQuery()
    {
        // The terminal answers through the PTY, so this is a round trip: the query out, the report back in.
        var (session, _) = Start("/bin/sh");
        using (session)
        {
            session.SendInput("stty -echo; printf '\\033[6n'; IFS=R read -r -d R reply; printf 'got:%s\\n' \"${reply#??}\"\r");
            WaitForScreen(session, @"got:\d+;\d+");
        }
    }

    [Fact]
    public async Task DisposingTheSessionEndsTheChild()
    {
        var (session, exit) = Start("/bin/sh");
        WaitForScreen(session, @"\$");
        session.Dispose();
        Assert.Equal(129u, await exit.WaitAsync(Timeout, TestContext.Current.CancellationToken)); // 128 plus SIGHUP
    }

    [Fact]
    public void AProgramThatDoesNotExistFailsToStartAndLeavesTheSessionUsable()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        Assert.Throws<FileNotFoundException>(() => session.Start("zinc-no-such-program-4af1", null, 80, 24));
        session.WriteToScreen("still here");
        Assert.Contains("still here", session.ReadScreenText());
    }
}
#endif
