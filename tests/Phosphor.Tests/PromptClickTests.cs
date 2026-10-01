using System.Text;
using Phosphor.Terminal;

namespace Phosphor.Tests;

/// <summary>Click to move the cursor in a shell's command line, through libghostty's own prompt marking.</summary>
public class PromptClickTests
{
    private const string Esc = "\x1b";

    /// <summary>A prompt "> " (with <paramref name="options"/> on its A mark), then <paramref name="input"/> typed after B.</summary>
    private static TerminalSession Prompt(string input, string options = ";cl=line", ushort cols = 40)
    {
        var session = new TerminalSession(cols, 6, TerminalTheme.Default);
        session.WriteToScreen($"{Esc}]133;A{options}\a> {Esc}]133;B\a{input}");
        return session;
    }

    private static string? Click(TerminalSession session, int col, int row, bool assumeArrows = false) =>
        session.ClickToMove(col, row, assumeArrows) is { } bytes ? Encoding.ASCII.GetString(bytes) : null;

    private static string Left(int n) => string.Concat(Enumerable.Repeat($"{Esc}[D", n));

    private static string Right(int n) => string.Concat(Enumerable.Repeat($"{Esc}[C", n));

    [Theory]
    [InlineData("cl=line", PromptClickMode.Arrows)]
    [InlineData("cl=m", PromptClickMode.Arrows)]
    [InlineData("click_events=1", PromptClickMode.Events)]
    [InlineData("click_events=2", PromptClickMode.EventsRelative)]
    [InlineData("k=i;click_events=1", PromptClickMode.Events)]
    [InlineData("cl=line;click_events=1", PromptClickMode.Events)] // click_events wins, as in Ghostty
    [InlineData("aid=12", null)]
    [InlineData("cl=nonsense", null)]
    public void PromptMarksAnnounceHowClicksMove(string options, PromptClickMode? mode) =>
        Assert.Equal(mode, PromptClick.Announced(options.Split(';')));

    [Fact]
    public void AClickBeforeTheCursorPressesLeft()
    {
        using var session = Prompt("echo hello"); // input at columns 2..11, cursor at 12
        Assert.Equal(Left(10), Click(session, 2, 0)); // on the "e" of echo
        Assert.Equal(Left(5), Click(session, 7, 0)); // on the "h" of hello
    }

    [Fact]
    public void AClickOnThePromptGoesToTheStartOfTheInput()
    {
        using var session = Prompt("echo hello");
        Assert.Equal(Left(10), Click(session, 0, 0));
    }

    [Fact]
    public void AClickAfterTheCursorPressesRightAndPastTheEndGoesToTheEnd()
    {
        using var session = Prompt("echo hello");
        session.WriteToScreen($"{Esc}[5D"); // the shell moved the cursor onto the "h" of hello (column 7)
        Assert.Equal(Right(2), Click(session, 9, 0));
        Assert.Equal(Right(5), Click(session, 30, 0)); // past the text: after its last character
        Assert.Equal(Right(5), Click(session, 5, 3)); // below it, too
    }

    [Fact]
    public void AtTheEndAlreadyAClickPastItSendsNothing()
    {
        using var session = Prompt("echo hello");
        Assert.Equal("", Click(session, 30, 0)); // handled: nothing to move
    }

    [Fact]
    public void BlanksALineEditorWroteAfterTheTextDoNotCount()
    {
        // zsh erases an autosuggestion by writing spaces, which land as input cells.
        using var session = Prompt("echo hi" + new string(' ', 10) + $"{Esc}[10D"); // cursor back after "hi" (column 9)
        Assert.Equal("", Click(session, 14, 0)); // on the blanks: already at the end
        Assert.Equal("", Click(session, 30, 0));

        session.WriteToScreen($"{Esc}[7D"); // onto the "e" (column 2)
        Assert.Equal(Right(7), Click(session, 14, 0)); // to just after "hi", not into the blanks
        Assert.Equal(Right(5), Click(session, 7, 0)); // a typed space counts: onto the "h" of hi
    }

    [Fact]
    public void DecorationAfterTheInputMarkIsNotText()
    {
        // powerlevel10k draws its frame's corner at the far right after marking where typing starts.
        using var session = new TerminalSession(60, 6, TerminalTheme.Default);
        session.WriteToScreen($"{Esc}]133;A\a> {Esc}]133;B\a{Esc}[55C─╯\r{Esc}[2C");
        session.WriteToScreen("echo hi"); // typed: the cursor is after "hi" (column 9)
        Assert.Equal("", Click(session, 58, 0, assumeArrows: true)); // on the corner: already at the end
        Assert.Equal("", Click(session, 40, 0, assumeArrows: true));

        session.WriteToScreen($"{Esc}[7D"); // onto the "e"
        Assert.Equal(Right(7), Click(session, 40, 0, assumeArrows: true));
    }

    [Fact]
    public void AWideCharacterIsOnePress()
    {
        using var session = Prompt("日本語"); // columns 2-3, 4-5, 6-7; cursor at 8
        Assert.Equal(Left(3), Click(session, 2, 0));
        Assert.Equal(Left(3), Click(session, 3, 0)); // the second half of 日 is 日
        Assert.Equal(Left(2), Click(session, 4, 0));
    }

    [Fact]
    public void ALineThatWrapsIsOneLine()
    {
        using var session = Prompt("abcdefghijklmn", cols: 10); // "> abcdefgh" / "ijklmn", cursor at (6, 1)
        Assert.Equal(Left(14), Click(session, 2, 0)); // on "a", the row above
        Assert.Equal(Left(2), Click(session, 4, 1)); // on "m"
    }

    [Fact]
    public void ApplicationCursorKeysAreRespected()
    {
        using var session = Prompt("ab");
        session.WriteToScreen($"{Esc}[?1h");
        Assert.Equal($"{Esc}OD{Esc}OD", Click(session, 2, 0));
    }

    [Fact]
    public void ClickEventsSendAClick()
    {
        using var session = Prompt("echo hi", ";click_events=1");
        Assert.Equal($"{Esc}[<0;5;1M{Esc}[<0;5;1m", Click(session, 4, 0));
        Assert.Equal($"{Esc}[<0;3;1M{Esc}[<0;3;1m", Click(session, 0, 0)); // on the prompt: the start of the input
        Assert.Equal($"{Esc}[<0;10;1M{Esc}[<0;10;1m", Click(session, 30, 0)); // past the text: just after it

        using var relative = new TerminalSession(40, 6, TerminalTheme.Default);
        relative.WriteToScreen($"output\r\n{Esc}]133;A;click_events=2\a> {Esc}]133;B\aecho hi");
        Assert.Equal($"{Esc}[<0;5;1M{Esc}[<0;5;1m", Click(relative, 4, 1)); // the prompt's first row is row 1
    }

    [Fact]
    public void WithoutAnAnnouncementOnlyAMarkedInputGetsArrows()
    {
        using var session = Prompt("echo hi", options: "");
        Assert.Null(Click(session, 2, 0));
        Assert.Equal(Left(7), Click(session, 2, 0, assumeArrows: true));
    }

    [Fact]
    public void NotWhileACommandRunsOrInAFullScreenProgram()
    {
        using var session = Prompt("sleep 5");
        session.WriteToScreen($"\r\n{Esc}]133;C\a");
        Assert.Null(Click(session, 2, 0));

        using var vim = Prompt("vim");
        vim.WriteToScreen($"{Esc}[?1049h");
        Assert.Null(Click(vim, 2, 0));
    }

    [Fact]
    public void NotAboveThePrompt()
    {
        using var session = new TerminalSession(40, 6, TerminalTheme.Default);
        session.WriteToScreen($"earlier output\r\n{Esc}]133;A;cl=line\a> {Esc}]133;B\aecho hi");
        Assert.Null(Click(session, 3, 0));
        Assert.Equal(Left(7), Click(session, 2, 1));
    }
}
