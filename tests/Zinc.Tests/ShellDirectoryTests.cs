using Zinc.Terminal;

namespace Zinc.Tests;

public class ShellDirectoryTests
{
    [Theory]
    [InlineData("file://localhost/home/jdoe/my%20dir", "/home/jdoe/my dir")]     // percent-encoded
    [InlineData("file://127.0.0.1/srv", "/srv")]
    [InlineData("file:///C:/Users/JDoe", @"C:\Users\JDoe")]                       // OSC 7, Windows path
    [InlineData("file://localhost/c:/Users/JDoe/", @"c:\Users\JDoe\")]
    [InlineData(@"C:\Users\JDoe", @"C:\Users\JDoe")]                              // OSC 9;9 from PowerShell
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
    public void ThisPcByNameIsLocal()
    {
        // OSC 7 from zsh in WSL or Git Bash: the computer name, as the shell spells it.
        Assert.Equal("/home/jdoe/src", ShellDirectory.Parse($"file://{Environment.MachineName.ToLowerInvariant()}/home/jdoe/src"));
        Assert.Equal(@"C:\Users", ShellDirectory.Parse($"file://{System.Net.Dns.GetHostName()}/C:/Users"));
    }

    [Theory]
    [InlineData("file://build-server/c/x")]           // ssh: its /c/x isn't C:\x
    [InlineData("file://build-server/home/jdoe")]
    [InlineData("file://build-server.example.com/C:/x")]
    [InlineData("file://localhost.localdomain/srv")]  // a fresh RHEL/Rocky server's name
    [InlineData("file://10.0.0.5/srv")]
    public void IgnoresOtherMachines(string reported)
    {
        Assert.Null(ShellDirectory.Parse(reported));
        Assert.NotNull(ShellDirectory.RemoteHost(reported));
    }

    [Theory]
    [InlineData("file://localhost/x")]
    [InlineData("file:///x")]
    [InlineData(@"C:\x")]   // OSC 9;9 names no host: taken as local
    [InlineData(null)]
    public void LocalReportsHaveNoRemoteHost(string? reported) => Assert.Null(ShellDirectory.RemoteHost(reported));

    [Fact]
    public void ADistrosOwnHostNameIsLocalOnceKnown()
    {
        // LocalHosts is process-wide: a random name no other test (or real host) uses keeps this one to itself.
        var name = "zinc-test-distro-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Null(ShellDirectory.Parse($"file://{name}/home/jdoe"));
        LocalHosts.Add(WslConf.Hostname($"[network]\nhostname = {name}\n"));
        Assert.Equal("/home/jdoe", ShellDirectory.Parse($"file://{name.ToUpperInvariant()}/home/jdoe"));
        Assert.Equal(LinkAction.Reveal, LinkTarget.Classify($"file://{name}/home/jdoe/a.txt", new LinkContext("PC", WslDistro: "Ubuntu"), out _));
    }

    private const string Wsl = "wsl.exe -d Fedora";
    private const string Pwsh = "pwsh.exe";
    private const string Home = @"C:\Users\JDoe";

    [Theory]
    [InlineData("~/src", Wsl, "/home/jdoe", "~/src")]                     // WSL expands ~ itself
    [InlineData("~", Wsl, "/home/jdoe", "~")]
    [InlineData("/etc", Wsl, "/home/jdoe", "/etc")]
    [InlineData("src/../docs", Wsl, "/home/jdoe", "/home/jdoe/docs")]     // relative to where the shell is
    [InlineData(@"C:\Users\JDoe\src", Wsl, "/home/jdoe", "/mnt/c/Users/JDoe/src")]
    [InlineData("~", Pwsh, @"C:\x", Home)]
    [InlineData("~/src", Pwsh, @"C:\x", @"C:\Users\JDoe\src")]
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

        session.WriteToScreen("\x1b]7;file://localhost/home/jdoe\x07");
        Assert.Equal("file://localhost/home/jdoe", session.ReportedDirectory);

        session.WriteToScreen("\x1b]9;9;C:\\Users\\JDoe\x1b\\");
        Assert.Equal(@"C:\Users\JDoe", ShellDirectory.Parse(session.ReportedDirectory));
    }

    [Fact]
    public void DirectoryChangesAreAnnouncedOnce()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var changes = 0;
        session.DirectoryChanged += () => changes++;

        session.WriteToScreen("\x1b]7;file://localhost/home/jdoe\x07");
        session.WriteToScreen("\x1b]7;file://localhost/home/jdoe\x07"); // every prompt re-reports it
        Assert.Equal(1, changes);

        session.WriteToScreen("\x1b]7;file://localhost/tmp\x07");
        Assert.Equal(2, changes);
    }

    [Fact]
    public void ARemoteReportKeepsTheLastLocalFolder()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var changes = 0;
        session.DirectoryChanged += () => changes++;

        session.WriteToScreen("\x1b]7;file://localhost/home/jdoe/src\x07");
        session.WriteToScreen("\x1b]7;file://build-server/c/x\x07"); // ssh build-server
        Assert.Equal("file://localhost/home/jdoe/src", session.ReportedDirectory);
        Assert.Equal("build-server", session.RemoteHost);
        Assert.Equal(2, changes); // what the tab shows changes

        session.WriteToScreen("\x1b]7;file://build-server/c/y\x07"); // moving around over there: nothing new here
        Assert.Equal(2, changes);

        session.WriteToScreen("\x1b]7;file://localhost/home/jdoe/src\x07"); // back
        Assert.Null(session.RemoteHost);
        Assert.Equal(3, changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackAtTheLocalShellsPromptSshHasEnded(bool remoteMarks)
    {
        // zsh reports its folder only when it changes: after ssh, the same folder isn't reported again.
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var changes = 0;
        session.DirectoryChanged += () => changes++;
        const string local = "\x1b]133;A;aid=1\x07$ \x1b]133;B\x07";

        session.WriteToScreen("\x1b]7;file://localhost/home/jdoe/src\x07" + local + "ssh box\r\n\x1b]133;C\x07");
        session.WriteToScreen("\x1b]7;file://box/home/jdoe\x07");
        if (remoteMarks)
        {
            // Its prompt isn't the local shell's: still over there.
            session.WriteToScreen("\x1b]133;A;aid=77\x07box$ \x1b]133;B\x07");
            Assert.Equal("box", session.RemoteHost);
            session.WriteToScreen("exit\r\n\x1b]133;C\x07\x1b]133;D;0\x07");
        }

        Assert.Equal("box", session.RemoteHost);
        Assert.Equal(2, changes);

        session.WriteToScreen("logout\r\n\x1b]133;D;0\x07" + local);
        Assert.Null(session.RemoteHost);
        Assert.Equal("file://localhost/home/jdoe/src", session.ReportedDirectory);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void AReportIgnoredTooEarlyIsTakenOnceItsHostIsKnown()
    {
        // LocalHosts is process-wide: a random name no other test (or real host) uses keeps this one to itself.
        var name = "zinc-test-distro-" + Guid.NewGuid().ToString("N")[..8];
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var changes = 0;
        session.DirectoryChanged += () => changes++;

        session.WriteToScreen($"\x1b]7;file://{name}/home/jdoe\x07");
        Assert.Equal(1, changes); // taken as another machine for now
        session.RecheckReportedDirectory(); // still unknown: nothing
        Assert.Null(session.ReportedDirectory);
        Assert.Equal(1, changes);

        LocalHosts.Add(name);
        session.RecheckReportedDirectory();
        Assert.Equal($"file://{name}/home/jdoe", session.ReportedDirectory);
        Assert.Null(session.RemoteHost);
        Assert.Equal(2, changes);
    }
}
