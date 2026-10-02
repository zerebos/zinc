using Zinc.Terminal;

namespace Zinc.Tests;

public class LinkTests
{
    [Theory]
    [InlineData("go to https://ghostty.org now", 10, "https://ghostty.org")]
    [InlineData("https://x.org/a.", 3, "https://x.org/a")]
    [InlineData("(see https://x.org/a)", 8, "https://x.org/a")]
    [InlineData("https://en.wikipedia.org/wiki/A_(b)", 5, "https://en.wikipedia.org/wiki/A_(b)")]
    [InlineData("\"http://q.io/?a=1&b=2\",", 5, "http://q.io/?a=1&b=2")]
    [InlineData("mailto:me@example.com", 0, "mailto:me@example.com")]
    [InlineData("file:///C:/Users/me/notes.txt", 9, "file:///C:/Users/me/notes.txt")]
    public void FindsUrls(string text, int index, string expected)
    {
        var found = UrlDetector.Find(text, index);
        Assert.NotNull(found);
        Assert.Equal(expected, text.Substring(found.Value.Start, found.Value.Length));
    }

    [Theory]
    [InlineData("go to https://ghostty.org now", 2)] // before the URL
    [InlineData("go to https://ghostty.org now", 26)] // after it
    [InlineData("https://x.org/a.", 15)] // the trimmed period
    [InlineData("no links here", 4)]
    public void NoUrlOutsideMatches(string text, int index)
    {
        Assert.Null(UrlDetector.Find(text, index));
    }

    [Theory]
    [InlineData("https://ghostty.org", LinkAction.Open)]
    [InlineData("HTTP://ghostty.org", LinkAction.Open)]
    [InlineData("mailto:me@example.com", LinkAction.Open)]
    [InlineData("file:///C:/Windows/notepad.exe", LinkAction.Reveal)]
    [InlineData("file://otherbox/home/me/x.txt", LinkAction.Reject)] // another machine
    [InlineData("file:///usr/bin/ls", LinkAction.Reject)] // a POSIX path with no known root
    [InlineData("ms-settings:privacy", LinkAction.Reject)]
    [InlineData("javascript:alert(1)", LinkAction.Reject)]
    [InlineData("not a uri", LinkAction.Reject)]
    public void ClassifiesLinks(string uri, LinkAction expected)
    {
        Assert.Equal(expected, LinkTarget.Classify(uri, Plain, out _));
    }

    private static readonly LinkContext Plain = new("Aetherion");

    // ls --hyperlink output as seen in each shell (the host is the Windows machine name).
    [Theory]
    [InlineData("file:///C:/Users/me/My%20Notes.txt", @"C:\Users\me\My Notes.txt")]
    [InlineData("file://localhost/C:/tmp/a.txt", @"C:\tmp\a.txt")]
    [InlineData("file://Aetherion/c/Users/Zack/dev", @"C:\Users\Zack\dev")] // Git Bash
    [InlineData("file://aetherion/d", @"D:\")]
    [InlineData("file:///cygdrive/c/x", @"C:\x")]
    public void WindowsAndMsysPaths(string uri, string expected)
    {
        Assert.Equal(LinkAction.Reveal, LinkTarget.Classify(uri, Plain, out var path));
        Assert.Equal(expected, path);
    }

    [Fact]
    public void MsysRootPaths()
    {
        var git = Plain with { PosixRoot = @"C:\Program Files\Git" };
        Assert.Equal(LinkAction.Reveal, LinkTarget.Classify("file://Aetherion/usr/bin/ls.exe", git, out var path));
        Assert.Equal(@"C:\Program Files\Git\usr\bin\ls.exe", path);
        Assert.Equal(LinkAction.Reveal, LinkTarget.Classify("file://Aetherion/c/x", git, out path));
        Assert.Equal(@"C:\x", path); // drives still win
    }

    [Theory]
    [InlineData("file://Aetherion/home/zack/My%20Dir", @"\\wsl.localhost\Fedora\home\zack\My Dir")]
    [InlineData("file://Aetherion/mnt/c/Users/Zack", @"C:\Users\Zack")]
    [InlineData("file://Aetherion/c/notadrive", @"\\wsl.localhost\Fedora\c\notadrive")]
    public void WslPaths(string uri, string expected)
    {
        Assert.Equal(LinkAction.Reveal, LinkTarget.Classify(uri, Plain with { WslDistro = "Fedora" }, out var path));
        Assert.Equal(expected, path);
    }
}
