using System.Text;

namespace Zinc.Terminal;

/// <summary>What the drawing benchmark makes a terminal show (as a program would write it).</summary>
public enum BenchmarkWorkload
{
    /// <summary>Full-screen frames, every few cells a new 256-color foreground and background: nothing repeats (the worst case for drawing).</summary>
    Colors,

    /// <summary>The same frames in synchronized output (mode 2026), as btop and neovim send them.</summary>
    Synchronized,

    /// <summary>Full-screen plain text that changes every frame (an editor scrolling).</summary>
    Text,

    /// <summary>Colored log lines scrolling through the main screen into the scrollback.</summary>
    Scrolling,
}

/// <summary>
/// The benchmark's output, made up front for a <paramref name="cols"/> x <paramref name="rows"/>
/// screen from a fixed seed, so every run (and every machine) draws the same thing. A port of
/// .scratch/perf/load.py, which measured the same workloads from inside WSL.
/// </summary>
public sealed class BenchmarkWorkloads(int cols, int rows, int seed = 42)
{
    private const string Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789{}[]()<>=+-*/│─┼█▓▒░";
    private static readonly string[] Words = ["def", "return", "self", "value", "for", "in", "if", "else"];

    /// <summary>How many different color frames there are (cycled): enough that none is drawn twice in a row.</summary>
    public const int ColorFrames = 32;

    private readonly Random _random = new(seed);
    private byte[][]? _colorFrames;

    public static string Title(BenchmarkWorkload workload) => workload switch
    {
        BenchmarkWorkload.Colors => "Colors",
        BenchmarkWorkload.Synchronized => "Synchronized output",
        BenchmarkWorkload.Text => "Text",
        _ => "Scrolling",
    };

    /// <summary>What a program says before its first frame (the alternate screen, cursor hidden).</summary>
    public static byte[] Start(BenchmarkWorkload workload) =>
        workload == BenchmarkWorkload.Scrolling ? "\e[2J\e[H"u8.ToArray() : "\e[?1049h\e[?25l"u8.ToArray();

    /// <summary>What it says when it's done (back to the main screen).</summary>
    public static byte[] End(BenchmarkWorkload workload) =>
        workload == BenchmarkWorkload.Scrolling ? "\e[0m"u8.ToArray() : "\e[?25h\e[?1049l"u8.ToArray();

    /// <summary>
    /// Output number <paramref name="n"/>: one full frame, or for <see cref="BenchmarkWorkload.Scrolling"/> 200
    /// lines; with how many units it counts as (frames, or lines).
    /// </summary>
    public (byte[] Bytes, int Units) Next(BenchmarkWorkload workload, int n)
    {
        switch (workload)
        {
            case BenchmarkWorkload.Colors:
                return (ColorFrame(n), 1);
            case BenchmarkWorkload.Synchronized:
                return ([.. "\e[?2026h"u8, .. ColorFrame(n), .. "\e[?2026l"u8], 1);
            case BenchmarkWorkload.Text:
                return (TextFrame(n), 1);
            default:
                var lines = new StringBuilder();
                for (var i = 0; i < 200; i++)
                {
                    var line = n * 200 + i + 1;
                    lines.Append($"\e[32m{line,8}\e[0m \e[1;34mINFO\e[0m request handled in \e[33m{_random.Next(1, 1000)} ms\e[0m path=/api/v1/items/{_random.Next(1, 100000)}\r\n");
                }

                return (Encoding.UTF8.GetBytes(lines.ToString()), 200);
        }
    }

    /// <summary>"frames" or "lines": what <see cref="Next"/>'s units are.</summary>
    public static string Unit(BenchmarkWorkload workload) => workload == BenchmarkWorkload.Scrolling ? "lines" : "frames";

    private byte[] ColorFrame(int n)
    {
        if (_colorFrames == null)
        {
            _colorFrames = new byte[ColorFrames][];
            for (var i = 0; i < ColorFrames; i++)
            {
                _colorFrames[i] = MakeColorFrame();
            }
        }

        return _colorFrames[n % ColorFrames];
    }

    private byte[] MakeColorFrame()
    {
        var frame = new StringBuilder("\e[H");
        for (var r = 0; r < rows; r++)
        {
            var c = 0;
            while (c < cols)
            {
                var run = Math.Min(cols - c, _random.Next(1, 9));
                frame.Append($"\e[38;5;{_random.Next(16, 256)};48;5;{_random.Next(16, 256)}m");
                for (var i = 0; i < run; i++)
                {
                    frame.Append(Chars[_random.Next(Chars.Length)]);
                }

                c += run;
            }

            if (r < rows - 1)
            {
                frame.Append("\r\n");
            }
        }

        frame.Append("\e[0m");
        return Encoding.UTF8.GetBytes(frame.ToString());
    }

    private byte[] TextFrame(int n)
    {
        var frame = new StringBuilder("\e[H");
        for (var r = 0; r < rows; r++)
        {
            var line = new StringBuilder($"{n + r,6}  ");
            for (var w = 0; w < cols / 7; w++)
            {
                line.Append(w == 0 ? "" : " ").Append(Words[_random.Next(Words.Length)]);
            }

            var text = line.Length > cols ? line.ToString(0, cols) : line.ToString().PadRight(cols);
            frame.Append(text);
            if (r < rows - 1)
            {
                frame.Append("\r\n");
            }
        }

        return Encoding.UTF8.GetBytes(frame.ToString());
    }
}

/// <summary>Average, 95th percentile and worst of a set of times (ms).</summary>
public readonly record struct TimeStats(double Average, double P95, double Max)
{
    public static TimeStats Of(IReadOnlyList<double> ms)
    {
        if (ms.Count == 0)
        {
            return default;
        }

        var sorted = ms.Order().ToArray();
        return new(sorted.Average(), sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.95))], sorted[^1]);
    }

    public override string ToString() => $"avg {Average:0.0}, p95 {P95:0.0}, max {Max:0.0} ms";
}

/// <summary>
/// One workload's result: how fast the terminal took the program's output (<see cref="ProgramRate"/>
/// units a second), how often it drew, and how long its frames took: our drawing (<see cref="Draw"/>:
/// reading the screen and recording Direct2D commands, split into <see cref="Sections"/>) and
/// <see cref="Flush"/> (Direct2D rasterizing them, on the GPU or the CPU).
/// </summary>
public sealed record BenchmarkResult(
    BenchmarkWorkload Workload,
    bool Software,
    double Seconds,
    double ProgramRate,
    int FramesDrawn,
    TimeStats Draw,
    TimeStats Flush,
    IReadOnlyList<(string Name, double Ms)> Sections)
{
    public double DrawRate => Seconds > 0 ? FramesDrawn / Seconds : 0;

    /// <summary>The time a frame took on average, drawing and rasterizing.</summary>
    public double FrameMs => Draw.Average + Flush.Average;

    public string Line =>
        $"{BenchmarkWorkloads.Title(Workload)}{(Software ? " (software)" : "")}: program {ProgramRate:0} {BenchmarkWorkloads.Unit(Workload)}/s, " +
        $"drew {DrawRate:0} fps; draw {Draw}; rasterize {Flush}" +
        (Sections.Count > 0 ? "; per frame: " + string.Join(", ", Sections.Select(s => $"{s.Name} {s.Ms:0.0}")) + " ms" : "");
}

/// <summary>A benchmark run: its results, and what they were measured on.</summary>
public sealed record BenchmarkReport(DateTime When, string About, int Cols, int Rows, double Scale, IReadOnlyList<BenchmarkResult> Results)
{
    /// <summary>The report as text, to paste into a bug report or a message.</summary>
    public string Text()
    {
        var text = new StringBuilder();
        text.AppendLine($"Zinc drawing benchmark, {When:yyyy-MM-dd HH:mm}");
        text.AppendLine(About);
        text.AppendLine($"{Cols} x {Rows} cells at {Scale * 100:0}% scale");
        foreach (var result in Results)
        {
            text.AppendLine(result.Line);
        }

        return text.ToString().TrimEnd();
    }
}
