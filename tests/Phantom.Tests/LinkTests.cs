using Phantom.Terminal;

namespace Phantom.Tests;

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
    [InlineData("file://fedora/home/me/x.txt", LinkAction.Reject)] // another host (e.g. WSL's ls --hyperlink)
    [InlineData("ms-settings:privacy", LinkAction.Reject)]
    [InlineData("javascript:alert(1)", LinkAction.Reject)]
    [InlineData("not a uri", LinkAction.Reject)]
    public void ClassifiesLinks(string uri, LinkAction expected)
    {
        Assert.Equal(expected, LinkTarget.Classify(uri, out _));
    }

    [Fact]
    public void FileLinksRevealTheLocalPath()
    {
        Assert.Equal(LinkAction.Reveal, LinkTarget.Classify("file:///C:/Users/me/My%20Notes.txt", out var path));
        Assert.Equal(@"C:\Users\me\My Notes.txt", path);
        Assert.Equal(LinkAction.Reveal, LinkTarget.Classify("file://localhost/C:/tmp/a.txt", out path));
        Assert.Equal(@"C:\tmp\a.txt", path);
    }
}
