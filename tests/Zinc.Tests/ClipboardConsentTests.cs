using System.Text;
using Zinc.Terminal;

namespace Zinc.Tests;

public class ClipboardConsentTests
{
    [Theory]
    [InlineData("\e]52;c;?\u0007", 0, 9)]
    [InlineData("ab\e]52;;?\e\\cd", 2, 11)]
    [InlineData("\e]52;c;aGk=\u0007\e]52;p;?\u0007", 12, 21)] // a write first, then a read
    public void FindsReadRequests(string text, int start, int end)
    {
        Assert.True(ClipboardReadRequest.TryFind(Encoding.UTF8.GetBytes(text), out var s, out var e));
        Assert.Equal((start, end), (s, e));
    }

    [Theory]
    [InlineData("\e]52;c;aGk=\u0007")] // a write
    [InlineData("\e]52;c;?")]           // not finished in this read
    [InlineData("plain text ; ?")]
    public void IgnoresEverythingElse(string text) =>
        Assert.False(ClipboardReadRequest.TryFind(Encoding.UTF8.GetBytes(text), out _, out _));

    [Theory]
    [InlineData(true, "c2VjcmV0")] // base64 of "secret"
    [InlineData(false, "")]
    public void AReadWaitsForTheAnswerAndGetsTheClipboardOnlyIfAllowed(bool allow, string payload)
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<string>();
        var asked = 0;
        session.PtyReplyObserver = r => replies.Add(Encoding.UTF8.GetString(r));
        session.ClipboardReader = () => "secret";
        session.ClipboardConsent = () =>
        {
            asked++;
            return allow;
        };

        session.WriteToScreen("before\e]52;c;?\u0007after");
        Assert.Equal(1, asked);
        Assert.Contains($"]52;c;{payload}", string.Concat(replies));
        Assert.Contains("beforeafter", session.ReadScreenText());
    }

    [Fact]
    public void WithoutAQuestionReadsFollowTheReaderAlone()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<string>();
        session.PtyReplyObserver = r => replies.Add(Encoding.UTF8.GetString(r));
        session.ClipboardReader = () => "secret";
        session.WriteToScreen("\e]52;c;?\u0007");
        Assert.Contains("c2VjcmV0", string.Concat(replies));
    }
}
