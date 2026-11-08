using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>
/// A tester's report (2026-10-03): with a prompt drawn by Write-Host (so PowerShell's marks come after its text), the
/// cursor sat on the line below the prompt. libghostty starts a fresh line for a prompt mark that arrives mid-line,
/// which ConPTY never hears of.
/// </summary>
public class PromptFreshLineTests
{
    [Fact]
    public void AMidLinePromptMarkDoesNotMoveTheCursor()
    {
        using var session = new TerminalSession(40, 8, TerminalTheme.Default);
        session.WriteToScreen("[C:\\Users\\JDoe]>\e]133;A;aid=42\a\e]9;9;\"C:\\Users\\JDoe\"\e\\ \e]133;B\a");
        Assert.Equal(0, session.CursorRow);
        Assert.Equal("[C:\\Users\\JDoe]>", session.ReadText().TrimEnd());

        // Zinc's own tracking still saw a prompt (with its options) and marks the command that follows.
        session.WriteToScreen("ls\r\n\e]133;C\aout\r\n\e]133;D;0\a");
        var command = Assert.Single(session.Commands());
        Assert.Equal(0, command.PromptRow);
        Assert.Equal(0, command.ExitCode);
    }

    [Fact]
    public void AMarkAtTheLineStartIsSentAsIs()
    {
        using var session = new TerminalSession(40, 8, TerminalTheme.Default);
        session.WriteToScreen("old output\r\n\e]133;A;click_events=1\a$ \e]133;B\a");
        Assert.Equal(1, session.CursorRow);
        Assert.Equal("old output\n$", session.ReadText().TrimEnd());
    }

    [Fact]
    public void NewCommandMarksToo()
    {
        using var session = new TerminalSession(40, 8, TerminalTheme.Default);
        session.WriteToScreen("no newline at the end\e]133;N\a> ");
        Assert.Equal(0, session.CursorRow);
    }
}
