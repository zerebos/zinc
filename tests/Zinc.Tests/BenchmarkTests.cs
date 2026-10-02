using Zinc.Terminal;

namespace Zinc.Tests;

public class BenchmarkTests
{
    [Theory]
    [InlineData(BenchmarkWorkload.Colors)]
    [InlineData(BenchmarkWorkload.Synchronized)]
    [InlineData(BenchmarkWorkload.Text)]
    public void FramesFillTheScreen(BenchmarkWorkload workload)
    {
        using var session = new TerminalSession(20, 5, TerminalTheme.Default);
        var output = new BenchmarkWorkloads(20, 5);
        session.WriteToScreen(BenchmarkWorkloads.Start(workload));
        var (bytes, units) = output.Next(workload, 3);
        Assert.Equal(1, units);
        session.WriteToScreen(bytes);

        var lines = session.ReadScreenText().Split('\n');
        Assert.Equal(5, lines.Length);
        Assert.All(lines, line => Assert.Equal(20, line.TrimEnd('\r').Length));
    }

    [Fact]
    public void ScrollingWritesLines()
    {
        var output = new BenchmarkWorkloads(80, 24);
        var (bytes, units) = output.Next(BenchmarkWorkload.Scrolling, 1);
        Assert.Equal(200, units);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Equal(200, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("     201", text); // numbered on from the previous chunk
    }

    [Fact]
    public void SameSeedSameOutput()
    {
        var a = new BenchmarkWorkloads(40, 10).Next(BenchmarkWorkload.Colors, 0).Bytes;
        var b = new BenchmarkWorkloads(40, 10).Next(BenchmarkWorkload.Colors, 0).Bytes;
        Assert.Equal(a, b);
        Assert.Equal("\e[?2026h"u8.ToArray(), new BenchmarkWorkloads(40, 10).Next(BenchmarkWorkload.Synchronized, 0).Bytes[..8]);
    }

    [Fact]
    public void TimeStatsAndReport()
    {
        var stats = TimeStats.Of([1, 2, 3, 4, 10]);
        Assert.Equal(4, stats.Average);
        Assert.Equal(10, stats.Max);
        Assert.Equal(default, TimeStats.Of([]));

        var result = new BenchmarkResult(BenchmarkWorkload.Colors, Software: true, Seconds: 5, ProgramRate: 100, FramesDrawn: 200,
            stats, TimeStats.Of([1]), [("read", 0.5)]);
        Assert.Equal(40, result.DrawRate);
        Assert.Equal(5, result.FrameMs);
        var text = new BenchmarkReport(new DateTime(2026, 10, 1, 12, 0, 0), "zinc x", 120, 40, 1.5, [result]).Text();
        Assert.Contains("120 x 40 cells at 150% scale", text);
        Assert.Contains("Colors (software): program 100 frames/s, drew 40 fps", text);
        Assert.Contains("per frame: read 0.5 ms", text);
    }
}
