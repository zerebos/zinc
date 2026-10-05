using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Zinc.Panes;
using Zinc.Terminal;
using Windows.UI;

namespace Zinc;

/// <summary>
/// One pane: a tab group's strip of tabs (when shown) over its shown tab's terminal. Each tab's
/// strip element is built once and updated in place, like <see cref="TabHeader"/>: rebuilding
/// headers re-decodes icons and makes them blink.
/// </summary>
internal sealed partial class GroupView : Grid
{
    public const double StripHeight = 28;
    private const double AddWidth = 30;

    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _strip = new() { Height = StripHeight };
    private readonly Border _content = new();
    private readonly Dictionary<MainWindow.TerminalTab, TabItem> _items = new();
    private readonly Button _add;

    public GroupView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var add = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 11 },
            Width = AddWidth,
            Padding = new Thickness(0, 4, 0, 4),
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(add, "New tab in this pane");
        ToolTipService.SetToolTip(add, "New tab in this pane"); // the window adds its shortcut (SetAddToolTip)
        _add = add;
        add.Click += (_, _) => NewTabClicked?.Invoke();

        // Tabs scroll when they don't fit; the + stays pinned after them.
        _strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var scroll = new ScrollViewer
        {
            Content = _tabs,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        SetColumn(add, 1);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        _strip.Children.Add(scroll);
        _strip.Children.Add(add);
        // The tabs take what they need, up to the width left beside the +.
        _strip.SizeChanged += (_, e) =>
        {
            scroll.MaxWidth = Math.Max(0, e.NewSize.Width - AddWidth - 2);
            BringShownIntoView(); // a narrower pane (e.g. after a split) can hide it
        };
        SetRow(_content, 1);
        SetRow(_dim, 1);
        SetRow(_focusLine, 1);
        SetRow(_paneDot, 1);
        Children.Add(_strip);
        Children.Add(_content);
        Children.Add(_dim);
        Children.Add(_focusLine);
        Children.Add(_paneDot); // above the dimming: it's what you should notice

        // Dropping a tab on the strip puts it between the tabs where it lands.
        _strip.Children.Add(_marker);
        _strip.AllowDrop = true;
        _strip.DragOver += (_, e) =>
        {
            e.Handled = true; // not also an edge/middle drop on the pane below
            var index = IndexAt(e);
            if (StripDragOver?.Invoke(e, index) == true)
            {
                ShowMarker(index);
            }
            else
            {
                _marker.Visibility = Visibility.Collapsed;
            }
        };
        _strip.DragLeave += (_, _) => _marker.Visibility = Visibility.Collapsed;
        _strip.Drop += (_, e) =>
        {
            e.Handled = true;
            _marker.Visibility = Visibility.Collapsed;
            StripDrop?.Invoke(e, IndexAt(e));
        };
    }

    private readonly Border _marker = new()
    {
        Width = 2,
        Height = StripHeight - 8,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        Background = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"],
    };

    /// <summary>Marks the focused pane when it has no strip (a strip's shown tab carries the same accent line).</summary>
    private readonly Border _focusLine = new()
    {
        Height = 2,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>
    /// A pane without a strip has nowhere else to say a command finished (or a program wants you, or
    /// how its progress is going) while you looked elsewhere: its dot sits in its top-right corner.
    /// </summary>
    private readonly AttentionDot _paneDot = new()
    {
        Corner = false,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 8, 10, 0),
    };

    /// <summary>Dims an unfocused pane (unfocused-split-opacity): its fill color, partly transparent, over the terminal.</summary>
    private readonly Border _dim = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    public event Action<MainWindow.TerminalTab>? TabClicked;
    public event Action<MainWindow.TerminalTab>? TabCloseClicked;
    public event Action<MainWindow.TerminalTab, FrameworkElement>? TabMenuRequested;
    public event Action? NewTabClicked;

    /// <summary>A strip tab starts being dragged (the handler fills in the drag's data).</summary>
    public event Action<MainWindow.TerminalTab, DragStartingEventArgs>? TabDragStarting;

    /// <summary>A strip tab's drag ended, with what the drop target did.</summary>
    public event Action<MainWindow.TerminalTab, Windows.ApplicationModel.DataTransfer.DataPackageOperation>? TabDragCompleted;

    /// <summary>A drag is over the strip at a drop position; the handler says whether it would be accepted.</summary>
    public event Func<DragEventArgs, int, bool>? StripDragOver;

    /// <summary>Something was dropped on the strip at a position (0 = before the first tab).</summary>
    public event Action<DragEventArgs, int>? StripDrop;

    /// <summary>The drop position under the pointer: how many tabs lie left of it.</summary>
    private int IndexAt(DragEventArgs e)
    {
        var x = e.GetPosition(_tabs).X;
        var index = 0;
        foreach (var child in _tabs.Children.OfType<FrameworkElement>())
        {
            var left = child.TransformToVisual(_tabs).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
            if (x < left + child.ActualWidth / 2)
            {
                break;
            }

            index++;
        }

        return index;
    }

    private void ShowMarker(int index)
    {
        var items = _tabs.Children.OfType<FrameworkElement>().ToList();
        double x;
        if (items.Count == 0)
        {
            x = 0;
        }
        else if (index < items.Count)
        {
            x = items[index].TransformToVisual(_strip).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        }
        else
        {
            var last = items[^1];
            x = last.TransformToVisual(_strip).TransformPoint(new Windows.Foundation.Point(last.ActualWidth, 0)).X;
        }

        _marker.Margin = new Thickness(Math.Max(0, x - 1), 0, 0, 0);
        _marker.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Shows the group's current state: its tabs in order, the active one highlighted (with an
    /// accent line when the pane has focus), and the active tab's terminal below. When the
    /// workspace is split (<paramref name="split"/>), focus shows as an accent line on top (the
    /// shown tab's, or the pane's own without a strip), and unfocused panes can be dimmed toward
    /// <paramref name="dimFill"/> (<paramref name="dim"/> = how much, 0 = not at all).
    /// </summary>
    public void Update(TabGroup<MainWindow.TerminalTab> group, bool showStrip, bool focused, Brush accent,
        bool split = false, double dim = 0, Color dimFill = default)
    {
        _strip.Visibility = showStrip ? Visibility.Visible : Visibility.Collapsed;
        var line = split && focused && !showStrip;
        _focusLine.Visibility = line ? Visibility.Visible : Visibility.Collapsed;
        if (line && group.Active is { } shown)
        {
            // Over the terminal's top edge: kept visible on its background.
            var lineBrush = Contrast.AccentOn(TerminalRenderer.ToColor(shown.Profile.Config.Theme.Background), accent);
            if (_focusLine.Background != lineBrush)
            {
                _focusLine.Background = lineBrush;
            }
        }

        var dimmed = split && !focused && dim > 0;
        _dim.Visibility = dimmed ? Visibility.Visible : Visibility.Collapsed;
        if (dimmed)
        {
            var color = Color.FromArgb((byte)Math.Round(dim * 255), dimFill.R, dimFill.G, dimFill.B);
            if (_dim.Background is not SolidColorBrush { } b || b.Color != color)
            {
                _dim.Background = new SolidColorBrush(color);
            }
        }
        if (_content.Child != group.Active.Control)
        {
            _content.Child = group.Active.Control;
        }

        // Without a strip, the pane's own dot: its tabs' most urgent (and how many), else a progress ring.
        List<MainWindow.TerminalTab> wanting = showStrip ? [] : group.Tabs.Where(t => t.Attention != Attention.None).ToList();
        _paneDot.Show(wanting.Select(t => t.Attention).DefaultIfEmpty().Max(), wanting.Count);
        _paneDot.ShowProgress(showStrip ? TerminalProgress.None
            : group.Tabs.Select(t => t.Control.Progress).FirstOrDefault(p => p.Active, TerminalProgress.None));

        foreach (var gone in _items.Keys.Where(t => !group.Contains(t)).ToList())
        {
            _tabs.Children.Remove(_items[gone]);
            _items.Remove(gone);
        }

        if (!showStrip)
        {
            return;
        }

        // Re-order children only when the tabs changed, so the common case touches nothing.
        var order = group.Tabs.Select(Item).ToList();
        if (!_tabs.Children.SequenceEqual(order))
        {
            _tabs.Children.Clear();
            foreach (var item in order)
            {
                _tabs.Children.Add(item);
            }
        }

        foreach (var tab in group.Tabs)
        {
            _items[tab].Show(TitleOf(tab), tab == group.Active, focused, accent, tab.Attention, tab.Control.Progress);
        }

        if (_shown != group.Active)
        {
            _shown = group.Active;
            BringShownIntoView();
        }
    }

    private MainWindow.TerminalTab? _shown;

    /// <summary>Scrolls the strip so the shown tab is visible (after layout, when sizes are known).</summary>
    private void BringShownIntoView()
    {
        if (_shown != null && _items.TryGetValue(_shown, out var item))
        {
            DispatcherQueue.SafeEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => item.StartBringIntoView());
        }
    }

    /// <summary>Lets go of the shown terminal (the group is moving to another window).</summary>
    public void Release() => _content.Child = null;

    /// <summary>The + button's tooltip (the window's, which knows the current key bindings).</summary>
    public void SetAddToolTip(ToolTip tip) => ToolTipService.SetToolTip(_add, tip);

    public static string TitleOf(MainWindow.TerminalTab tab) => tab.Title;

    private TabItem Item(MainWindow.TerminalTab tab)
    {
        if (!_items.TryGetValue(tab, out var item))
        {
            item = new TabItem(tab);
            item.Tapped += (_, e) =>
            {
                e.Handled = true;
                TabClicked?.Invoke(tab);
            };
            item.CloseClicked += () => TabCloseClicked?.Invoke(tab);
            item.RightTapped += (_, e) =>
            {
                e.Handled = true;
                TabMenuRequested?.Invoke(tab, item);
            };
            item.CanDrag = true;
            item.DragStarting += (_, e) => TabDragStarting?.Invoke(tab, e);
            item.DropCompleted += (_, e) => TabDragCompleted?.Invoke(tab, e.DropResult);
            item.PointerReleased += (_, e) =>
            {
                // Middle click closes, like browser and editor tabs.
                if (e.GetCurrentPoint(item).Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased)
                {
                    e.Handled = true;
                    TabCloseClicked?.Invoke(tab);
                }
            };
            _items[tab] = item;
        }

        return item;
    }

    /// <summary>One tab in the strip: icon, title, close button.</summary>
    private sealed partial class TabItem : Grid
    {
        private readonly TextBlock _title = new()
        {
            MaxWidth = 180,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private readonly Border _accent = new() { Height = 2, VerticalAlignment = VerticalAlignment.Top };
        private readonly MainWindow.TerminalTab _tab;
        private readonly AttentionDot _dot = new() { Corner = false };
        private readonly Button _close;
        private SolidColorBrush _background;
        private readonly Brush _clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        private bool _active, _hovered;
        private Attention _attention;
        private TerminalProgress _progress = TerminalProgress.None;

        public TabItem(MainWindow.TerminalTab tab)
        {
            // The shown tab takes its terminal's background, so it flows into the pane below.
            _tab = tab;
            _background = new SolidColorBrush(TerminalRenderer.ToColor(tab.Profile.Config.Theme.Background));
            CornerRadius = new CornerRadius(6, 6, 0, 0);
            Background = _clear;

            var close = _close = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 9 },
                Padding = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = _clear,
                BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(close, "Close tab");
            close.Click += (_, _) => CloseClicked?.Invoke();

            var icon = new Grid
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { new IconSourceElement { IconSource = ProfileIcons.For(tab.Profile) } },
            };
            // The close button and the attention dot share one slot, as in the sidebar: the dot
            // until you hover, then the close button, so the tab keeps its width.
            _dot.Margin = new Thickness(0);
            _dot.HorizontalAlignment = HorizontalAlignment.Center;
            var slot = new Grid { Width = 20, Margin = new Thickness(6, 0, 0, 0), Children = { close, _dot } };

            // The padding goes on the content, not the tab, so the accent line spans the whole tab
            // (like the line across a strip-less focused pane).
            Children.Add(new StackPanel
            {
                Padding = new Thickness(10, 0, 4, 0),
                Orientation = Orientation.Horizontal,
                Children = { icon, _title, slot },
            });
            Children.Add(_accent);

            PointerEntered += (_, _) =>
            {
                _hovered = true;
                ShowSlot();
                if (!_active)
                {
                    Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
                }
            };
            PointerExited += (_, _) =>
            {
                _hovered = false;
                ShowSlot();
                if (!_active)
                {
                    Background = _clear;
                }
            };
        }

        public event Action? CloseClicked;

        private void ShowSlot()
        {
            _dot.Show(_attention);
            _dot.ShowProgress(_progress);
            var dot = _dot.HasContent && !_hovered;
            _dot.Opacity = dot ? 1 : 0; // hidden, not removed: showing it again mustn't re-pulse
            _close.Visibility = dot ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Show(string title, bool active, bool focused, Brush accent, Attention attention, TerminalProgress progress)
        {
            _attention = attention;
            _progress = progress;
            ShowSlot();
            if (_title.Text != title)
            {
                _title.Text = title;
                AutomationProperties.SetName(this, title);
                ToolTipService.SetToolTip(this, title);
            }

            // The profile's background can change (a config reload).
            var background = TerminalRenderer.ToColor(_tab.Profile.Config.Theme.Background);
            if (_background.Color != background)
            {
                _background = new SolidColorBrush(background);
            }

            _active = active;
            Background = active ? _background : _clear;
            // On a light terminal background (the shown tab takes it), dark text.
            if (active && Contrast.TextOn(_background.Color) is { } text)
            {
                _title.Foreground = text;
                _close.Foreground = text;
            }
            else
            {
                _title.ClearValue(TextBlock.ForegroundProperty);
                _close.ClearValue(Control.ForegroundProperty);
            }

            _title.Opacity = active ? 1 : 0.7;
            _accent.Background = active && focused ? Contrast.AccentOn(background, accent) : _clear;
        }
    }
}
