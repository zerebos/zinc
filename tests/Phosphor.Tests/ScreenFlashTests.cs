using System.Text;
using Phosphor.Terminal;

namespace Phosphor.Tests;

public class ScreenFlashTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    [Theory]
    [InlineData("plain text\r\n", false)]
    [InlineData("\u001b[32mgreen\u001b[0m \u001b[?25h \u001b[?1049l", false)] // other sequences, one sharing the prefix
    [InlineData("\u001b[?5h", true)]
    [InlineData("before \u001b[?5l after", true)]
    [InlineData("ends partway \u001b[?", true)] // may finish in the next read
    [InlineData("ends with \u001b", true)]
    [InlineData("\u001b[?50h", false)]
    public void ConcernsOnlyReadsTheFilterCouldChange(string text, bool concerns)
    {
        Assert.Equal(concerns, ScreenFlashFilter.Concerns(B(text)));

        // And where it says no, the filter would indeed have given the read back unchanged.
        if (!concerns)
        {
            var filter = new ScreenFlashFilter();
            Assert.Equal(text, S(filter.Filter(B(text), 0, out var flashes)));
            Assert.Equal(0, flashes);
            Assert.False(filter.Waiting);
        }
    }

    [Fact]
    public void OnAndStraightOffIsAFlashAndDisappears()
    {
        var filter = new ScreenFlashFilter();
        var output = filter.Filter(B("$ \u001b[?5h\u001b[?5l\r\nnext"), 0, out var flashes);
        Assert.Equal("$ \r\nnext", S(output));
        Assert.Equal(1, flashes);
        Assert.False(filter.Waiting);
    }

    [Fact]
    public void AFlashSplitAcrossReadsIsCaughtToo()
    {
        var filter = new ScreenFlashFilter();
        var first = filter.Filter(B("a\u001b[?"), 0, out var f1);
        var second = filter.Filter(B("5h"), 10, out var f2);
        var third = filter.Filter(B("\u001b[?5lb"), 110, out var f3); // readline's 100 ms later
        Assert.Equal(("a", "", "b"), (S(first), S(second), S(third)));
        Assert.Equal(1, f1 + f2 + f3);
    }

    [Fact]
    public void ReverseVideoThatStaysIsAppliedOnceTheWaitIsOver()
    {
        var filter = new ScreenFlashFilter();
        Assert.Equal("x", S(filter.Filter(B("x\u001b[?5h"), 0, out _)));
        Assert.Empty(filter.TakeExpired(100)); // could still be a flash
        Assert.Equal("\u001b[?5h", S(filter.TakeExpired(ScreenFlashFilter.FlashMs)));
        Assert.Equal("\u001b[?5l", S(filter.Filter(B("\u001b[?5l"), 5000, out var flashes))); // and turned off later as usual
        Assert.Equal(0, flashes);
    }

    [Fact]
    public void OtherSequencesPassThroughWholeEvenWhenSplit()
    {
        var filter = new ScreenFlashFilter();
        var first = filter.Filter(B("\u001b[?25"), 0, out _); // could be ESC [ ? 5 h / l's start? no: "?2" isn't
        var second = filter.Filter(B("h\u001b["), 0, out _);   // "ESC [" at the end might be: held
        var third = filter.Filter(B("31m"), 0, out _);
        Assert.Equal("\u001b[?25h\u001b[31m", S(first) + S(second) + S(third));
        Assert.Equal("\u001b", S(filter.Filter(B("\u001b"), 0, out _)) + S(filter.TakeExpired(1))); // a lone ESC isn't lost
    }
}
