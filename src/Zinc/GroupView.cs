using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;
using Windows.UI;

namespace Zinc;

/// <summary>
/// One pane: its header (when shown: the pane's tabs, or with one tab its title) over its shown tab's terminal. Each
/// tab's element is built once and updated in place, like <see cref="TabHeader"/>: rebuilding headers re-decodes icons
/// and makes them blink.
/// </summary>
internal sealed partial class GroupView : Grid
{
    public const double StripHeight = 28;
    private const double AddWidth = 30;

    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
    /// <summary>
    /// The header. Its empty part takes the pointer too (transparent, not no background). Hovering anywhere on it shows
    /// the buttons, a double-click there maximizes, and a tab dropped there joins the tabs.
    /// </summary>
    private readonly Grid _strip = new() { Height = StripHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TitleHeader _title = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _content = new();
    private readonly Dictionary<MainWindow.TerminalTab, TabItem> _items = new();
    private readonly Button _add;
    private PaneHeaderKind _header;

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

        // Columns: the tabs (or a one-tab title), the + after them (or the count of folded tabs), room, the status slot, the buttons.
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
        {
            _strip.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        _split = HeaderButton("", "Split right",
            () => HeaderAction?.Invoke(new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Right)));
        _maximize = HeaderButton("", "Maximize pane", () => HeaderAction?.Invoke(new ActionInvocation(TerminalAction.MaximizePane)), _maximizeIcon);
        _more = HeaderButton("", "More options", () => MoreRequested?.Invoke(_more!));
        _status.Children.Add(_statusRing);
        _status.Children.Add(_statusText);
        SetColumn(_status, 3);
        SetColumn(_buttons, 4);
        _buttons.Visibility = Visibility.Collapsed;
        SetColumn(add, 1);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        _count = CountButton();
        AddGrip();
        _strip.Children.Add(_tabs);
        _strip.Children.Add(_title);
        _strip.Children.Add(add);
        _strip.Children.Add(_count);
        _strip.Children.Add(_status);
        _strip.Children.Add(_buttons);
        // The tabs (or the title) take what they need, up to the width the + and the buttons leave. Tabs that don't fit shorten, then fold.
        _strip.SizeChanged += (_, _) => ArrangeHeader();
        // Buttons shown on hover come with the pointer over the header (decision 40).
        _strip.PointerEntered += (_, _) =>
        {
            _hovered = true;
            ApplyButtons();
        };
        _strip.PointerExited += (_, _) =>
        {
            _hovered = false;
            ApplyButtons();
        };
        _title.CloseClicked += () => WithShown(t => TabCloseClicked?.Invoke(t));
        _title.Selecting = () => WithShown(t => TabClicked?.Invoke(t));
        _title.Tapped += (_, e) =>
        {
            e.Handled = true;
            _title.SelectItem();
        };
        _title.RightTapped += (_, e) =>
        {
            e.Handled = true;
            WithShown(t => TabMenuRequested?.Invoke(t, _title));
        };
        _title.PointerReleased += (_, e) =>
        {
            // Middle click closes its tab, as it does on a tab.
            if (e.GetCurrentPoint(_title).Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased)
            {
                e.Handled = true;
                WithShown(t => TabCloseClicked?.Invoke(t));
            }
        };
        // Double-click anywhere on the header but its buttons maximizes the pane, or restores it. Not in a lone pane, where "Maximize" isn't offered (or needed tbf).
        _strip.DoubleTapped += (_, e) =>
        {
            if (_splitWorkspace && !OnButton(e.OriginalSource as DependencyObject))
            {
                e.Handled = true;
                HeaderAction?.Invoke(new ActionInvocation(TerminalAction.MaximizePane));
            }
        };
        // Dragging the title drags its tab, as a one-tab strip did.
        _title.CanDrag = true;
        _title.DragStarting += (_, e) => WithShown(t => TabDragStarting?.Invoke(t, e));
        _title.DropCompleted += (_, e) => WithShown(t => TabDragCompleted?.Invoke(t, e.DropResult));

        SetRow(_content, 1);
        SetRow(_dim, 1);
        SetRow(_focusLine, 1);
        SetRow(_paneDot, 1);
        SetRowSpan(_ring, 2);
        Children.Add(_strip);
        Children.Add(_content);
        Children.Add(_dim);
        Children.Add(_focusLine);
        Children.Add(_ring);
        Children.Add(_paneDot); // above the dimming: it's what you should notice

        // Dropping a tab anywhere on the header puts it between the tabs where it lands. With pane tabs off a pane holds one tab, so drops go on to the pane's edges below.
        _strip.Children.Add(_marker);
        _strip.AllowDrop = true;
        _strip.DragOver += (_, e) =>
        {
            // If pane tabs are off, we don't handle drag here.
            // That would be silly, and definitely not a latent bug I forgot to handle...
            if (!_settings.PaneTabs)
            {
                return;
            }

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
            // If pane tabs are off, we don't handle drops here.
            // That would be silly, and definitely not a latent bug I forgot to handle...
            if (!_settings.PaneTabs)
            {
                return;
            }

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

    /// <summary>
    /// The ring around a flat pane (gap 0): drawn inside its edge, over the terminal's padding and the header.
    /// A card's ring is its own edge (<c>MainWindow.ShowCard</c>).
    /// </summary>
    private readonly Border _ring = new()
    {
        BorderThickness = new Thickness(PaneCard.Edge),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
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

    /// <summary>
    /// The drop position under the pointer: how many tabs lie left of it. Folded tabs count where they sit in order, so
    /// a drop beside the shown tab lands next to it.
    /// </summary>
    private int IndexAt(DragEventArgs e)
    {
        var x = e.GetPosition(_strip).X;
        var items = HeaderItems();
        var after = 0; // past the last tab shown left of the pointer
        for (var i = 0; i < items.Count; i++)
        {
            var child = items[i];
            if (child.Visibility != Visibility.Visible)
            {
                continue;
            }

            var left = child.TransformToVisual(_strip).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
            if (x < left + child.ActualWidth / 2)
            {
                return i;
            }

            after = i + 1;
        }

        return items.Any(c => c.Visibility == Visibility.Visible) ? after : items.Count;
    }

    /// <summary>Whether <paramref name="element"/> is in one of the header's buttons (a tab's ×, the +, the count, Split…).</summary>
    private bool OnButton(DependencyObject? element)
    {
        for (; element != null && element != _strip; element = VisualTreeHelper.GetParent(element))
        {
            if (element is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The header's tabs, left to right: the title counts as the one tab.</summary>
    private List<FrameworkElement> HeaderItems() =>
        _header == PaneHeaderKind.Title ? [_title] : _tabs.Children.OfType<FrameworkElement>().ToList();

    private void ShowMarker(int index)
    {
        // The left edge of the first tab shown from the position on, else the right edge of the last one shown
        var shown = HeaderItems().Select((item, i) => (item, i)).Where(p => p.item.Visibility == Visibility.Visible).ToList();
        double x;
        if (shown.Count == 0)
        {
            x = 0;
        }
        else if (shown.FirstOrDefault(p => p.i >= index).item is { } next)
        {
            x = next.TransformToVisual(_strip).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        }
        else
        {
            var last = shown[^1].item;
            x = last.TransformToVisual(_strip).TransformPoint(new Windows.Foundation.Point(last.ActualWidth, 0)).X;
        }

        _marker.Margin = new Thickness(Math.Max(0, x - 1), 0, 0, 0);
        _marker.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Shows the group's current state: its header (<paramref name="header"/>: its tabs in order, the active one
    /// highlighted, or its one tab's title), and the active tab's terminal below. The focused pane gets the accent line
    /// (<see cref="PaneHeader.AccentLine"/>): on its shown tab or its title header (drawn as the tab), else along its top when
    /// there are other panes (<paramref name="several"/>), where unfocused panes can also be dimmed toward
    /// <paramref name="dimFill"/> (<paramref name="dim"/> = how much, 0 = not at all). The + (a new tab here) shows
    /// only with pane tabs on; the buttons and status slot per <paramref name="settings"/>, Maximize only when the
    /// workspace is <paramref name="split"/> (a restore button while this pane is <paramref name="maximized"/>). A card
    /// (gap above 0) has its header filled with <paramref name="cardFill"/>; null keeps panes flat. With pane-focus = ring
    /// the focused pane gets the ring instead of the line (<see cref="PaneHeader.Ring"/>): drawn here on a flat pane, by
    /// the window on a card's edge.
    /// </summary>
    public void Update(TabGroup<MainWindow.TerminalTab> group, PaneHeaderKind header, HeaderSettings settings, bool focused,
        Brush accent, bool split = false, bool several = false, bool maximized = false, double dim = 0, Color dimFill = default,
        Color? cardFill = null)
    {
        _header = header;
        _settings = settings;
        (_focused, _splitWorkspace, _maximized) = (focused, split, maximized);
        _add.Visibility = settings.PaneTabs ? Visibility.Visible : Visibility.Collapsed; // with pane tabs off a title has no +
        var showStrip = header != PaneHeaderKind.None;
        _strip.Visibility = showStrip ? Visibility.Visible : Visibility.Collapsed;
        var titled = header == PaneHeaderKind.Title;
        _title.Visibility = titled ? Visibility.Visible : Visibility.Collapsed;
        _tabs.Visibility = titled ? Visibility.Collapsed : Visibility.Visible;

        // The status slot shows the shown terminal's progress where buttons can show; its tab's dot then doesn't (decision 44).
        var buttons = settings.Buttons != PaneHeaderButtons.Hide;
        _progress = buttons && showStrip ? group.Active.Control.Progress : TerminalProgress.None;
        _exitCode = buttons && showStrip ? group.Active.Control.ExitCode : null;
        ApplyButtons();

        var accentLine = PaneHeader.AccentLine(settings.Focus, header, focused, several);
        // A tab (the title header too) carries its own line; a pane without a header has it over the terminal's top edge.
        var line = accentLine && header == PaneHeaderKind.None;
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

        ShowCard(cardFill);
        var ring = cardFill == null && PaneHeader.Ring(settings.Focus, focused, several);
        _ring.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        if (ring)
        {
            // Kept visible on the terminal's background, like the accent line
            var ringBrush = Contrast.AccentOn(TerminalRenderer.ToColor(group.Active.Profile.Config.Theme.Background), accent);
            if (_ring.BorderBrush != ringBrush)
            {
                _ring.BorderBrush = ringBrush;
            }
        }

        var dimmed = several && !focused && dim > 0;
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

        if (titled)
        {
            _title.Show(group.Active, HeaderTitleOf(group.Active), accentLine, accent, closeHere: true, // its own ×, as a tab's
                _progress.Active ? TerminalProgress.None : group.Active.Control.Progress);
            _shown = group.Active;
            ArrangeHeader();
            return;
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
            var shownTab = tab == group.Active;
            _items[tab].Show(TitleOf(tab), tab.TitleIsFolder, shownTab, accentLine, accent, tab.Attention,
                shownTab && _progress.Active ? TerminalProgress.None : tab.Control.Progress,
                closeOnHover: false); // every tab keeps its ×: hover-only waits for the filled header (session C, decision 39)
        }

        _shown = group.Active;
        _order = [.. group.Tabs];
        ShowCount();
        ArrangeHeader(); // the tabs' width may have changed
    }

    /// <summary>
    /// A card's header is filled (still hit-testable when it isn't: transparent, not none) and themed to match, its top
    /// corners follow the card's inside its edge (and so does the pane drag's picture of it), and dimming covers the whole card (decision 22).
    /// </summary>
    private void ShowCard(Color? fill)
    {
        var color = fill ?? Microsoft.UI.Colors.Transparent;
        if (_strip.Background is not SolidColorBrush { } brush || brush.Color != color)
        {
            _strip.Background = new SolidColorBrush(color);
        }

        // The header's own text and glyphs (hidden tabs, +, buttons) follow the fill, not the window: dark on a light scheme's.
        // The shown tab's text keeps following its background (TextOn), and inherits this where it clears its own.
        var theme = fill is { } f ? Contrast.IsLight(f) ? ElementTheme.Light : ElementTheme.Dark : ElementTheme.Default;
        if (_strip.RequestedTheme != theme)
        {
            _strip.RequestedTheme = theme;
        }

        var inner = PaneCard.Radius - PaneCard.Edge;
        _strip.CornerRadius = fill != null ? new CornerRadius(inner, inner, 0, 0) : default;
        SetRow(_dim, fill != null ? 0 : 1);
        SetRowSpan(_dim, fill != null ? 2 : 1);
    }

    /// <summary>Measures header text (12 px, as titles show): what fits is decided before it's shown.</summary>
    private static readonly TextBlock Measurer = new() { FontSize = 12 };

    private static double TextWidth(string text)
    {
        Measurer.Text = text;
        Measurer.Measure(new Windows.Foundation.Size(double.PositiveInfinity, StripHeight));
        return Measurer.DesiredSize.Width;
    }

    /// <summary>The shown tab (the title header's, and the one menus and drags from it act on).</summary>
    private MainWindow.TerminalTab? _shown;

    private void WithShown(Action<MainWindow.TerminalTab> action)
    {
        if (_shown is { } tab)
        {
            action(tab);
        }
    }

    /// <summary>
    /// The element showing <paramref name="tab"/> in the header (its tab, the title, or the count it's folded into), if the
    /// header shows it.
    /// </summary>
    public FrameworkElement? AnchorFor(MainWindow.TerminalTab tab) => _header switch
    {
        PaneHeaderKind.Title when tab == _shown => _title,
        PaneHeaderKind.Tabs when _items.TryGetValue(tab, out var item) => item.Visibility == Visibility.Visible ? item : _count,
        _ => null,
    };

    /// <summary>Lets go of the shown terminal (the group is moving to another window).</summary>
    public void Release() => _content.Child = null;

    /// <summary>The + button's tooltip (the window's, which knows the current key bindings).</summary>
    public void SetAddToolTip(ToolTip tip) => ToolTipService.SetToolTip(_add, tip);

    public static string TitleOf(MainWindow.TerminalTab tab) => tab.Title;

    /// <summary>A one-tab header's title: a name given to the tab wins, else the folder, command or program (decision 19).</summary>
    private static HeaderTitle HeaderTitleOf(MainWindow.TerminalTab tab) =>
        tab.CustomTitle is { } custom ? new HeaderTitle(custom, null) : tab.Control.HeaderTitle(tab.Profile.Name);

    private TabItem Item(MainWindow.TerminalTab tab)
    {
        if (!_items.TryGetValue(tab, out var item))
        {
            item = new TabItem(tab) { Selecting = () => TabClicked?.Invoke(tab) };
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

    /// <summary>
    /// A one-tab pane's header: the tab's icon, title and attention dot, drawn as the shown tab (its terminal's
    /// background, joining the terminal below), as a one-tab strip was (decision 36). It is the tab, so it selects,
    /// opens the tab menu and drags as one, and carries the accent line as a tab does.
    /// </summary>
    private sealed partial class TitleHeader : Grid, ISelectableItem
    {
        private readonly IconSourceElement _icon = new();
        private readonly TextBlock _text = new()
        {
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>The quieter text after the title (the profile at the prompt, the folder while a command runs).</summary>
        private readonly TextBlock _detail = new()
        {
            FontSize = 12,
            Opacity = 0.6,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>The tab itself (a Cards header may later stretch around it: session C).</summary>
        private readonly Grid _tab = new() { CornerRadius = new CornerRadius(6, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };

        private readonly AttentionDot _dot = new() { Corner = false, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly Button _close;
        private readonly Border _accent = new() { Height = 2, VerticalAlignment = VerticalAlignment.Top };
        private readonly Brush _clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        private SolidColorBrush? _background;
        private Profile? _profile;
        private string? _name;
        private bool _closeHere, _hovered;

        public TitleHeader()
        {
            Background = _clear; // hit-testable
            var icon = new Grid
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _icon },
            };
            // The tab's ×, sharing its slot with the dot as a tab's does: the dot until you hover, then the ×.
            _close = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 9 },
                Padding = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = _clear,
                BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(_close, "Close tab");
            _close.Click += (_, _) => CloseClicked?.Invoke();
            var slot = new Grid { Width = 20, Margin = new Thickness(6, 0, 0, 0), Children = { _close, _dot } };

            // A tab's left padding, so the icon sits where a tab's would. The detail's column gives way first when
            // narrow; the title's is capped (SetAvailable), since an Auto column never trims.
            var row = new Grid { Padding = new Thickness(10, 0, 4, 0) };
            foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            }

            SetColumn(_text, 1);
            SetColumn(_detail, 2);
            SetColumn(slot, 3);
            row.Children.Add(icon);
            row.Children.Add(_text);
            row.Children.Add(_detail);
            row.Children.Add(slot);
            _tab.Children.Add(row);
            _tab.Children.Add(_accent);
            Children.Add(_tab);

            PointerEntered += (_, _) =>
            {
                _hovered = true;
                ShowSlot();
            };
            PointerExited += (_, _) =>
            {
                _hovered = false;
                ShowSlot();
            };
        }

        public event Action? CloseClicked;

        /// <summary>The width the tab may take: the title keeps room for the icon, padding and slot beside it.</summary>
        public void SetAvailable(double width)
        {
            _tab.MaxWidth = width;
            _text.MaxWidth = Math.Max(40, width - 70);
            if (_available != width)
            {
                _available = width;
                ShowText();
            }
        }

        private double _available = double.PositiveInfinity;
        private HeaderTitle? _title;

        /// <summary>
        /// The title as it fits: a folder loses its start, not its end ("…/BetterDiscord/betterdiscord.app"), dropping
        /// leading folders only as far as the width needs. The lead first, the quieter text in what's left.
        /// </summary>
        private void ShowText()
        {
            if (_title is not { } title)
            {
                return;
            }

            var room = _text.MaxWidth;
            var lead = title.LeadIsFolder ? ShellTitle.FitFolder(title.Lead, room, TextWidth) : title.Lead;
            _text.Text = lead;
            if (title.Detail is { } detail)
            {
                var left = room - Math.Min(room, TextWidth(lead)) - _detail.Margin.Left;
                _detail.Text = title.DetailIsFolder ? ShellTitle.FitFolder(detail, left, TextWidth) : detail;
            }

            _detail.Visibility = title.Detail == null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>What a click does (set by the view), for screen readers selecting it.</summary>
        public Action? Selecting { get; set; }

        public bool IsSelectedItem => true;

        public void SelectItem() => Selecting?.Invoke();

        protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
            new SelectableItemPeer(this, Microsoft.UI.Xaml.Automation.Peers.AutomationControlType.TabItem);

        private void ShowSlot()
        {
            var dot = _dot.HasContent && (!_hovered || !_closeHere);
            _dot.Opacity = dot ? 1 : 0; // hidden, not removed: showing it again mustn't re-pulse
            _close.Visibility = _closeHere && !dot ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <param name="closeHere">The tab carries its own × (until session C decides whether Cards moves it to the buttons).</param>
        /// <param name="progress">The progress its dot shows (none while the header's status slot shows it).</param>
        public void Show(MainWindow.TerminalTab tab, HeaderTitle title, bool accentLine, Brush accent, bool closeHere, TerminalProgress progress)
        {
            if (_profile != tab.Profile)
            {
                _profile = tab.Profile; // only on a change: setting an icon re-decodes it
                _icon.IconSource = ProfileIcons.For(tab.Profile);
            }

            _closeHere = closeHere;
            _dot.Show(tab.Attention);
            _dot.ShowProgress(progress);
            ShowSlot();
            var words = AttentionWords.For(tab.Attention, tab.FailedExit);
            var name = AttentionWords.Name(title.Detail == null ? title.Lead : $"{title.Lead}, {title.Detail}", words);
            if (title != _title)
            {
                _title = title;
                ShowText();
            }

            if (name != _name)
            {
                _name = name;
                AutomationProperties.SetName(this, name);
                var tip = title.Detail == null ? title.Lead : $"{title.Lead}  ·  {title.Detail}";
                ToolTipService.SetToolTip(this, AttentionWords.ToolTip(tip, words));
            }

            // The terminal's background (it can change: a config reload), and dark text on a light one.
            var background = TerminalRenderer.ToColor(tab.Profile.Config.Theme.Background);
            if (_background?.Color != background)
            {
                _background = new SolidColorBrush(background);
                _tab.Background = _background;
                var dark = Contrast.TextOn(background);
                foreach (var block in new[] { _text, _detail })
                {
                    if (dark is { } text)
                    {
                        block.Foreground = text;
                    }
                    else
                    {
                        block.ClearValue(TextBlock.ForegroundProperty);
                    }
                }

                if (dark is { } closeText)
                {
                    _close.Foreground = closeText;
                }
                else
                {
                    _close.ClearValue(Control.ForegroundProperty);
                }
            }

            _accent.Background = accentLine ? Contrast.AccentOn(background, accent) : _clear;
        }
    }

    /// <summary>One tab in the strip: icon, title, close button.</summary>
    private sealed partial class TabItem : Grid, ISelectableItem
    {
        // TODO: test and tune these over time
        private const double LongestTitle = 180;

        /// <summary>The narrowest title worth showing (the shown tab beside the count).</summary>
        private const double MinTitle = 24;

        /// <summary>A tab's width beside its title: padding, icon, the dot/× slot.</summary>
        public const double Chrome = 10 + 14 + 8 + 6 + 20 + 4;

        /// <summary>A tab shown as its icon and mark (<see cref="TabsStage.Icons"/>).</summary>
        public const double IconWidth = 10 + 14 + 6 + 20 + 4;

        private readonly TextBlock _title = new()
        {
            MaxWidth = LongestTitle,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private readonly Border _accent = new() { Height = 2, VerticalAlignment = VerticalAlignment.Top };
        private readonly MainWindow.TerminalTab _tab;
        private readonly AttentionDot _dot = new() { Corner = false };
        private readonly Button _close;
        private readonly Grid _icon;
        private SolidColorBrush _background;
        private readonly Brush _clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        private bool _active, _hovered, _closeOnHover;
        private Attention _attention;
        private string? _name;
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

            var icon = _icon = new Grid
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

        /// <summary>The title's width in full (up to the longest a tab shows).</summary>
        public double TitleWidth { get; private set; }

        /// <summary>
        /// Shows the tab as the header's fit says (<see cref="PaneHeader.FitTabs"/>): its title capped (the shown tab's
        /// last), as its icon and mark (the title stays in the tooltip; the × stays, as on every tab), or folded.
        /// </summary>
        public void Fit(TabsFit fit, bool shown)
        {
            var folded = fit.Stage == TabsStage.Folded && !shown;
            Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
            var icon = fit.Stage == TabsStage.Icons && !shown;
            var cap = Math.Min(LongestTitle, shown ? fit.ShownCap : fit.OthersCap);
            var titled = !icon && cap >= MinTitle; // a sliver of a title says nothing: the shown tab keeps its icon
            _title.Visibility = titled ? Visibility.Visible : Visibility.Collapsed;
            _icon.Margin = new Thickness(0, 0, titled ? 8 : 0, 0);
            _title.MaxWidth = cap;
            if (cap != _cap)
            {
                _cap = cap;
                ShowTitle();
            }
        }

        private string _fullTitle = "";
        private bool _folder;
        private double _cap = LongestTitle;

        /// <summary>The label as it fits its cap: a folder loses its start, not its end, as a title header's does.</summary>
        private void ShowTitle() =>
            _title.Text = _folder ? ShellTitle.FitFolder(_fullTitle, _cap, TextWidth) : _fullTitle;

        /// <summary>What a click does (set by the list), for screen readers selecting it.</summary>
        public Action? Selecting { get; set; }

        public bool IsSelectedItem => _active;

        public void SelectItem() => Selecting?.Invoke();

        protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
            new SelectableItemPeer(this, Microsoft.UI.Xaml.Automation.Peers.AutomationControlType.TabItem);

        private void ShowSlot()
        {
            _dot.Show(_attention);
            _dot.ShowProgress(_progress);
            var dot = _dot.HasContent && !_hovered;
            _dot.Opacity = dot ? 1 : 0; // hidden, not removed: showing it again mustn't re-pulse
            // Where the header has buttons, only the shown tab keeps its ×; the others show one under the pointer.
            // Elsewhere every tab keeps it, as before.
            var close = !dot && (!_closeOnHover || _active || _hovered);
            _close.Visibility = close ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <param name="accentLine">The tab carries the accent line (its pane is focused: <see cref="PaneHeader.AccentLine"/>).</param>
        /// <param name="folder">The title is the shell's folder.</param>
        /// <param name="closeOnHover">A hidden tab shows its × only under the pointer.</param>
        public void Show(string title, bool folder, bool active, bool accentLine, Brush accent, Attention attention,
            TerminalProgress progress, bool closeOnHover = false)
        {
            _attention = attention;
            _progress = progress;
            _active = active;
            _closeOnHover = closeOnHover;
            ShowSlot();
            // The title, and what its dot means, for the tooltip and screen readers.
            var words = AttentionWords.For(attention, _tab.FailedExit);
            if (_fullTitle != title || folder != _folder)
            {
                if (_fullTitle != title)
                {
                    TitleWidth = Math.Min(LongestTitle, Math.Ceiling(TextWidth(title)));
                }

                (_fullTitle, _folder) = (title, folder);
                ShowTitle();
            }

            var name = AttentionWords.Name(title, words);
            if (name != _name)
            {
                _name = name;
                AutomationProperties.SetName(this, name);
                ToolTipService.SetToolTip(this, AttentionWords.ToolTip(title, words));
            }

            // The profile's background can change (a config reload).
            var background = TerminalRenderer.ToColor(_tab.Profile.Config.Theme.Background);
            if (_background.Color != background)
            {
                _background = new SolidColorBrush(background);
            }

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
            _accent.Background = active && accentLine ? Contrast.AccentOn(background, accent) : _clear;
        }
    }
}
