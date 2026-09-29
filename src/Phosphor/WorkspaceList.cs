using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;

namespace Phosphor;

/// <summary>
/// A window's workspaces as a vertical list on its left (<c>workspace-tabs = left</c>). The
/// window's TabView still holds the workspaces and shows the selected one; its horizontal
/// strip is hidden and this list stands in for it. Items are built once per workspace and
/// updated in place, like <see cref="GroupView"/>'s strip.
/// </summary>
internal sealed partial class WorkspaceList : Grid
{
    public const double DefaultWidth = 220;

    /// <summary>The width as a rail: icons only.</summary>
    public const double RailWidth = 52;

    private readonly StackPanel _items = new() { Spacing = 2, Padding = new Thickness(6, 6, 6, 6) };
    private readonly Dictionary<MainWindow.WorkspaceTab, Item> _byTab = new();

    private readonly Border _marker = new()
    {
        Height = 2,
        Margin = new Thickness(8, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        Background = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"],
    };

    /// <param name="footer">What sits below the list (new-workspace buttons).</param>
    public WorkspaceList(UIElement footer)
    {
        Width = DefaultWidth;
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow((FrameworkElement)footer, 1);
        Children.Add(footer);

        var scroll = new ScrollViewer
        {
            Content = _items,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Children.Add(scroll);
        Children.Add(_marker);

        // The whole column takes drops (below the last item = at the end).
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Handled = true; // not also the window root's
            var index = IndexAt(e);
            if (ListDragOver?.Invoke(e, index, TabAt(e)) == true)
            {
                ShowMarker(index);
            }
            else
            {
                _marker.Visibility = Visibility.Collapsed;
            }
        };
        DragLeave += (_, _) => _marker.Visibility = Visibility.Collapsed;
        Drop += (_, e) =>
        {
            e.Handled = true;
            _marker.Visibility = Visibility.Collapsed;
            ListDrop?.Invoke(e, IndexAt(e));
        };
    }

    public event Action<MainWindow.WorkspaceTab>? ItemClicked;
    public event Action<MainWindow.WorkspaceTab>? ItemCloseClicked;

    /// <summary>An item starts being dragged (the handler fills in the drag's data).</summary>
    public event Action<MainWindow.WorkspaceTab, DragStartingEventArgs>? ItemDragStarting;

    /// <summary>An item's drag ended, with what the drop target did.</summary>
    public event Action<MainWindow.WorkspaceTab, DataPackageOperation>? ItemDragCompleted;

    /// <summary>A drag is over the list at a drop position (and over an item, or null); the handler says whether it would be accepted.</summary>
    public event Func<DragEventArgs, int, MainWindow.WorkspaceTab?, bool>? ListDragOver;

    /// <summary>Something was dropped on the list at a position (0 = before the first item).</summary>
    public event Action<DragEventArgs, int>? ListDrop;

    private bool _compact;

    /// <summary>A rail of icons (titles in tooltips) instead of the full list.</summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            _compact = value;
            Width = value ? RailWidth : DefaultWidth;
            foreach (var item in _byTab.Values)
            {
                item.Compact = value;
            }
        }
    }

    /// <summary>The element showing a workspace (to anchor flyouts to).</summary>
    public FrameworkElement? ElementFor(MainWindow.WorkspaceTab tab) => _byTab.GetValueOrDefault(tab);

    /// <summary>
    /// Shows the workspaces in order with the selected one highlighted; <paramref name="colors"/>
    /// gives each one's tab color (if set) and its focused pane's background.
    /// </summary>
    public void Update(IReadOnlyList<MainWindow.WorkspaceTab> tabs, MainWindow.WorkspaceTab? selected,
        Func<MainWindow.WorkspaceTab, (Color? Color, Color Pane)> colors)
    {
        foreach (var gone in _byTab.Keys.Where(t => !tabs.Contains(t)).ToList())
        {
            _items.Children.Remove(_byTab[gone]);
            _byTab.Remove(gone);
        }

        var order = tabs.Select(ItemFor).ToList();
        if (!_items.Children.SequenceEqual(order))
        {
            _items.Children.Clear();
            foreach (var item in order)
            {
                _items.Children.Add(item);
            }
        }

        foreach (var tab in tabs)
        {
            var (color, pane) = colors(tab);
            _byTab[tab].Show(tab == selected, color, pane);
        }

        if (selected != null && _byTab.TryGetValue(selected, out var shown))
        {
            DispatcherQueue.SafeEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => shown.StartBringIntoView());
        }
    }

    private Item ItemFor(MainWindow.WorkspaceTab tab)
    {
        if (!_byTab.TryGetValue(tab, out var item))
        {
            item = new Item(tab) { Compact = _compact };
            item.Tapped += (_, e) =>
            {
                e.Handled = true;
                ItemClicked?.Invoke(tab);
            };
            item.CloseClicked += () => ItemCloseClicked?.Invoke(tab);
            item.PointerReleased += (_, e) =>
            {
                // Middle click closes, as on the title-bar tabs.
                if (e.GetCurrentPoint(item).Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased)
                {
                    e.Handled = true;
                    ItemCloseClicked?.Invoke(tab);
                }
            };
            item.CanDrag = true;
            item.DragStarting += (_, e) => ItemDragStarting?.Invoke(tab, e);
            item.DropCompleted += (_, e) => ItemDragCompleted?.Invoke(tab, e.DropResult);
            _byTab[tab] = item;
        }

        return item;
    }

    /// <summary>The drop position under the pointer: how many items lie above it.</summary>
    private int IndexAt(DragEventArgs e)
    {
        var y = e.GetPosition(_items).Y;
        var index = 0;
        foreach (var child in _items.Children.OfType<FrameworkElement>())
        {
            var top = child.TransformToVisual(_items).TransformPoint(new Point(0, 0)).Y;
            if (y < top + child.ActualHeight / 2)
            {
                break;
            }

            index++;
        }

        return index;
    }

    /// <summary>The workspace whose item is under the pointer, if any.</summary>
    private MainWindow.WorkspaceTab? TabAt(DragEventArgs e)
    {
        foreach (var (tab, item) in _byTab)
        {
            var p = e.GetPosition(item);
            if (p.X >= 0 && p.Y >= 0 && p.X < item.ActualWidth && p.Y < item.ActualHeight)
            {
                return tab;
            }
        }

        return null;
    }

    private void ShowMarker(int index)
    {
        var items = _items.Children.OfType<FrameworkElement>().ToList();
        double y;
        if (items.Count == 0)
        {
            y = 0;
        }
        else if (index < items.Count)
        {
            y = items[index].TransformToVisual(this).TransformPoint(new Point(0, 0)).Y - 2;
        }
        else
        {
            var last = items[^1];
            y = last.TransformToVisual(this).TransformPoint(new Point(0, last.ActualHeight)).Y;
        }

        _marker.Margin = new Thickness(8, Math.Max(0, y), 8, 0);
        _marker.Visibility = Visibility.Visible;
    }

    /// <summary>One workspace: accent pill when selected, icon + title, close button.</summary>
    private sealed partial class Item : Grid
    {
        private readonly Border _pill = new()
        {
            Width = 3,
            Height = 16,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"],
        };

        private readonly Button _close;

        // The close button and the attention dot share one slot: hovering swaps them, so nothing
        // beside them moves (VS Code's unsaved-file dot does the same).
        private readonly Grid _slot = new() { Width = 26, Margin = new Thickness(0, 0, 4, 0), Visibility = Visibility.Collapsed };
        private readonly AttentionDot _slotDot = new() { Corner = false };
        private readonly Brush _clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        private readonly TabHeader _header;
        private bool _selected, _hovered, _compact;
        private (bool, Color?, Color)? _shown;

        public Item(MainWindow.WorkspaceTab tab)
        {
            Height = 36;
            CornerRadius = new CornerRadius(4);
            Background = _clear;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _header = tab.Header;
            tab.Header.Owner = this;
            // The same menu as the (hidden) title-bar tab: rename, color, split, move, close…
            ContextFlyout = tab.Item.ContextFlyout;
            tab.Header.Margin = new Thickness(12, 0, 0, 0);
            Children.Add(tab.Header);

            _close = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 10 },
                Padding = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = _clear,
                BorderThickness = new Thickness(0),
                IsTabStop = false,
                Visibility = Visibility.Collapsed,
            };
            AutomationProperties.SetName(_close, "Close workspace");
            _close.Click += (_, _) => CloseClicked?.Invoke();
            _slotDot.Margin = new Thickness(0);
            _slotDot.HorizontalAlignment = HorizontalAlignment.Center;
            _slot.Children.Add(_close);
            _slot.Children.Add(_slotDot);
            SetColumn(_slot, 1);
            Children.Add(_slot);
            Children.Add(_pill);
            tab.Header.DotElsewhere = true;
            tab.Header.AttentionChanged += Restyle;
            tab.Header.PulseRequested += _slotDot.PulseIfShown;

            // Title room: the item less its left margin, icon (16 + 10), close button and pill.
            SizeChanged += (_, e) => tab.Header.TitleMaxWidth = e.NewSize.Width - 12 - 26 - 32;

            PointerEntered += (_, _) =>
            {
                _hovered = true;
                Restyle();
            };
            PointerExited += (_, _) =>
            {
                _hovered = false;
                Restyle();
            };
        }

        public event Action? CloseClicked;

        public bool Compact
        {
            set
            {
                _compact = value;
                _header.Compact = value;
                _header.Margin = new Thickness(value ? 0 : 12, 0, 0, 0);
                SetColumnSpan(_header, value ? 2 : 1);
                Restyle();
            }
        }

        private Color? _color;
        private Color _pane;

        public void Show(bool selected, Color? color, Color pane)
        {
            if (_shown == (selected, color, pane))
            {
                return;
            }

            _shown = (selected, color, pane);
            (_selected, _color, _pane) = (selected, color, pane);
            Restyle();
        }

        private void Restyle()
        {
            // Selected: the tab color, else the focused pane's background (as the title-bar tab
            // flows into its terminal). Others: a tint of their color, or a hover highlight.
            Background = _selected
                ? new SolidColorBrush(_color ?? _pane)
                : _color is { } c
                    ? new SolidColorBrush(Color.FromArgb(0x80, c.R, c.G, c.B))
                    : _hovered ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"] : _clear;
            _pill.Visibility = _selected ? Visibility.Visible : Visibility.Collapsed;
            // The rail has no room for either (its dot sits on the icon): middle click and the
            // context menu still close.
            _slotDot.Show(_compact ? Attention.None : _header.Attention, _header.AttentionCount);
            _slotDot.ShowProgress(_compact ? Phosphor.Terminal.TerminalProgress.None : _header.Progress);
            var dot = _slotDot.HasContent && !_hovered;
            var close = !_compact && (_hovered || (_selected && !_slotDot.HasContent));
            _slotDot.Opacity = dot ? 1 : 0; // hidden, not removed: showing it again mustn't re-pulse
            _close.Visibility = close ? Visibility.Visible : Visibility.Collapsed;
            _slot.Visibility = dot || close ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
