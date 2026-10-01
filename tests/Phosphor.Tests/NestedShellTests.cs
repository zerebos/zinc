using Phosphor.Terminal;

namespace Phosphor.Tests;

/// <summary>A shell started from a command of another (fish from zsh): the command runs until that shell ends.</summary>
public class NestedShellTests
{
    // Marks as the shells send them (traced): powerlevel10k's prompt mark has no options, fish 4's
    // says click_events=1, and Phosphor's own scripts give their process id.
    private const string Zsh = "\e]133;A\a% \e]133;B\a";
    private const string Fish = "\e]133;A;click_events=1\a> \e]133;B\a";

    private static string Run(string line, string output = "", int? exit = 0) =>
        $"{line}\r\n\e]133;C;cmdline_url={Uri.EscapeDataString(line)}\a{output}{(exit is { } e ? $"\e]133;D;{e}\a" : "")}";

    private static TerminalSession Session(out Func<DateTime> advance)
    {
        var session = new TerminalSession(80, 40, TerminalTheme.Default);
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        session.Clock = () => now;
        advance = () => now = now.AddSeconds(10);
        return session;
    }

    private static string Summary(TerminalSession session) =>
        string.Join(" | ", session.Commands().Where(c => c.Text != null)
            .Select(c => $"{c.Text}:{(c.Running ? "running" : c.ExitCode?.ToString() ?? "?")}"));

    [Fact]
    public void TheCommandThatStartedAShellRunsUntilItEnds()
    {
        using var session = Session(out var advance);
        session.WriteToScreen(Zsh + Run("fish", exit: null));
        session.WriteToScreen(Fish + Run("echo hi", "hi\r\n") + Fish + Run("false", exit: 1) + Fish);
        Assert.Equal("fish:running | echo hi:0 | false:1", Summary(session));
        Assert.Null(TestControl.RunningCommand(session)); // at fish's prompt: nothing running there

        advance();
        session.WriteToScreen(Run("exit", exit: null) + "\e]133;D;1\a" + Zsh); // zsh reports fish's exit status
        Assert.Equal("fish:1 | echo hi:0 | false:1 | exit:?", Summary(session)); // not "exit" failing
        var fish = session.Commands().First(c => c.Text == "fish");
        Assert.Equal(TimeSpan.FromSeconds(10), fish.Duration);

        session.WriteToScreen(Run("echo after", "after\r\n") + Zsh);
        Assert.Equal("fish:1 | echo hi:0 | false:1 | exit:? | echo after:0", Summary(session));
    }

    [Fact]
    public void ItsRowsStopWhereTheNestedShellStarts()
    {
        using var session = Session(out _);
        session.WriteToScreen(Zsh + Run("fish", "Welcome to fish\r\n", exit: null)); // rows 0-1
        session.WriteToScreen(Fish + Run("echo hi", "hi\r\n") + Fish + "\e]133;D;0\a" + Zsh);
        var fish = session.Commands().First(c => c.Text == "fish");
        Assert.Equal((0L, 1L, 1L, 0), (fish.PromptRow, fish.OutputRow, fish.LastRow, fish.ExitCode));
    }

    [Fact]
    public void LeavingWithCtrlDAtThePromptEndsIt()
    {
        using var session = Session(out _);
        session.WriteToScreen(Zsh + Run("fish", exit: null) + Fish + Run("echo hi") + Fish);
        session.WriteToScreen("\r\n\e]133;D;0\a" + Zsh); // fish ends without a command; zsh says how
        Assert.Equal("fish:0 | echo hi:0", Summary(session));
    }

    [Fact]
    public void ShellsInsideShells()
    {
        // pwsh with Phosphor's integration, then wsl (zsh), then fish in it.
        const string Pwsh = "\e]133;A;aid=4242\aPS> \e]133;B\a";
        using var session = Session(out _);
        session.WriteToScreen(Pwsh + Run("wsl", exit: null) + Zsh + Run("fish", exit: null) + Fish + Run("echo hi") + Fish);
        Assert.Equal("wsl:running | fish:running | echo hi:0", Summary(session));

        // Leaving both at once (wsl's zsh exits with fish: "exec fish", or the window closed): back at pwsh.
        session.WriteToScreen("\e]133;D;0\a" + Pwsh);
        Assert.Equal("wsl:0 | fish:? | echo hi:0", Summary(session)); // pwsh reports on its command, wsl
    }

    [Fact]
    public void ShellsThatLookAlikeStayOneLevel()
    {
        // zsh in zsh: no way to tell the inner prompts from the outer ones, so as before nesting
        // existed: the command ends at the next prompt.
        using var session = Session(out _);
        session.WriteToScreen(Zsh + Run("zsh", exit: null) + Zsh + Run("echo hi") + Zsh);
        Assert.Equal("zsh:? | echo hi:0", Summary(session));
    }

    [Fact]
    public void ACommandWithoutDIsNotTakenForAShell()
    {
        // The same shell's next prompt after a command it sent no D for: that command just ended.
        using var session = Session(out _);
        session.WriteToScreen(Fish + Run("sleep 1", exit: null) + Fish + Run("echo hi") + Fish);
        Assert.Equal("sleep 1:? | echo hi:0", Summary(session));
    }

    [Fact]
    public void ClicksFollowEachPrompt()
    {
        using var session = Session(out _);
        session.WriteToScreen(Zsh + Run("fish", exit: null) + Fish + "echo");
        Assert.StartsWith("\e[<0;", Encoding(session.ClickToMove(2, 1, assumeArrows: true)));
        session.WriteToScreen("\r\n\e]133;D;0\a" + Zsh + "echo");
        Assert.StartsWith("\e[D", Encoding(session.ClickToMove(2, 2, assumeArrows: true))); // zsh: arrows, not click events
    }

    private static string Encoding(byte[]? bytes) => bytes == null ? "(none)" : System.Text.Encoding.ASCII.GetString(bytes);

    /// <summary>TerminalControl.RunningCommand's rule: the last command, if it's running.</summary>
    private static class TestControl
    {
        public static ShellCommand? RunningCommand(TerminalSession session) =>
            session.Commands() is { Count: > 0 } commands && commands[^1].Running ? commands[^1] : null;
    }
}
