using Zinc.Terminal;

namespace Zinc.Tests;

public class ShellTitleTests
{
    private const string Home = @"C:\Users\Zack";

    private static string Title(bool? atPrompt, string? folder = null, string? command = null, string program = "",
        bool programSinceCommand = false, bool shellTitles = true, bool programTitles = true, double ranSeconds = 5) =>
        ShellTitle.Choose(shellTitles, programTitles, atPrompt, folder, command, atPrompt == false ? Now.AddSeconds(-ranSeconds) : null,
            Now, program, programSinceCommand, Home);

    private static readonly double Leads = ShellTitle.CommandLeadsAfter.TotalSeconds;

    [Fact]
    public void AQuickCommandNeverNamesTheTab()
    {
        // For a quick command like ls or git status, the folder stays, as in a pane header, until the command has run
        // long enough to lead.
        Assert.Equal(@"~\dev\zinc", Title(false, @"C:\Users\Zack\dev\zinc", "git status", ranSeconds: Leads - 0.05));
        Assert.True(ShellTitle.ChoosesFolder(true, true, false, @"C:\Users\Zack\dev\zinc", "git status", Now.AddSeconds(-(Leads - 0.05)),
            Now, "", false));
        Assert.Equal("npm test", Title(false, @"C:\Users\Zack\dev\zinc", "npm test", ranSeconds: Leads + 0.01));
        Assert.False(ShellTitle.ChoosesFolder(true, true, false, @"C:\Users\Zack\dev\zinc", "npm test", Now.AddSeconds(-(Leads + 0.01)),
            Now, "", false));

        // A program's own title still names it at once.
        Assert.Equal("vim: notes.md", Title(false, @"C:\Users\Zack", "vim notes.md", "vim: notes.md", programSinceCommand: true, ranSeconds: 0.01));
        Assert.False(ShellTitle.ChoosesFolder(true, true, false, @"C:\Users\Zack", "vim notes.md", Now.AddSeconds(-0.01), Now,
            "vim: notes.md", true));
    }

    [Fact]
    public void FolderAtThePromptCommandWhileItRuns()
    {
        Assert.Equal(@"~\dev\zinc", Title(true, @"C:\Users\Zack\dev\zinc", program: "zn.exe"));
        Assert.Equal("git log --oneline", Title(false, @"C:\Users\Zack", "git log --oneline", "old title"));
    }

    [Fact]
    public void AProgramsOwnTitleWinsWhileItRuns()
    {
        Assert.Equal("vim: notes.md", Title(false, command: "vim notes.md", program: "vim: notes.md", programSinceCommand: true));
        // Set before the command (by the shell's prompt): the command's text instead.
        Assert.Equal("vim notes.md", Title(false, command: "vim notes.md", program: "zack@fedora:~"));
        Assert.Equal("vim notes.md", Title(false, command: "vim notes.md", program: "vim: x", programSinceCommand: true, programTitles: false));
    }

    [Fact]
    public void WhatsMissingFallsBack()
    {
        Assert.Equal("pwsh", Title(null, @"C:\x", "ls", "pwsh")); // no marks: the program's, as before
        Assert.Equal("pwsh", Title(true, null, null, "pwsh"));    // no folder reported yet
        Assert.Equal("pwsh", Title(false, null, null, "pwsh"));   // command text unknown
        Assert.Equal("", Title(null, program: "pwsh", programTitles: false));
        Assert.Equal("pwsh", Title(true, @"C:\x", program: "pwsh", shellTitles: false));
    }

    [Theory]
    [InlineData(@"C:\Users\Zack", "~")]
    [InlineData(@"c:\users\zack\src\", @"~\src")]
    [InlineData(@"C:\Users\Zack2", @"C:\Users\Zack2")]      // a sibling isn't home
    [InlineData("/c/Users/Zack/src", "~/src")]              // Git Bash, MSYS2
    [InlineData("/home/zack", "~")]                         // WSL
    [InlineData("/home/zack/dev/zinc", "~/dev/zinc")]
    [InlineData("/root/x", "~/x")]
    [InlineData("/homeless", "/homeless")]
    [InlineData("/etc/nginx", "/etc/nginx")]
    [InlineData(@"D:\work", @"D:\work")]
    public void HomeIsATilde(string path, string shown)
    {
        Assert.Equal(shown, ShellTitle.Folder(path, Home));
    }

    [Theory]
    [InlineData(@"~\dev\zinc", null)] // a shell's title, which is the folder itself
    [InlineData(@"C:\Users\Zack\dev\zinc - vim", null)] // a program's title that names the folder in full
    [InlineData("npm test", @"~\dev\zinc")]
    public void TheFolderAfterATitleIsShortAndNotSaidTwice(string title, string? after) =>
        Assert.Equal(after, ShellTitle.FolderAfter(title, @"C:\Users\Zack\dev\zinc", @"C:\Users\Zack"));

    [Fact]
    public void LongOrSeveralLineCommandsAreCut()
    {
        Assert.Equal("for x in 1 2 3; do", Title(false, command: "for x in 1 2 3; do\n  echo $x\ndone"));
        var long_ = new string('a', 120);
        Assert.Equal(80, Title(false, command: long_).Length);
        Assert.EndsWith("…", Title(false, command: long_));
    }

    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private static HeaderTitle Header(bool? atPrompt, string? folder = null, string? command = null, double ranSeconds = 5,
        string program = "", bool programSinceCommand = false, bool programTitles = true) =>
        ShellTitle.Header(programTitles, atPrompt, folder, command, atPrompt == false ? Now.AddSeconds(-ranSeconds) : null, Now,
            program, programSinceCommand, Home, "PowerShell");

    [Fact]
    public void HeaderLeadsWithTheFolderAtThePromptAndTheCommandWhileItRuns()
    {
        Assert.Equal(new HeaderTitle(@"~\dev\zinc", "PowerShell", LeadIsFolder: true), Header(true, @"C:\Users\Zack\dev\zinc"));
        Assert.Equal(new HeaderTitle("npm test", @"~\dev\zinc", DetailIsFolder: true), Header(false, @"C:\Users\Zack\dev\zinc", "npm test"));
    }

    [Fact]
    public void HeaderKeepsTheFolderUntilACommandHasRunLongEnough()
    {
        // git status, ls: never in the header.
        Assert.Equal(new HeaderTitle(@"~\dev\zinc", "PowerShell", LeadIsFolder: true),
            Header(false, @"C:\Users\Zack\dev\zinc", "git status", ranSeconds: Leads - 0.01));
        Assert.Equal(new HeaderTitle("npm test", @"~\dev\zinc", DetailIsFolder: true),
            Header(false, @"C:\Users\Zack\dev\zinc", "npm test", ranSeconds: Leads + 0.01));
        Assert.Equal(ShellTitle.CommandLeadsAfter - TimeSpan.FromMilliseconds(50), ShellTitle.UntilCommandLeads(Now.AddMilliseconds(-50), Now));
        Assert.Equal(TimeSpan.Zero, ShellTitle.UntilCommandLeads(Now.AddSeconds(-3), Now));
        Assert.Null(ShellTitle.UntilCommandLeads(null, Now));
    }

    [Fact]
    public void HeaderLetsAProgramsOwnTitleLeadAtOnce()
    {
        Assert.Equal(new HeaderTitle("vim: notes.md", @"~\dev", DetailIsFolder: true),
            Header(false, @"C:\Users\Zack\dev", "vim notes.md", ranSeconds: 0.01, program: "vim: notes.md", programSinceCommand: true));
        // A title the shell set before the command doesn't count; with program-titles off no program names it.
        Assert.Equal(new HeaderTitle("vim notes.md", null), Header(false, null, "vim notes.md", program: "zack@fedora:~"));
        Assert.Equal(new HeaderTitle("vim notes.md", null),
            Header(false, null, "vim notes.md", program: "vim: x", programSinceCommand: true, programTitles: false));
    }

    [Fact]
    public void HeaderFallsBackToTheProgramsTitleAndTheProfile()
    {
        // No marks (cmd without Clink): the program's title, then the profile.
        Assert.Equal(new HeaderTitle(@"C:\WINDOWS\SYSTEM32\cmd.exe", "PowerShell"), Header(null, @"C:\x", program: @"C:\WINDOWS\SYSTEM32\cmd.exe"));
        Assert.Equal(new HeaderTitle("PowerShell", null), Header(null)); // nothing at all: the profile once
        Assert.Equal(new HeaderTitle("pwsh", "PowerShell"), Header(true, program: "pwsh")); // no folder reported yet
        Assert.Equal(new HeaderTitle("PowerShell", null), Header(false, command: null)); // a command with no text
    }

    [Fact]
    public void HeaderSaysTheFolderOnce()
    {
        // A program's title that names the folder already, as written or in full: no quieter folder after it.
        Assert.Equal(new HeaderTitle(@"config.toml ~\dev\BetterDiscord\betterdiscord.app flow", null),
            Header(false, @"C:\Users\Zack\dev\BetterDiscord\betterdiscord.app", "flow config.toml",
                program: @"config.toml ~\dev\BetterDiscord\betterdiscord.app flow", programSinceCommand: true));
        Assert.Equal(new HeaderTitle(@"vim C:\Users\Zack\dev\zinc\notes.md", null),
            Header(false, @"C:\Users\Zack\dev\zinc", "vim notes.md", program: @"vim C:\Users\Zack\dev\zinc\notes.md", programSinceCommand: true));
        // Only the whole folder counts: its last name alone isn't enough.
        Assert.Equal(new HeaderTitle("zinc: build", @"~\dev\zinc", DetailIsFolder: true),
            Header(false, @"C:\Users\Zack\dev\zinc", "make", program: "zinc: build", programSinceCommand: true));
        // The profile likewise.
        Assert.Equal(new HeaderTitle("Windows PowerShell", null), Header(null, program: "Windows PowerShell"));
    }

    [Theory]
    [InlineData(true, true, true)] // at the prompt, a folder reported: the label is the folder
    [InlineData(true, false, false)] // a command runs (for longer than CommandLeadsAfter)
    [InlineData(true, null, false)] // no marks: the program's title
    [InlineData(false, true, false)] // shell-titles off
    public void TabLabelsKnowWhenTheyAreAFolder(bool shellTitles, bool? atPrompt, bool folder)
    {
        var started = atPrompt == false ? Now.AddSeconds(-5) : (DateTime?)null;
        Assert.Equal(folder, ShellTitle.ChoosesFolder(shellTitles, true, atPrompt, @"C:\Users\Zack\dev", "make", started, Now, "pwsh", false));
        Assert.False(ShellTitle.ChoosesFolder(shellTitles, true, atPrompt, null, "make", started, Now, "pwsh", false));
        if (folder)
        {
            Assert.Equal(@"~\dev", Title(atPrompt, @"C:\Users\Zack\dev", shellTitles: shellTitles));
        }
    }

    [Fact]
    public void FoldersShortenFromTheirStart()
    {
        Assert.Equal([@"…\BetterDiscord\betterdiscord.app", @"…\betterdiscord.app"],
            ShellTitle.FolderShortenings(@"~\dev\BetterDiscord\betterdiscord.app"));
        Assert.Equal(["…/x"], ShellTitle.FolderShortenings("/home/x"));
        Assert.Equal([@"…\share\x", @"…\x"], ShellTitle.FolderShortenings(@"\\server\share\x"));
        Assert.Equal([@"…\x"], ShellTitle.FolderShortenings(@"C:\x\"));
        foreach (var root in new[] { "~", @"C:\", "/" })
        {
            Assert.Empty(ShellTitle.FolderShortenings(root));
        }
    }

    [Fact]
    public void AFolderDropsOnlyWhatDoesntFit()
    {
        static double Chars(string text) => text.Length; // a character a pixel
        const string folder = @"~\dev\BetterDiscord\betterdiscord.app";
        Assert.Equal(folder, ShellTitle.FitFolder(folder, folder.Length, Chars));
        Assert.Equal(@"…\BetterDiscord\betterdiscord.app", ShellTitle.FitFolder(folder, 33, Chars));
        Assert.Equal(@"…\betterdiscord.app", ShellTitle.FitFolder(folder, 32, Chars));
        Assert.Equal(@"…\betterdiscord.app", ShellTitle.FitFolder(folder, 5, Chars)); // then trimmed at its end
        Assert.Equal("~", ShellTitle.FitFolder("~", 0, Chars));
    }
}
