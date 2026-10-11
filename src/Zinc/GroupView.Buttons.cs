using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

/// <summary>The settings a pane's header follows (<c>pane-focus</c>, <c>pane-header-buttons</c>, <c>pane-tabs</c>).</summary>
internal readonly record struct HeaderSettings(PaneFocus Focus, PaneHeaderButtons Buttons, bool PaneTabs);

// The header's right end: the status slot (the shown terminal's progress) and the buttons (Split, Maximize, ⋯, and with
// one tab Close), per pane-header-buttons. Both only where buttons can show (decision 37); hidden buttons keep their
// width so the tabs and title don't move under the pointer. In a narrow header Split and Maximize fold into ⋯, then the
// status drops its words (PaneHeader.Fit).
internal sealed partial class GroupView
{
    private const double ButtonWidth = 28;

    /// <summary>What the title or tabs keep before the buttons fold: a title needs this much, tabs their full width.</summary>
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

    private readonly AttentionDot _statusRing = new() { Corner = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusText = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };

    private HeaderSettings _settings;
    private bool _focused, _hovered, _splitWorkspace, _maximized;
    private TerminalProgress _progress = TerminalProgress.None;

    /// <summary>A header button asked for an action on this pane (Split, Maximize; the window runs it as its key would).</summary>
    public event Action<ActionInvocation>? HeaderAction;

    /// <summary>⋯ was clicked: the window shows the pane's menu at it.</summary>
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
            IsTabStop = false, // the terminal keeps the keyboard; every action has a key and a palette entry
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => click();
        _buttons.Children.Add(button);
        return button;
    }

    /// <summary>The buttons' tooltips with their shortcuts (the window's, which knows the key bindings).</summary>
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
        _buttons.Opacity = shown ? 1 : 0; // hidden, not removed: the tabs and title keep their place
        _buttons.IsHitTestVisible = shown;
        _close.Visibility = _header == PaneHeaderKind.Title ? Visibility.Visible : Visibility.Collapsed;
        _maximize.Visibility = _splitWorkspace ? Visibility.Visible : Visibility.Collapsed; // nothing to maximize in a lone pane
        ShowMaximizeState();

        var words = available ? PaneHeader.StatusWords(_progress) : null;
        _status.Visibility = words != null ? Visibility.Visible : Visibility.Collapsed;
        _statusRing.ShowProgress(words != null ? _progress : TerminalProgress.None);
        _statusText.Text = words ?? "";
        AutomationProperties.SetName(_status, words ?? "");
        ToolTipService.SetToolTip(_status, words);
        ArrangeHeader();
    }

    /// <summary>
    /// Lays out the header for its width: the + after the tabs or, on a one-tab header, before the buttons (decision 35);
    /// what folds when narrow; how much the tabs or title may take.
    /// </summary>
    private void ArrangeHeader()
    {
        var width = _strip.ActualWidth;
        var titled = _header == PaneHeaderKind.Title;
        // The title stretches to the buttons (its whole width drags, shows the tooltip and opens the menu); tabs take
        // what they need and the spacer the rest.
        _strip.ColumnDefinitions[0].Width = titled ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        _strip.ColumnDefinitions[2].Width = titled ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        SetColumn(_add, titled ? 4 : 1);

        var add = _add.Visibility == Visibility.Visible ? AddWidth : 0; // its 2 px margin is the room's - 2 below
        var buttons = _buttons.Visibility == Visibility.Visible;
        var foldable = buttons ? ButtonWidth + 2 + (_splitWorkspace ? ButtonWidth + 2 : 0) : 0;
        var fixedWidth = add + (buttons ? (ButtonWidth + 2) * (titled ? 2 : 1) : 0);
        double words = 0;
        if (_status.Visibility == Visibility.Visible)
        {
            _statusText.Visibility = Visibility.Visible;
            _statusText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, StripHeight));
            words = _statusText.DesiredSize.Width + _status.Spacing;
            fixedWidth += 12 + 12; // the ring and the slot's margins
        }

        double content;
        if (titled)
        {
            content = TitleRoom;
        }
        else
        {
            _tabs.Measure(new Windows.Foundation.Size(double.PositiveInfinity, StripHeight));
            content = _tabs.DesiredSize.Width;
        }

        var fit = width > 0 ? PaneHeader.Fit(width, content, fixedWidth, foldable, words) : new HeaderFit(false, true);
        _split.Visibility = fit.Fold ? Visibility.Collapsed : Visibility.Visible;
        if (fit.Fold)
        {
            _maximize.Visibility = Visibility.Collapsed;
        }

        _statusText.Visibility = fit.Words ? Visibility.Visible : Visibility.Collapsed;

        // What's left for the tabs (they scroll past it) or the title (it trims).
        var used = fixedWidth + (fit.Fold ? 0 : foldable) + (fit.Words ? words : 0);
        var room = Math.Max(0, width - used - 2);
        _scroll.MaxWidth = room;
        _title.SetAvailable(room);
    }
}
