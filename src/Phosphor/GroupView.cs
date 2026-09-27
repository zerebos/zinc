using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Phosphor.Panes;
using Phosphor.Terminal;
using Windows.UI;

namespace Phosphor;

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
        ToolTipService.SetToolTip(add, "New tab in this pane (Ctrl+Shift+T)");
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
        Children.Add(_strip);
        Children.Add(_content);

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
    /// accent line when the pane has focus), and the active tab's terminal below.
    /// </summary>
    public void Update(TabGroup<MainWindow.TerminalTab> group, bool showStrip, bool focused, Brush accent)
    {
        _strip.Visibility = showStrip ? Visibility.Visible : Visibility.Collapsed;
        if (_content.Child != group.Active.Control)
        {
            _content.Child = group.Active.Control;
        }

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
            _items[tab].Show(TitleOf(tab), tab == group.Active, focused, accent);
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
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => item.StartBringIntoView());
        }
    }

    /// <summary>Lets go of the shown terminal (the group is moving to another window).</summary>
    public void Release() => _content.Child = null;

    public static string TitleOf(MainWindow.TerminalTab tab) =>
        string.IsNullOrWhiteSpace(tab.Control.Title) ? tab.Profile.Name : tab.Control.Title;

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
        private readonly Brush _background;
        private readonly Brush _clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        private bool _active;

        public TabItem(MainWindow.TerminalTab tab)
        {
            // The shown tab takes its terminal's background, so it flows into the pane below.
            _background = new SolidColorBrush(TerminalRenderer.ToColor(tab.Profile.Config.Theme.Background));
            Padding = new Thickness(10, 0, 4, 0);
            CornerRadius = new CornerRadius(6, 6, 0, 0);
            Background = _clear;

            var close = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 9 },
                Padding = new Thickness(5),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = _clear,
                BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(close, "Close tab");
            close.Click += (_, _) => CloseClicked?.Invoke();

            var icon = new IconSourceElement
            {
                IconSource = ProfileIcons.For(tab.Profile),
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { icon, _title, close },
            });
            Children.Add(_accent);

            PointerEntered += (_, _) =>
            {
                if (!_active)
                {
                    Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
                }
            };
            PointerExited += (_, _) =>
            {
                if (!_active)
                {
                    Background = _clear;
                }
            };
        }

        public event Action? CloseClicked;

        public void Show(string title, bool active, bool focused, Brush accent)
        {
            if (_title.Text != title)
            {
                _title.Text = title;
                AutomationProperties.SetName(this, title);
                ToolTipService.SetToolTip(this, title);
            }

            _active = active;
            Background = active ? _background : _clear;
            _title.Opacity = active ? 1 : 0.7;
            _accent.Background = active && focused ? accent : _clear;
        }
    }
}
