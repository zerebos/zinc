using Zinc.Ghostty;
using Zinc.Terminal;

namespace Zinc.Tests;

public class ProgramNotificationTests
{
    private static (List<(string Title, string Body)> Notes, List<TerminalProgress> Progress) Watch(TerminalSession session)
    {
        var notes = new List<(string, string)>();
        var progress = new List<TerminalProgress>();
        session.Notified += (title, body) => notes.Add((title, body));
        session.ProgressChanged += p => progress.Add(p);
        return (notes, progress);
    }

    [Fact]
    public void ReadsBothNotificationForms()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var (notes, _) = Watch(session);
        session.WriteToScreen("\e]9;Build finished\a");              // iTerm2 / Windows Terminal
        session.WriteToScreen("\e]777;notify;Tests;3 failed\e\\");   // rxvt / Ghostty
        Assert.Equal([("", "Build finished"), ("Tests", "3 failed")], notes);
    }

    [Theory]
    [InlineData("\e]9;9;\"C:\\Users\\JDoe\"\e\\")] // the PowerShell prompt's folder report
    [InlineData("\e]9;4;1;50\a")]                   // progress, not a message
    public void ConEmuSequencesAreNotNotifications(string text)
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var (notes, _) = Watch(session);
        session.WriteToScreen(text);
        Assert.Empty(notes);
    }

    [Fact]
    public void FollowsProgress()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var (_, progress) = Watch(session);
        session.WriteToScreen("\e]9;4;1;25\a");
        session.WriteToScreen("\e]9;4;1;25\a"); // the same again: no news
        session.WriteToScreen("\e]9;4;3\a");
        session.WriteToScreen("\e]9;4;2;80\a");
        session.WriteToScreen("\e]9;4;0\a");
        Assert.Equal(
        [
            new TerminalProgress(TerminalProgressState.Set, 25),
            new TerminalProgress(TerminalProgressState.Indeterminate, -1),
            new TerminalProgress(TerminalProgressState.Error, 80),
            TerminalProgress.None,
        ], progress);
        Assert.False(session.Progress.Active);
    }
}
