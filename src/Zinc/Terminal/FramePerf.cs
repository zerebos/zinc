using System.Diagnostics;

namespace Zinc.Terminal;

/// <summary>
/// Frame timing for one pane, which is how long its frames took, split into our drawing and the flush. Our drawing is
/// the render state and the glyph runs, which are the commands Direct2D records, split into <see cref="SectionNames"/>.
/// The flush is Direct2D rasterizing those commands, which is the GPU's work, or the CPU's with software rendering.
/// ZINC_PERF=1 with ZINC_LOG logs every pane's numbers once a second (<see cref="LogsEverySecond"/>), and the drawing
/// benchmark collects them for a run (<see cref="Snapshot"/>). When neither is on, a pane has no timing and pays one
/// null check per frame.
/// </summary>
internal sealed class FramePerf
{
    /// <summary>Whether every pane logs its frame times (ZINC_PERF=1 with ZINC_LOG).</summary>
    public static readonly bool LogEnabled = Log.Enabled && Environment.GetEnvironmentVariable("ZINC_PERF") == "1";

    public static readonly string[] SectionNames = ["read", "backgrounds", "text", "decorations"];

    /// <summary>
    /// Where drawing time went, in ms summed over the frames since the last reset. The renderer adds to these.
    /// </summary>
    private readonly double[] _sections = new double[SectionNames.Length];

    private readonly List<double> _draw = [];
    private readonly List<double> _flush = [];
    private long _windowStart = Stopwatch.GetTimestamp();

    public bool LogsEverySecond { get; init; }

    /// <summary>
    /// Adds the time since <paramref name="since"/> to section <paramref name="section"/>, and returns now.
    /// </summary>
    public long Mark(int section, long since)
    {
        var now = Stopwatch.GetTimestamp();
        _sections[section] += Stopwatch.GetElapsedTime(since, now).TotalMilliseconds;
        return now;
    }

    public void Add(double drawMs, double flushMs)
    {
        _draw.Add(drawMs);
        _flush.Add(flushMs);
        if (!LogsEverySecond)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_windowStart);
        if (elapsed.TotalSeconds < 1)
        {
            return;
        }

        var (draw, flush, sections) = Snapshot();
        var split = string.Join(", ", sections.Select(s => $"{s.Name} {s.Ms:0.0}"));
        Log.Write($"perf: {_draw.Count / elapsed.TotalSeconds:0} fps, draw {draw}, flush {flush}; per frame: {split} ms");
        Reset();
    }

    /// <summary>The frames since the last reset: draw and flush times, and the sections per frame.</summary>
    public (TimeStats Draw, TimeStats Flush, IReadOnlyList<(string Name, double Ms)> Sections) Snapshot()
    {
        var frames = Math.Max(1, _draw.Count);
        return (TimeStats.Of(_draw), TimeStats.Of(_flush), SectionNames.Select((name, i) => (name, _sections[i] / frames)).ToList());
    }

    public int Frames => _draw.Count;

    public void Reset()
    {
        _draw.Clear();
        _flush.Clear();
        Array.Clear(_sections);
        _windowStart = Stopwatch.GetTimestamp();
    }
}
