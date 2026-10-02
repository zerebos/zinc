using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Phosphor.Terminal;

namespace Phosphor;

/// <summary>
/// The drawing benchmark (Settings > About): a window of its own with one terminal that runs no
/// program; each <see cref="BenchmarkWorkload"/> is written into it from a background thread for
/// <see cref="Seconds"/>, as fast as it takes it (the way a busy program writes), while every frame
/// it draws is timed. Optionally all again with software rendering (Direct2D on the processor), the
/// nearest thing to a slow machine. ConPTY isn't in the way: this measures Phosphor's own side.
/// </summary>
internal sealed class BenchmarkWindow : Window
{
    public const double Seconds = 5;

    private readonly Grid _host = new();
    private bool _closed;

    private BenchmarkWindow()
    {
        Title = "Phosphor benchmark";
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"));
        Content = _host;
        Closed += (_, _) => _closed = true;
    }

    /// <summary>Runs the benchmark with <paramref name="config"/>'s look; null if the window was closed before it finished.</summary>
    public static async Task<BenchmarkReport?> Run(Config config, bool software, Window? owner)
    {
        var window = new BenchmarkWindow();
        WindowPlacement.CenterOnScreen(window, 1200, 760, owner);
        window.Activate();
        try
        {
            return await window.RunAll(config, software);
        }
        finally
        {
            if (!window._closed)
            {
                window.Close();
            }
        }
    }

    private async Task<BenchmarkReport?> RunAll(Config config, bool software)
    {
        var results = new List<BenchmarkResult>();
        (int Cols, int Rows) grid = default;
        var workloads = Enum.GetValues<BenchmarkWorkload>();
        var passes = software ? new[] { false, true } : [false];
        var step = 0;
        foreach (var pass in passes)
        {
            var terminal = TerminalControl.Benchmark(config, pass);
            _host.Children.Clear();
            _host.Children.Add(terminal);
            try
            {
                if (await GridOf(terminal) is not { } size)
                {
                    return null;
                }

                grid = size;
                var output = new BenchmarkWorkloads(size.Cols, size.Rows);
                foreach (var workload in workloads)
                {
                    Title = $"Phosphor benchmark: {BenchmarkWorkloads.Title(workload)}{(pass ? ", software" : "")} ({++step} of {workloads.Length * passes.Length})";
                    if (await RunOne(terminal, output, workload, pass) is not { } result)
                    {
                        return null;
                    }

                    results.Add(result);
                    Log.Write($"benchmark: {result.Line}");
                }
            }
            finally
            {
                _host.Children.Remove(terminal);
                terminal.Close();
            }
        }

        var scale = Content.XamlRoot?.RasterizationScale ?? 1;
        return new BenchmarkReport(DateTime.Now, Log.About, grid.Cols, grid.Rows, scale, results);
    }

    /// <summary>The terminal's grid once its session has started (it starts when the canvas has its size); null if closed or it never does.</summary>
    private async Task<(int Cols, int Rows)?> GridOf(TerminalControl terminal)
    {
        for (var i = 0; i < 100 && !_closed; i++)
        {
            if (terminal.BenchmarkGrid is { Cols: > 0, Rows: > 0 } grid)
            {
                return grid;
            }

            await Task.Delay(100);
        }

        return null;
    }

    private async Task<BenchmarkResult?> RunOne(TerminalControl terminal, BenchmarkWorkloads output, BenchmarkWorkload workload, bool software)
    {
        if (terminal.BenchmarkPerf is not { } perf)
        {
            return null;
        }

        output.Next(workload, 0); // makes the color frames before the clock starts
        terminal.WriteForBenchmark(BenchmarkWorkloads.Start(workload));
        await Task.Delay(300); // settles: the switch to the alternate screen is drawn outside the measurement
        perf.Reset();

        var closed = () => _closed;
        var (units, elapsed) = await Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            long sent = 0;
            for (var n = 0; clock.Elapsed.TotalSeconds < Seconds && !closed(); n++)
            {
                var (bytes, count) = output.Next(workload, n);
                terminal.WriteForBenchmark(bytes);
                sent += count;
            }

            return (sent, clock.Elapsed.TotalSeconds);
        });

        // Frames are timed on this (the UI) thread, so reading them here races with nothing.
        var frames = perf.Frames;
        var (draw, flush, sections) = perf.Snapshot();
        terminal.WriteForBenchmark(BenchmarkWorkloads.End(workload));
        await Task.Delay(200);
        return _closed ? null : new BenchmarkResult(workload, software, elapsed, units / elapsed, frames, draw, flush, sections);
    }
}
