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
}
