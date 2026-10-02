using Zinc.Terminal;

namespace Zinc.Tests;

public class DroppedPathsTests
{
    private static readonly LinkContext Windows = new("PC");
    private static readonly LinkContext Wsl = new("PC", WslDistro: "Ubuntu");
    private static readonly LinkContext GitBash = new("PC", PosixRoot: @"C:\Program Files\Git");

    [Theory]
    [InlineData(@"C:\Users\Zack\notes.txt", "/mnt/c/Users/Zack/notes.txt")]
    [InlineData(@"D:\", "/mnt/d/")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\zack\src", "/home/zack/src")] // its own files
    [InlineData(@"\\wsl$\ubuntu\etc\hosts", "/etc/hosts")]
    [InlineData(@"\\wsl.localhost\Fedora\home\zack", @"\\wsl.localhost\Fedora\home\zack")] // another distro's: not mounted
    public void WslSeesDrivesUnderMnt(string path, string expected) =>
        Assert.Equal(expected, DroppedPaths.Translate(path, Wsl));

    [Theory]
    [InlineData(@"C:\Users\Zack\notes.txt", "/c/Users/Zack/notes.txt")]
    [InlineData(@"C:\Program Files\Git\usr\bin\ls.exe", "/usr/bin/ls.exe")] // under its root
    [InlineData(@"C:\Program Files\Git", "/")]
    [InlineData(@"\\server\share\f.txt", "//server/share/f.txt")]
    public void GitBashSeesDrivesAtTheRoot(string path, string expected) =>
        Assert.Equal(expected, DroppedPaths.Translate(path, GitBash));

    [Fact]
    public void WindowsShellsGetWindowsPaths() =>
        Assert.Equal(@"C:\a b\c.txt", DroppedPaths.Translate(@"C:\a b\c.txt", Windows));

    [Theory]
    [InlineData("/mnt/c/plain/path.txt", PathQuoting.Posix, "/mnt/c/plain/path.txt")]
    [InlineData("/mnt/c/My Files/it's.txt", PathQuoting.Posix, @"'/mnt/c/My Files/it'\''s.txt'")]
    [InlineData("/c/$HOME/*.txt", PathQuoting.Posix, "'/c/$HOME/*.txt'")]
    [InlineData(@"C:\plain\path.txt", PathQuoting.PowerShell, @"C:\plain\path.txt")]
    [InlineData(@"C:\Program Files\it's $x.txt", PathQuoting.PowerShell, @"'C:\Program Files\it''s $x.txt'")]
    [InlineData(@"C:\a,b.txt", PathQuoting.PowerShell, @"'C:\a,b.txt'")] // , builds an array
    [InlineData(@"C:\Program Files\a&b.txt", PathQuoting.Windows, "\"C:\\Program Files\\a&b.txt\"")]
    public void QuotedOnlyWhereTheShellWouldSplitOrExpand(string path, PathQuoting quoting, string expected) =>
        Assert.Equal(expected, DroppedPaths.Quote(path, quoting));

    [Fact]
    public void SeveralFilesAreSeparatedBySpaces() =>
        Assert.Equal("/mnt/c/a.txt '/mnt/c/b c.txt'",
            DroppedPaths.Format([@"C:\a.txt", @"C:\b c.txt", ""], Wsl, PathQuoting.Posix));
}
