using Zinc.Input;

namespace Zinc.Tests;

public class InputLatencyTests
{
    [Fact]
    public void InOrderTypingReportsTimesAndNoSwaps()
    {
        var trace = new InputLatency();
        trace.KeyDown(0, 2, "h");
        trace.Character(5, "h");
        trace.KeyDown(100, 3, "i");
        trace.Character(104, "i");

        var line = trace.Summary(10_000)!;
        Assert.Contains("input: 2 keys, 2 characters", line);
        Assert.Contains("key→char median 4, p95 5, max 5 ms", line);
        Assert.Contains("queue median 2", line);
        Assert.Contains("out of order 0, swallowed 0, unexpected 0", line);
    }

    [Fact]
    public void ASpaceOvertakingTheLastLetterIsCounted()
    {
        // "o" then space typed; the space's character arrives first (the tester's "the space lands before the letter").
        var trace = new InputLatency();
        trace.KeyDown(0, 0, "o");
        trace.KeyDown(30, 0, " ");
        trace.Character(40, " ");
        trace.Character(45, "o");

        var line = trace.Summary(10_000)!;
        Assert.Contains("out of order 1", line);
        Assert.Contains("'space' before 'o'", line);
    }

    [Fact]
    public void ASwallowedCharacterIsCounted()
    {
        var trace = new InputLatency();
        trace.KeyDown(0, 0, "s");
        trace.KeyDown(20, 0, null); // Backspace: encoded at once
        trace.Swallowed("s");

        var line = trace.Summary(10_000)!;
        Assert.Contains("swallowed 1,", line);
        Assert.Contains("('s' swallowed)", line);
    }

    [Fact]
    public void EchoTimesRunFromTheFirstSendToTheOutputAndOnToTheScreen()
    {
        var trace = new InputLatency();
        trace.KeyDown(0, 0, "a");
        trace.Sent(1);
        trace.Sent(3); // a second key before the echo: the clock keeps the first
        trace.Output(21);
        trace.Drawn(30);

        var line = trace.Summary(10_000)!;
        Assert.Contains("send→output median 20", line);
        Assert.Contains("output→frame median 9", line);
    }

    [Fact]
    public void ASummaryWaitsForAStretchOfTyping()
    {
        var trace = new InputLatency();
        Assert.Null(trace.Summary(0));
        trace.KeyDown(0, 0, "a");
        Assert.Null(trace.Summary(1000));
        Assert.NotNull(trace.Summary(6000));
        Assert.Null(trace.Summary(7000)); // started afresh
    }
}
