using System.Text;
using Phosphor.Terminal;

namespace Phosphor.Tests;

public class ShellIntegrationTests
{
    private static List<ShellMark> ScanAll(Osc133Scanner scanner, string text)
    {
        var marks = new List<ShellMark>();
        ReadOnlySpan<byte> data = Encoding.UTF8.GetBytes(text);
        while (scanner.Next(data, out var mark) is var end and >= 0)
        {
            marks.Add(mark);
            data = data[end..];
        }

        return marks;
    }

    [Fact]
    public void FindsMarksWithEitherTerminator()
    {
        var marks = ScanAll(new Osc133Scanner(), "\e]133;A\a$ \e]133;B\e\\ls\r\n\e]133;C\aout\r\n\e]133;D;2\a");
        Assert.Equal([new('A') { Options = "" }, new('B'), new('C'), new('D', 2)], marks);
    }

    [Fact]
    public void FindsMarksSplitAcrossReads()
    {
        var scanner = new Osc133Scanner();
        var text = "x\e]133;D;127;aid=1\e\\y";
        var marks = new List<ShellMark>();
        foreach (var ch in text)
        {
            marks.AddRange(ScanAll(scanner, ch.ToString())); // one byte at a time
        }

        Assert.Equal([new ShellMark('D', 127)], marks);
    }

    [Theory]
    [InlineData("\e]7;file://host/tmp\a")]          // another OSC
    [InlineData("\e]1330;A\a")]                     // not 133
    [InlineData("\e]133A\a")]                       // malformed
    [InlineData("\e]133;\a")]                       // empty
    [InlineData("\e]133;A\u0018")]                  // cancelled (CAN)
    public void IgnoresWhatIsNotAMark(string text) => Assert.Empty(ScanAll(new Osc133Scanner(), text));

    [Fact]
    public void ReadsExitCodes()
    {
        Assert.Null(ShellMark.Parse("D")!.Value.ExitCode);
        Assert.Equal(0, ShellMark.Parse("D;0")!.Value.ExitCode);
        Assert.Equal(-1, ShellMark.Parse("D;-1")!.Value.ExitCode);
        Assert.Null(ShellMark.Parse("D;err")!.Value.ExitCode);
        Assert.Equal('A', ShellMark.Parse("A;k=i;aid=3")!.Value.Kind);
    }

    [Fact]
    public void TakesTheCommandLineWhenTheShellSendsIt()
    {
        Assert.Equal("git log -3 | grep \"fix\"", ShellMark.Parse("C;cmdline_url=git%20log%20-3%20%7C%20grep%20%22fix%22")!.Value.CommandLine);

        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\e]133;A\a> \e]133;B\astray   }\r\n\e]133;C;cmdline_url=ls%20-la\a");
        Assert.Equal("ls -la", session.Commands()[0].Text);
    }

    /// <summary>What a shell with integration prints for one command.</summary>
    private static string Command(string line, string output, int exit, string prompt = "$ ") =>
        $"\e]133;A\a{prompt}\e]133;B\a{line}\r\n\e]133;C\a{output}\e]133;D;{exit}\a";

    [Fact]
    public void TracksCommandsWithTheirTextOutputAndExit()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        session.Clock = () => now;
        var changes = 0;
        session.CommandsChanged += () => changes++;

        session.WriteToScreen("\e]133;A\a$ \e]133;B\aecho hi\r\n\e]133;C\a");
        now = now.AddSeconds(3);
        session.WriteToScreen("hi\r\n\e]133;D;0\a");
        session.WriteToScreen(Command("false", "", 1));
        session.WriteToScreen("\e]133;A\a$ \e]133;B\a"); // the next prompt, waiting

        var commands = session.Commands();
        Assert.Equal(3, commands.Count);
        Assert.Equal(("echo hi", 0L, 1L, 1L, 0), (commands[0].Text, commands[0].PromptRow, commands[0].OutputRow, commands[0].LastRow, commands[0].ExitCode));
        Assert.Equal(TimeSpan.FromSeconds(3), commands[0].Duration);
        Assert.False(commands[0].Failed);
        Assert.Equal("false", commands[1].Text);
        Assert.True(commands[1].Failed);
        Assert.Null(commands[2].OutputRow);
        Assert.False(commands[2].Running);
        Assert.True(changes >= 3);
    }

    [Fact]
    public void AnEmptyCommandLineIsNotAFailure()
    {
        // Shells report the last exit code at every prompt, also after an empty Enter (no C).
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\e]133;A\a$ \e]133;B\a\r\n\e]133;D;1\a\e]133;A\a$ ");
        Assert.False(session.Commands()[0].Failed);
    }

    [Fact]
    public void AnEmptyEnterThatPowerlevel10kMarksAsRunIsNotAFailure()
    {
        // p10k sends C (and D with the previous exit code) even when nothing was typed.
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\e]133;A\a❯ \e]133;B\a\r\n\e]133;C;\a\e]133;D;1\a\e]133;A\a❯ ");
        var empty = session.Commands()[0];
        Assert.False(empty.Ran);
        Assert.False(empty.Failed);
    }

    [Fact]
    public void ReadsTheCommandLineWithoutTheRightPrompt()
    {
        // zsh (p10k): left prompt, the right prompt at the far end (P;k=r), back to type.
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\e]133;A\a❯ \e]133;B\a\e[70G\e]133;P;k=r\a~/src\e]133;B\a\e[3Gls -la\r\n\e]133;C\a");
        Assert.Equal("ls -la", session.Commands()[0].Text);
    }

    [Fact]
    public void LeavesOutRightPromptDecorationMarkedAsInput()
    {
        // What powerlevel10k's two-line prompt sends (traced): the frame's corner is drawn after B,
        // so it reads as input.
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\e]133;A\a╰─ \e]133;B\a\e[K\e[73C─╯\e[75Dfalse\r\n\e]133;C;\a\e]133;D;1\a");
        session.WriteToScreen("\e]133;A\a╰─ \e]133;B\a\e[K\e[73C─╯\e[75D\r\n\e]133;C;\a\e]133;D;1\a\e]133;A\a");
        var commands = session.Commands();
        Assert.Equal("false", commands[0].Text);
        Assert.False(commands[1].Ran); // an empty Enter
    }

    [Fact]
    public void ReadsACommandOfSeveralLines()
    {
        // bash with a trailing "\": Phosphor's script marks the continuation prompt "> " (P;k=s ... B).
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\e]133;A\a$ \e]133;B\aecho one \\\r\n\e]133;P;k=s\a> \e]133;B\atwo \\\r\n\e]133;P;k=s\a> \e]133;B\athree\r\n\e]133;C\a");
        Assert.Equal("echo one \\ two \\ three", session.Commands()[0].Text);

        // A long line the terminal wrapped is still one line: no space where it wrapped.
        using var narrow = new TerminalSession(20, 24, TerminalTheme.Default);
        narrow.WriteToScreen("\e]133;A\a$ \e]133;B\aecho abcdefghijklmnopqrstuvwxyz\r\n\e]133;C\a");
        Assert.Equal("echo abcdefghijklmnopqrstuvwxyz", narrow.Commands()[0].Text);
    }

    [Fact]
    public void KnowsARunningCommand()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\e]133;A\a$ \e]133;B\asleep 60\r\n\e]133;C\a");
        var running = Assert.Single(session.Commands());
        Assert.True(running.Running);
        Assert.Equal("sleep 60", running.Text);
    }

    [Fact]
    public void IgnoresMarksOnTheAlternateScreen()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen("\e[?1049h\e]133;A\a$ \e[?1049l");
        Assert.Empty(session.Commands());
    }

    [Fact]
    public void JumpsBetweenPrompts()
    {
        using var session = new TerminalSession(80, 10, TerminalTheme.Default);
        var output = string.Concat(Enumerable.Range(1, 20).Select(i => $"line {i}\r\n"));
        for (var i = 0; i < 3; i++)
        {
            session.WriteToScreen(Command($"cmd{i}", output, 0));
        }

        session.WriteToScreen("\e]133;A\a$ ");
        var prompts = session.Commands().Select(c => c.PromptRow).ToList();
        Assert.Equal([0L, 21, 42, 63], prompts);

        Assert.True(session.JumpToPrompt(-1));
        Assert.Equal(42UL, session.GetScrollbar().Offset); // the last command's prompt on top
        Assert.True(session.JumpToPrompt(-1));
        Assert.Equal(21UL, session.GetScrollbar().Offset);
        Assert.True(session.JumpToPrompt(+1));
        Assert.Equal(42UL, session.GetScrollbar().Offset);
        Assert.True(session.JumpToPrompt(+1)); // the last prompt is on screen: back to the bottom
        var sb = session.GetScrollbar();
        Assert.Equal(sb.Total, sb.Offset + sb.Len);
    }

    [Fact]
    public void JumpingNeedsMarks()
    {
        using var session = new TerminalSession(80, 10, TerminalTheme.Default);
        session.WriteToScreen("no integration here\r\n");
        Assert.False(session.JumpToPrompt(-1));
    }

    [Fact]
    public void SelectsACommandsOutput()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.WriteToScreen(Command("ls", "a.txt\r\nb.txt\r\n\r\n", 0));
        session.WriteToScreen("\e]133;A\a$ ");

        var command = session.CommandInView();
        Assert.Equal("ls", command?.Text);
        Assert.True(session.SelectOutput(command!));
        Assert.Equal("a.txt\nb.txt", session.GetSelectionText()?.Replace("\r\n", "\n"));
    }
}
