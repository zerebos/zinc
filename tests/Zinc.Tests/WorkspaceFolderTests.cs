using Zinc.Terminal;

namespace Zinc.Tests;

public class WorkspaceFolderTests
{
    private const string Fedora = "wsl.exe --distribution-id {abc}";
    private const string Pwsh = "pwsh.exe";
    private const string GitBash = "\"C:\\Program Files\\Git\\bin\\bash.exe\" --login -i";

    [Theory]
    [InlineData("/home/zack/src/ghostty", "ghostty")]
    [InlineData(@"C:\Users\Zack\dev\zinc\", "zinc")]
    [InlineData("~", "~")]
    [InlineData(@"C:\", @"C:")]
    public void ItsNameIsTheLastFolder(string path, string name) =>
        Assert.Equal(name, new WorkspaceFolder(path).Name);

    [Fact]
    public void ADistrosFolderOpensThereInWslAndThroughItsShareElsewhere()
    {
        var folder = new WorkspaceFolder("/home/zack/src", "Fedora");
        Assert.Equal(("wsl.exe --cd \"/home/zack/src\" --distribution-id {abc}", (string?)null), folder.LaunchFor(Fedora, "Fedora"));
        Assert.Equal((Pwsh, @"\\wsl.localhost\Fedora\home\zack\src"), folder.LaunchFor(Pwsh, null));
        Assert.Null(folder.LaunchFor("wsl.exe -d Ubuntu", "Ubuntu")); // another distro's: its usual folder
        Assert.Null(folder.LaunchFor("cmd.exe", null)); // cmd can't start in a share
    }

    [Fact]
    public void ADistrosViewOfAWindowsDriveIsThatWindowsFolderElsewhere()
    {
        var folder = new WorkspaceFolder("/mnt/c/Users/Zack/dev", "Ubuntu");
        Assert.Equal(("cmd.exe", @"C:\Users\Zack\dev"), folder.LaunchFor("cmd.exe", null));
        Assert.Equal((Pwsh, @"C:\Users\Zack\dev"), folder.LaunchFor(Pwsh, null));
        Assert.Equal((GitBash, @"C:\"), new WorkspaceFolder("/mnt/c", "Ubuntu").LaunchFor(GitBash, null));
        Assert.Equal(("wsl.exe --cd \"/mnt/c/Users/Zack/dev\" --distribution-id {abc}", (string?)null), folder.LaunchFor(Fedora, "Fedora"));
        // The distro's own shells keep its path (a custom mount root would make /mnt/c an ordinary folder).
        Assert.Equal(("wsl.exe --cd \"/mnt/c/Users/Zack/dev\" -d Ubuntu", (string?)null), folder.LaunchFor("wsl.exe -d Ubuntu", "Ubuntu"));
    }

    [Fact]
    public void AWindowsFolderOpensEverywhere()
    {
        var folder = new WorkspaceFolder(@"C:\Users\Zack\dev");
        Assert.Equal((Pwsh, @"C:\Users\Zack\dev"), folder.LaunchFor(Pwsh, null));
        Assert.Equal((GitBash, @"C:\Users\Zack\dev"), folder.LaunchFor(GitBash, null));
        Assert.Equal(("wsl.exe --cd \"/mnt/c/Users/Zack/dev\" --distribution-id {abc}", (string?)null), folder.LaunchFor(Fedora, "Fedora"));
    }

    [Fact]
    public void MsysPathsAndHomeResolveForWindowsShells()
    {
        Assert.Equal((GitBash, @"C:\src"), new WorkspaceFolder("/c/src").LaunchFor(GitBash, null));
        Assert.NotNull(new WorkspaceFolder("~").LaunchFor(Pwsh, null)?.WorkingDirectory);
        Assert.Null(new WorkspaceFolder("~", "Fedora").LaunchFor(Pwsh, null)); // WSL's home isn't known from Windows
    }

    [Theory]
    [InlineData(@"\\wsl.localhost\Fedora\home\zack", "Fedora", "/home/zack", "Fedora")]
    [InlineData(@"\\wsl$\fedora\etc", "Fedora", "/etc", "Fedora")]
    [InlineData(@"\\wsl.localhost\Fedora", "Fedora", "/", "Fedora")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home", "Fedora", @"\\wsl.localhost\Ubuntu\home", null)] // another distro's: a Windows path
    [InlineData(@"\\wsl.localhost\FedoraX\home", "Fedora", @"\\wsl.localhost\FedoraX\home", null)]
    [InlineData(@"C:\dev", "Fedora", @"C:\dev", null)]
    [InlineData(@"C:\dev", null, @"C:\dev", null)]
    public void AFolderChosenInWindowsIsTheDistrosOwnOnItsShare(string chosen, string? distro, string path, string? owner)
    {
        var folder = WorkspaceFolder.FromWindows(chosen, distro);
        Assert.Equal((path, owner), (folder.Path, folder.WslDistro));
    }

    [Fact]
    public void RecentFoldersOfferWhatAProfileCanReach()
    {
        var now = DateTimeOffset.UtcNow;
        var recent = new RecentFolders();
        recent.Note(new RecentFolder("/home/zack/a", "wsl:Fedora", "Fedora", now));
        recent.Note(new RecentFolder(@"C:\dev", "pwsh", null, now));
        recent.Note(new RecentFolder("/home/u/b", "wsl:Ubuntu", "Ubuntu", now));
        recent.Note(new RecentFolder("/home/zack/a", "wsl:Fedora", "Fedora", now.AddMinutes(1))); // again: moves to the top

        Assert.Equal(["/home/zack/a", @"C:\dev"], recent.For("wsl:Fedora", "Fedora").Select(f => f.Path));
        Assert.Equal([@"C:\dev"], recent.For("pwsh", null).Select(f => f.Path));
        Assert.Equal(3, recent.All.Count);
        Assert.Equal("/home/zack/a", recent.All[0].Path);
    }
}
