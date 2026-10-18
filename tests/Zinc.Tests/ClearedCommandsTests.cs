using System.Text;
using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>A tester's report (2026-10-03): failure bars and scrollbar ticks stayed after <c>clear</c>.</summary>
public class ClearedCommandsTests
{
    private static string Command(string line, int exit) =>
        $"\e]133;A\a$ \e]133;B\a{line}\r\n\e]133;C\aout\r\n\e]133;D;{exit}\a";

    /// <summary>Six failed commands on an 8-row screen: the first three have scrolled into the scrollback.</summary>
    private static TerminalSession WithFailures()
    {
        var session = new TerminalSession(40, 8, TerminalTheme.Default);
        for (var i = 0; i < 6; i++)
        {
            session.WriteToScreen(Command($"false{i}", 1));
        }

        return session;
    }

    private static string Clear(string sequence) =>
        $"\e]133;A\a$ \e]133;B\aclear\r\n\e]133;C\a{sequence}\e]133;D;0\a\e]133;A\a$ \e]133;B\a";

    [Theory]
    [InlineData("\e[H\e[2J\e[3J")] // bash, zsh: clear
    [InlineData("\e[3J\e[H\e[2J")] // ncurses' order
    [InlineData("\e[2J\e[3J\e[H")] // PowerShell's Clear-Host through ConPTY
    [InlineData("\ec")]            // a full reset
    public void WipingTheScrollbackForgetsEveryFinishedCommand(string clear)
    {
        using var session = WithFailures();
        Assert.Equal(6, session.Commands().Count(c => c.Failed));

        session.WriteToScreen(Clear(clear));

        var commands = session.Commands();
        Assert.DoesNotContain(commands, c => c.Failed);
        // The clear itself stays (it was running), except after a full reset, which wipes its place too.
        Assert.Equal(clear == "\ec" ? [null] : ["clear", null], commands.Select(c => c.Text));
    }

    [Theory]
    [InlineData("\e[H\e[2J")] // Fedora's clear (no E3 in its terminfo), as ConPTY passes it on
    [InlineData("\e[H\e[J")]
    public void ClearingTheScreenTakesItsCommandsTheScrollbackKeepsTheRest(string clear)
    {
        // libghostty erases the screen in place: the commands on it are gone (their bars stayed beside empty rows),
        // the ones already scrolled into the scrollback are still there, marks and all. The next prompt lands on an
        // erased row again, which mustn't bring an old command back.
        using var session = WithFailures();
        session.WriteToScreen(Clear(clear));
        var scrollback = (long)(session.GetScrollbar().Total - session.GetScrollbar().Len);
        var failed = session.Commands().Where(c => c.Failed).ToList();
        Assert.Equal(["false0", "false1", "false2"], failed.Select(c => c.Text));
        Assert.All(failed, c => Assert.True(c.PromptRow < scrollback));
    }

    [Fact]
    public void ShellsRedrawingTheirPromptKeepTheirCommands()
    {
        // A prompt redrawn in place (nushell on every key, zsh themes) erases to the end of the screen around a
        // live prompt: finished commands above it, still on screen, stay.
        using var session = WithFailures();
        session.WriteToScreen("\e]133;A\a$ \e]133;B\ax\r\e[J\e]133;A\a$ \e]133;B\axy\r\e[J\e]133;A\a$ \e]133;B\axyz");
        Assert.Equal(6, session.Commands().Count(c => c.Failed));
    }

    [Fact]
    public void ACommandStillRunningStays()
    {
        // A nested shell's host (zsh started fish) is still running when fish clears: only finished ones go.
        using var session = WithFailures();
        session.WriteToScreen("\e]133;A\a$ \e]133;B\atop\r\n\e]133;C\a\e[3J");
        var commands = session.Commands();
        Assert.Equal("top", Assert.Single(commands).Text);
        Assert.True(commands[0].Running);
    }

    [Theory]
    [InlineData("\e[?1049h\e[H\e[2J\e[3J|app\e[2J|\e[?1049l")] // switching and clearing in one write (| between writes)
    [InlineData("\e[?1049h|\e[H\e[2J|app\e[3J|\e[2J\e[?1049l")] // and in separate ones
    public void AFullScreenAppClearingItsOwnScreenLeavesTheCommandsAlone(string app)
    {
        // Every scrollbar tick would go once a TUI had drawn, and never come back, if erases on the alternate screen
        // were checked against that screen, where no row is a prompt.
        using var session = WithFailures();
        session.WriteToScreen("\e]133;A\a$ \e]133;B\abtm\r\n\e]133;C\a");
        foreach (var write in app.Split('|'))
        {
            session.WriteToScreen(write);
        }

        session.WriteToScreen("\e]133;D;0\a\e]133;A\a$ \e]133;B\a");
        Assert.Equal(6, session.Commands().Count(c => c.Failed));
        Assert.Contains(session.Commands(), c => c.Text == "btm");
    }

    [Fact]
    public void AFullResetFromAFullScreenAppStillForgetsThem()
    {
        using var session = WithFailures();
        session.WriteToScreen("\e]133;A\a$ \e]133;B\abtm\r\n\e]133;C\a\e[?1049h\e[2J");
        session.WriteToScreen("\ec");
        Assert.DoesNotContain(session.Commands(), c => c.Failed);
    }

    private static Erased[] Feed(params string[] reads)
    {
        var watch = new EraseWatch();
        return reads.Select(r => watch.Seen(Encoding.ASCII.GetBytes(r))).ToArray();
    }

    [Fact]
    public void WatchFindsTheSequencesSplitAcrossReads()
    {
        Assert.Equal([Erased.Scrollback], Feed("abc\e[3Jdef"));
        Assert.Equal([Erased.None, Erased.Scrollback], Feed("abc\e[", "3J"));
        Assert.Equal([Erased.None, Erased.None, Erased.Scrollback], Feed("\e", "[3", "J"));
        Assert.Equal([Erased.None, Erased.Scrollback | Erased.Reset], Feed("x\e", "c"));
        Assert.Equal([Erased.Display | Erased.Scrollback], Feed("\e[H\e[2J\e[3J"));
        Assert.Equal([Erased.Display, Erased.Display], Feed("\e[2J", "\e[J"));
        Assert.Equal([Erased.None, Erased.Display], Feed("\e[2", "J"));
        Assert.Equal([Erased.None], Feed("\e[3K\e[31m\e[?2J\e[10;3H")); // erase in line, colors, DECSED, moves: not these
    }
}
