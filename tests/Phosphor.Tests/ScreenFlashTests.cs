using System.Text;
using Phosphor.Terminal;

namespace Phosphor.Tests;

public class ScreenFlashTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

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
