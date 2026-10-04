using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Input;
using Zinc.Terminal;
using Windows.Foundation;
using Windows.Graphics;

namespace Zinc;

// The title bar row in vertical-tabs mode: the app's name over the sidebar and, in the middle, a
// command center showing what the focused tab is doing; clicking it opens the command palette.
// The row is the window's drag area (caption), so the command center is registered as a
// click-through region. See .scratch/NOTES.md "Chrome plan" for where this is heading.
public sealed partial class MainWindow
{
    private TextBlock? _commandContext;
    private FontIcon? _searchGlyph;
    private Microsoft.UI.Xaml.Shapes.Rectangle? _runningCell;
    private TextBlock? _commandShortcut;
    private bool _windowClosed;
    private ColumnDefinition? _markColumn;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _commandClock;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _commandSettle;

    /// <summary>0:38, 12:05, 1:02:03.</summary>
    private static string Elapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    private StackPanel? _mark;

    private FrameworkElement BuildTitleRow()
    {
        var row = new Grid();
        _markColumn = new ColumnDefinition { Width = new GridLength(WorkspaceList.DefaultWidth) };
        row.ColumnDefinitions.Add(_markColumn);
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // the window buttons

        var mark = _mark = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Zinc.ico"))),
                    Width = 16,
                    Height = 16,
                },
                new TextBlock { Text = "Zinc", FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        row.Children.Add(mark);

        _commandContext = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _commandShortcut = new TextBlock { FontSize = 11, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _searchGlyph = new FontIcon { Glyph = "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_searchGlyph);
        // While a command runs: a still lit cell in place of the search glyph (the pulse is for things that need you).
        _runningCell = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = 7,
            Height = 7,
            RadiusX = 0.84,
            RadiusY = 0.84,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        content.Children.Add(_runningCell);
        Grid.SetColumn(_commandContext, 1);
        content.Children.Add(_commandContext);
        Grid.SetColumn(_commandShortcut, 2);
        content.Children.Add(_commandShortcut);

        var center = new Button
        {
            Height = 24,
            MinWidth = 280,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(6),
            IsTabStop = false,
            Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Content = content,
        };
        AutomationProperties.SetName(center, "Search commands");
        center.Click += (_, _) => OpenCommandPalette();
        Grid.SetColumn(center, 1);
        row.Children.Add(center);

        _refreshClickThrough = KeepClickThrough(center);
        UpdateCommandCenter();
        return row;
    }

    /// <summary>Keeps the app mark over the sidebar: its width, just the icon over the rail, or its own width when the sidebar hides.</summary>
    private void UpdateMark(SidebarMode mode)
    {
        if (_markColumn == null || _mark == null)
        {
            return;
        }

        _markColumn.Width = mode switch
        {
            SidebarMode.Full => new GridLength(WorkspaceList.DefaultWidth),
            SidebarMode.Rail => new GridLength(WorkspaceList.RailWidth),
            _ => GridLength.Auto,
        };
        _mark.Children[1].Visibility = mode == SidebarMode.Rail ? Visibility.Collapsed : Visibility.Visible;
        _mark.Margin = new Thickness(mode == SidebarMode.Rail ? (WorkspaceList.RailWidth - 16) / 2 : 14, 0, 14, 0);
    }

    /// <summary>
    /// <see cref="UpdateCommandCenter"/> once a shell's reports have settled. They come in bursts: a
    /// prompt reports its new folder, then sets the title (Git Bash's names the folder), and drawn
    /// in between, the center showed the new folder beside the old title for a moment.
    /// </summary>
    private void UpdateCommandCenterSoon()
    {
        if (_commandSettle == null)
        {
            _commandSettle = DispatcherQueue.CreateTimer();
            _commandSettle.Interval = TimeSpan.FromMilliseconds(80);
            _commandSettle.IsRepeating = false;
            _commandSettle.OnTick(_ => UpdateCommandCenter());
        }

        _commandSettle.Stop();
        _commandSettle.Start();
    }

    /// <summary>What the focused tab of the shown workspace is doing: its title, and the folder its shell reported.</summary>
    private void UpdateCommandCenter()
    {
        if (_commandContext == null || _commandShortcut == null)
        {
            return;
        }

        var text = "";
        var running = false;
        if (SelectedTab is { } tab)
        {
            var pane = tab.Active;
            if (pane.Control.RunningCommand is { Text: { } command, Started: { } started })
            {
                // Shell integration: what's running, and for how long.
                text = $"{command}  ·  {Elapsed(DateTime.UtcNow - started)}";
                running = true;
            }
            else
            {
                var title = pane.Title;
                var folder = ShellDirectory.Parse(pane.Control.ReportedDirectory);
                text = folder != null && !title.Contains(folder, StringComparison.OrdinalIgnoreCase) ? $"{title}  ·  {folder}" : title;
            }
        }

        // While a command runs, the elapsed time ticks.
        if (running && _commandClock == null)
        {
            _commandClock = DispatcherQueue.CreateTimer();
            _commandClock.Interval = TimeSpan.FromSeconds(1);
            _commandClock.OnTick(_ =>
            {
                // A/B for the pointer trace: does the clock's tick put the arrow back?
                if (PointerTrace.HoldClock)
                {
                    Log.Write("pointer: command clock tick (held)");
                    return;
                }

                if (PointerTrace.Running)
                {
                    Log.Write("pointer: command clock tick");
                }

                UpdateCommandCenter();
            });
        }

        if (running)
        {
            _commandClock!.Start();
        }
        else
        {
            _commandClock?.Stop();
        }

        if (_runningCell != null && _searchGlyph != null)
        {
            _runningCell.Fill = new SolidColorBrush(Brand.Attention(Brand.IsDark(_runningCell)));
            _runningCell.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            _searchGlyph.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_commandContext.Text != text)
        {
            if (!running)
            {
                Log.Write($"command center: {text}");
            }

            _commandContext.Text = text;
            ToolTipService.SetToolTip(_commandContext, text);
        }

        _commandShortcut.Text = _keybindings.ShortcutFor(TerminalAction.OpenCommandPalette) ?? "";
    }

    /// <summary>
    /// Lets an element in the title row take clicks: the row is caption (it drags the window),
    /// so the element is registered as a click-through region, kept in step with layout.
    /// </summary>
    /// <returns>Brings the region up to date now (after a move layout doesn't see, like a slide).</returns>
    private Action KeepClickThrough(FrameworkElement element)
    {
        RectInt32? set = null;
        void Update()
        {
            // Layout still runs as a closed window tears down; its AppWindow is gone by then.
            if (_windowClosed || element.XamlRoot is not { } root || element.ActualWidth <= 0)
            {
                return;
            }

            var scale = root.RasterizationScale;
            var r = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            var rect = new RectInt32((int)(r.X * scale), (int)(r.Y * scale), (int)Math.Ceiling(r.Width * scale), (int)Math.Ceiling(r.Height * scale));
            // LayoutUpdated fires for every layout pass in the window: only tell Windows about changes.
            if (set is { } previous && previous.Equals(rect))
            {
                return;
            }

            set = rect;
            InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
        }

        element.SizeChanged += (_, _) => Update();
        element.LayoutUpdated += (_, _) => Update();
        return Update;
    }
}
