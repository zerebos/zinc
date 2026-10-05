using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Terminal;

namespace Zinc;

/// <summary>About's drawing benchmark: run it (<see cref="BenchmarkWindow"/>), see and copy the last results.</summary>
internal sealed partial class SettingsWindow
{
    /// <summary>The last run's results, for the app's life (shown again whenever About is).</summary>
    private static BenchmarkReport? _lastBenchmark;

    private static bool _benchmarkSoftware;
    private static bool _benchmarkRunning;

    private void BenchmarkRows()
    {
        var software = new CheckBox { Content = "Also with software rendering", IsChecked = _benchmarkSoftware, MinWidth = 0 };
        software.Checked += (_, _) => _benchmarkSoftware = true;
        software.Unchecked += (_, _) => _benchmarkSoftware = false;

        var run = new Button { Content = _benchmarkRunning ? "Running…" : "Run benchmark", IsEnabled = !_benchmarkRunning };
        AutomationProperties.SetName(run, "Run the drawing benchmark");
        run.Click += (_, _) => RunBenchmark();

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { software, run } };
        _page.Children.Add(Row("Drawing benchmark",
            $"How fast Zinc draws on this computer: {Enum.GetValues<BenchmarkWorkload>().Length} tests of {BenchmarkWindow.Seconds:0} seconds, " +
            "in a window of their own. Software rendering draws without the graphics card, which shows how a slower computer would do.",
            controls));

        if (_lastBenchmark is { } report)
        {
            _page.Children.Add(BenchmarkResults(report));
        }
    }

    private async void RunBenchmark()
    {
        if (_benchmarkRunning)
        {
            return;
        }

        _benchmarkRunning = true;
        Render();
        try
        {
            var report = await BenchmarkWindow.Run(_config.DefaultProfile.Config, _benchmarkSoftware, this);
            if (report != null)
            {
                _lastBenchmark = report;
            }
        }
        catch (Exception ex)
        {
            Log.Crash("drawing benchmark", ex); // after an await: nothing above catches it
        }
        finally
        {
            _benchmarkRunning = false;
        }

        try
        {
            Activate();
            Render();
        }
        catch (Exception ex)
        {
            Log.Crash("showing the benchmark", ex);
        }
    }

    /// <summary>The results as a table: per test, how fast the program's output was taken, frames drawn, and frame times.</summary>
    private static Border BenchmarkResults(BenchmarkReport report)
    {
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var table = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        string[] headings = ["Test", "Program output", "Drawn", "Frame time (avg / p95)"];
        for (var c = 0; c < headings.Length; c++)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        void Add(int row, int col, string text, Brush? brush = null)
        {
            var cell = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            if (brush != null)
            {
                cell.Foreground = brush;
            }

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            table.Children.Add(cell);
        }

        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < headings.Length; c++)
        {
            Add(0, c, headings[c], secondary);
        }

        for (var i = 0; i < report.Results.Count; i++)
        {
            var result = report.Results[i];
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(i + 1, 0, BenchmarkWorkloads.Title(result.Workload) + (result.Software ? " (software)" : ""));
            Add(i + 1, 1, $"{result.ProgramRate:N0} {BenchmarkWorkloads.Unit(result.Workload)}/s");
            Add(i + 1, 2, $"{result.DrawRate:0} fps");
            Add(i + 1, 3, $"{result.FrameMs:0.0} / {result.Draw.P95 + result.Flush.P95:0.0} ms");
        }

        var copy = new Button { Content = "Copy results", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(copy, "Copy the benchmark results");
        copy.Click += (_, _) =>
        {
            ClipboardText.Set(report.Text());
            copy.Content = "Copied";
        };

        var heading = new Grid();
        heading.Children.Add(new TextBlock
        {
            Text = $"Last run: {report.When:t}, {report.Cols} x {report.Rows} cells",
            FontSize = 12,
            Foreground = secondary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        heading.Children.Add(copy);

        return CardBorder(new StackPanel
        {
            Spacing = 10,
            Padding = new Thickness(0, 4, 0, 4),
            Children =
            {
                heading,
                table,
                new TextBlock
                {
                    Text = "Program output: how fast Zinc read a busy program's output. Drawn: frames a second it showed. " +
                        "Frame time: drawing one frame. Copy has the details.",
                    FontSize = 12,
                    Foreground = secondary,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        });
    }
}
