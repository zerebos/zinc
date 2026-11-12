using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

/// <summary>The settings a pane's header follows (<c>pane-focus</c>, <c>pane-header-buttons</c>, <c>pane-tabs</c>).</summary>
internal readonly record struct HeaderSettings(PaneFocus Focus, PaneHeaderButtons Buttons, bool PaneTabs, PaneTabsAs TabsAs = PaneTabsAs.Tabs);

// The header's right end holds the status slot (the shown terminal's progress) and the buttons (Split, Maximize, ⋯, and
// Close pane with pane tabs off), as pane-header-buttons says. Both show only where buttons can show, and hidden buttons
// keep their width so the tabs don't move under the pointer. In a narrow header Split and Maximize fold into ⋯, then the
// status drops its words (PaneHeader.Fit). With pane tabs on, every tab keeps its ×, a one-tab header's × is on its tab
// and the + follows the tab, in card and compact. With them off a pane never grows a second tab, so its × moves to the end
// of the buttons. With buttons hidden there is no ×, and middle-click, the tab menu, keys and the palette close the pane.
internal sealed partial class GroupView
{
    private const double ButtonWidth = 28;

    /// <summary>
    /// The room the title or tabs keep before the buttons fold. A title needs this much, and tabs need their full width.
    /// </summary>
    private const double TitleRoom = 160;

    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _split, _maximize, _more, _close;
    private readonly FontIcon _maximizeIcon = new() { Glyph = "", FontSize = 12 };
    private ToolTip? _maximizeTip, _restoreTip;

    private readonly StackPanel _status = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Margin = new Thickness(6, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>
    /// The status's icon, which is the progress ring or an error exit's mark. It stays still because it's status, not news.
    /// </summary>
    private readonly AttentionDot _statusRing = new() { Corner = false, Steady = true, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The status words. They are at least as wide as "100%", so a counting percent doesn't move the slot.</summary>
    private readonly TextBlock _statusText = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };

    private HeaderSettings _settings;
    private bool _focused, _hovered, _splitWorkspace, _maximized;
    private TerminalProgress _progress = TerminalProgress.None;
    private uint? _exitCode;

    /// <summary>
    /// A header button asked for an action on this pane (Split, Maximize). The window runs it as its key would.
    /// </summary>
    public event Action<ActionInvocation>? HeaderAction;

    /// <summary>⋯ was clicked, so the window shows the pane's menu at that button.</summary>
    public event Action<FrameworkElement>? MoreRequested;

    private Button HeaderButton(string glyph, string name, Action click, FontIcon? icon = null)
    {
        var button = new Button
        {
            Content = icon ?? new FontIcon { Glyph = glyph, FontSize = 12 },
            Width = ButtonWidth,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            IsTabStop = false, // the terminal keeps the keyboard, and every action has a key and a palette entry
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => click();
        _buttons.Children.Add(button);
        return button;
    }

    /// <summary>
    /// Sets the buttons' tooltips with their shortcuts. The window supplies them because it knows the key bindings.
    /// </summary>
    public void SetButtonToolTips(ToolTip split, ToolTip maximize, ToolTip restore, ToolTip close)
    {
        ToolTipService.SetToolTip(_split, split);
        ToolTipService.SetToolTip(_close, close);
        (_maximizeTip, _restoreTip) = (maximize, restore);
        ShowMaximizeState();
    }

    private void ShowMaximizeState()
    {
        var name = _maximized ? "Restore panes" : "Maximize pane";
        _maximizeIcon.Glyph = _maximized ? "" : "";
        AutomationProperties.SetName(_maximize, name);
        ToolTipService.SetToolTip(_maximize, (_maximized ? _restoreTip : _maximizeTip) ?? (object)name);
    }

    /// <summary>Shows or hides the buttons and the status slot, and folds what doesn't fit.</summary>
    private void ApplyButtons()
    {
        var available = _settings.Buttons != PaneHeaderButtons.Hide && _header != PaneHeaderKind.None;
        _buttons.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        var shown = PaneHeader.ButtonsShown(_settings.Buttons, _focused, _hovered);
        _buttons.Opacity = shown ? 1 : 0; // hidden, not removed, so the tabs keep their place
        _buttons.IsHitTestVisible = shown;
        _close.Visibility = !_settings.PaneTabs && _header == PaneHeaderKind.Title ? Visibility.Visible : Visibility.Collapsed;
        ShowMaximizeState();

        // An ended program's exit wins over progress.
        var words = available ? PaneHeader.StatusWords(_progress, _exitCode) : null;
        _status.Visibility = words != null ? Visibility.Visible : Visibility.Collapsed;
        var exited = words != null && _exitCode != null;
        _statusRing.Show(exited && _exitCode != 0 ? Attention.Exited : Attention.None);
        _statusRing.ShowProgress(words != null && !exited ? _progress : TerminalProgress.None);
        _statusText.Text = words ?? "";
        if (_statusText.MinWidth == 0 && words != null)
        {
            var widest = new TextBlock { FontSize = _statusText.FontSize, Text = "100%" };
            widest.Measure(new Windows.Foundation.Size(double.PositiveInfinity, StripHeight));
            _statusText.MinWidth = Math.Ceiling(widest.DesiredSize.Width);
        }

        AutomationProperties.SetName(_status, words ?? "");
        ToolTipService.SetToolTip(_status, words);
        ArrangeHeader();
    }

    /// <summary>Lays out the header for its width: what folds when narrow, and how much the tabs or title may take.</summary>
    private void ArrangeHeader()
    {
        // Use the width the pane gives the header (its layout slot) instead of the header's own width. A header whose
        // title was capped for a wider pane stays that wide until the title is capped again, so capping from its own
        // width only takes 2 px off per layout round, and a big shrink would run out of rounds and end in a layout cycle.
        var slot = LayoutInformation.GetLayoutSlot(_strip).Width;
        var width = slot > 0 ? slot : _strip.ActualWidth;
        // The + (or the count that folded tabs put in its place). Its 2 px margin is the `- 2` in `room` below.
        var add = _settings.PaneTabs ? AddWidth : 0;
        var buttons = _buttons.Visibility == Visibility.Visible;
        var foldable = buttons ? ButtonWidth + 2 + (_splitWorkspace ? ButtonWidth + 2 : 0) : 0;
        var close = buttons && _close.Visibility == Visibility.Visible ? ButtonWidth + 2 : 0;
        var fixedWidth = add + (buttons ? ButtonWidth + 2 : 0) + close; // the +, ⋯ and Close
        double words = 0;
        if (_status.Visibility == Visibility.Visible)
        {
            _statusText.Visibility = Visibility.Visible;
            _statusText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, StripHeight));
            words = _statusText.DesiredSize.Width + _status.Spacing;
            fixedWidth += 12 + 12; // the ring and the slot's margins
        }

        double content;
        if (_header == PaneHeaderKind.Title)
        {
            content = TitleRoom;
        }
        else if (_settings.TabsAs == PaneTabsAs.Menu)
        {
            // Only the shown tab, and the count in the +'s place (counted in fixedWidth).
            content = _shown != null && _items.TryGetValue(_shown, out var item) ? TabItem.Chrome + item.TitleWidth : 0;
        }
        else
        {
            // Split and Maximize fold before the tabs shorten.
            content = _order.Sum(t => _items.TryGetValue(t, out var item) ? TabItem.Chrome + item.TitleWidth : 0);
        }

        var fit = width > 0 ? PaneHeader.Fit(width, content, fixedWidth, foldable, words) : new HeaderFit(false, true);
        _split.Visibility = fit.Fold ? Visibility.Collapsed : Visibility.Visible;
        // Nothing to maximize in a lone pane.
        _maximize.Visibility = !fit.Fold && _splitWorkspace ? Visibility.Visible : Visibility.Collapsed;
        _statusText.Visibility = fit.Words ? Visibility.Visible : Visibility.Collapsed;

        // What's left for the tabs (they shorten and fold to fit it) or the title (it trims).
        var used = fixedWidth + (fit.Fold ? 0 : foldable) + (fit.Words ? words : 0);
        var room = Math.Max(0, width - used - 2);
        _title.SetAvailable(room);
        ArrangeTabs(width > 0 ? room + add : 0);
    }
}
