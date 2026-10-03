using Zinc.Terminal;

namespace Zinc.Tests;

public class ShellTitleTests
{
    private const string Home = @"C:\Users\Zack";

    private static string Title(bool? atPrompt, string? folder = null, string? command = null, string program = "",
        bool programSinceCommand = false, bool shellTitles = true, bool programTitles = true) =>
        ShellTitle.Choose(shellTitles, programTitles, atPrompt, folder, command, program, programSinceCommand, Home);

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

    [Fact]
    public void LongOrSeveralLineCommandsAreCut()
    {
        Assert.Equal("for x in 1 2 3; do", Title(false, command: "for x in 1 2 3; do\n  echo $x\ndone"));
        var long_ = new string('a', 120);
        Assert.Equal(80, Title(false, command: long_).Length);
        Assert.EndsWith("…", Title(false, command: long_));
    }
}
