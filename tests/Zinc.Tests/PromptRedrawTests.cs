using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>
/// After a resize the shell draws its prompt again, but the terminal has already rewrapped the old one, so without
/// clearing, stale copies pile up above the new one (powerlevel10k's full-width frame, on every narrowing resize).
/// libghostty clears the prompt on a resize when the prompt mark says the shell draws it again, and Zinc adds that to
/// marks that don't say either way (TerminalSession.WritePromptStart).
/// </summary>
public class PromptRedrawTests
{
    [Fact]
    public void AResizeClearsThePromptForTheShellToDrawAgain()
    {
        using var session = new TerminalSession(10, 5, TerminalTheme.Default);
        // A two-line prompt whose first line fills the width, like powerlevel10k's frame, with p10k's own marks.
        session.WriteToScreen("out\r\n\e]133;A\aPPPPPPPPPP\r\n> \e]133;B\als");
        session.Resize(5, 5, 8, 16);

        // All of it goes, the first line's wrapped part too (patches/ghostty), and the output above it stays.
        Assert.Equal("out", session.ReadText().TrimEnd());
    }

    [Fact]
    public void AShellThatDoesNotDrawItAgainKeepsItsPrompt()
    {
        using var session = new TerminalSession(10, 5, TerminalTheme.Default);
        session.WriteToScreen("out\r\n\e]133;A;redraw=0\a> \e]133;B\als");
        session.Resize(5, 5, 8, 16);
        Assert.Equal("out\n> ls", session.ReadText().TrimEnd());
    }
}
