using Phosphor.Terminal;

namespace Phosphor.Tests;

public class ShellDirectoryTests
{
    [Theory]
    [InlineData("file://Aetherion/home/zack/src", "/home/zack/src")]           // OSC 7 from zsh (WSL)
    [InlineData("file://host/home/zack/my%20dir", "/home/zack/my dir")]          // percent-encoded
    [InlineData("file:///C:/Users/Zack", @"C:\Users\Zack")]                       // OSC 7, Windows path
    [InlineData("file://localhost/c:/Users/Zack/", @"c:\Users\Zack\")]
    [InlineData(@"C:\Users\Zack", @"C:\Users\Zack")]                              // OSC 9;9 from PowerShell
    [InlineData("\"C:\\Program Files\"", @"C:\Program Files")]                     // quoted
    [InlineData("C:", @"C:\")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home", @"\\wsl.localhost\Ubuntu\home")]
    [InlineData("/usr/local", "/usr/local")]                                      // OSC 1337 CurrentDir
    public void DecodesReportedDirectories(string reported, string expected) =>
        Assert.Equal(expected, ShellDirectory.Parse(reported));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file://hostonly")]
    [InlineData("relative/path")]
    [InlineData("~")]
    public void IgnoresWhatIsNotAPath(string? reported) => Assert.Null(ShellDirectory.Parse(reported));

    private const string Wsl = "wsl.exe -d Fedora";
    private const string Pwsh = "pwsh.exe";
    private const string Home = @"C:\Users\Zack";

    [Theory]
    [InlineData("~/src", Wsl, "/home/zack", "~/src")]                     // WSL expands ~ itself
    [InlineData("~", Wsl, "/home/zack", "~")]
    [InlineData("/etc", Wsl, "/home/zack", "/etc")]
    [InlineData("src/../docs", Wsl, "/home/zack", "/home/zack/docs")]     // relative to where the shell is
    [InlineData(@"C:\Users\Zack\src", Wsl, "/home/zack", "/mnt/c/Users/Zack/src")]
    [InlineData("~", Pwsh, @"C:\x", Home)]
    [InlineData("~/src", Pwsh, @"C:\x", @"C:\Users\Zack\src")]
    [InlineData(@"D:\work", Pwsh, @"C:\x", @"D:\work")]
    [InlineData("sub", Pwsh, @"C:\x", @"C:\x\sub")]
    [InlineData("..", Pwsh, @"C:\x\y", @"C:\x")]
    [InlineData("/c/src", "bash.exe", "/c/Users", "/c/src")]               // MSYS: LaunchIn maps it
    [InlineData("\"C:\\Program Files\"", Pwsh, @"C:\x", @"C:\Program Files")]
    public void ResolvesTypedFolders(string typed, string command, string current, string expected) =>
        Assert.Equal(expected, ShellDirectory.ResolveTyped(typed, command, current, Home));

    [Theory]
    [InlineData("", Pwsh, @"C:\x")]
    [InlineData("   ", Pwsh, @"C:\x")]
    [InlineData("sub", Wsl, @"C:\x")] // relative, but WSL's folder is unknown
    public void RejectsWhatCannotBeAFolder(string typed, string command, string current) =>
        Assert.Null(ShellDirectory.ResolveTyped(typed, command, current, Home));

    [Fact]
    public void WslStartsInAHomeRelativeFolder() =>
        Assert.Equal("wsl.exe --cd \"~/src\" -d Fedora", ShellDirectory.LaunchIn(Wsl, "~/src")?.Command);

    [Fact]
    public void SessionKeepsWhatTheShellLastReported()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        Assert.Null(session.ReportedDirectory);

        session.WriteToScreen("\x1b]7;file://box/home/zack\x07");
        Assert.Equal("file://box/home/zack", session.ReportedDirectory);

        session.WriteToScreen("\x1b]9;9;C:\\Users\\Zack\x1b\\");
        Assert.Equal(@"C:\Users\Zack", ShellDirectory.Parse(session.ReportedDirectory));
    }

    [Fact]
    public void DirectoryChangesAreAnnouncedOnce()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var changes = 0;
        session.DirectoryChanged += () => changes++;

        session.WriteToScreen("\x1b]7;file://box/home/zack\x07");
        session.WriteToScreen("\x1b]7;file://box/home/zack\x07"); // every prompt re-reports it
        Assert.Equal(1, changes);

        session.WriteToScreen("\x1b]7;file://box/tmp\x07");
        Assert.Equal(2, changes);
    }
}
