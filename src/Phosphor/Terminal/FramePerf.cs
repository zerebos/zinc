using System.Diagnostics;

namespace Phosphor.Terminal;

/// <summary>
/// Frame timing for performance work (PHOSPHOR_PERF=1 with PHOSPHOR_LOG): once a second, how many
/// frames a pane drew and how long they took: our drawing (render state, glyph runs: the commands
/// Direct2D records) and the flush (Direct2D rasterizing them: the GPU's work, or the CPU's with
/// software rendering). Off, it costs one static check per frame.
/// </summary>
internal sealed class FramePerf
{
    public static readonly bool Enabled = Log.Enabled && Environment.GetEnvironmentVariable("PHOSPHOR_PERF") == "1";

    // (Before Sections: static fields initialize in order.)
    public static readonly string[] SectionNames = ["read", "backgrounds", "text", "decorations"];

    /// <summary>Where drawing time goes (ms, summed over the frames since the last log line): the renderer adds to these.</summary>
    public static readonly double[] Sections = new double[SectionNames.Length];

    /// <summary>Adds the time since <paramref name="since"/> to section <paramref name="section"/>; returns now.</summary>
    public static long Mark(int section, long since)
    {
        var now = Stopwatch.GetTimestamp();
        Sections[section] += Stopwatch.GetElapsedTime(since, now).TotalMilliseconds;
        return now;
    }

    private readonly List<double> _draw = [];
    private readonly List<double> _flush = [];
    private long _windowStart = Stopwatch.GetTimestamp();

    public void Add(double drawMs, double flushMs)
    {
        _draw.Add(drawMs);
        _flush.Add(flushMs);
        var elapsed = Stopwatch.GetElapsedTime(_windowStart);
        if (elapsed.TotalSeconds < 1)
        {
            return;
        }

        static string Stats(List<double> ms)
        {
            ms.Sort();
            return $"avg {ms.Average():0.0} p95 {ms[(int)(ms.Count * 0.95)]:0.0} max {ms[^1]:0.0} ms";
        }

        var frames = _draw.Count;
        var split = string.Join(", ", SectionNames.Select((name, i) => $"{name} {Sections[i] / frames:0.0}"));
        Array.Clear(Sections);
        Log.Write($"perf: {frames / elapsed.TotalSeconds:0} fps, draw {Stats(_draw)}, flush {Stats(_flush)}; per frame: {split} ms");
        _draw.Clear();
        _flush.Clear();
        _windowStart = Stopwatch.GetTimestamp();
    }
}
