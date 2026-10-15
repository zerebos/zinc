using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Zinc.Panes;

namespace Zinc;

// A narrow header's tabs: the others shorten (then the shown tab, down to the shortest), then become their icon and mark,
// then all fold at once into a count ("4 of 5", with the most urgent mark among them) that takes the +'s place.
// Which stage applies follows the width and the number of tabs only, so a title change never flips it.
// The count opens a menu of every tab with its status in words, then New tab in this pane. With pane-tabs-as = menu the
// tabs are folded at every width.
internal sealed partial class GroupView
{
    private readonly Button _count;
    private readonly TextBlock _countText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly AttentionDot _countDot = new() { Corner = false, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The pane's tabs in order (the last Update's).</summary>
    private List<MainWindow.TerminalTab> _order = [];

    private Button CountButton()
    {
        var count = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _countText, _countDot, new FontIcon { Glyph = "", FontSize = 8, Opacity = 0.75 } },
            },
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };
        SetColumn(count, 1);
        count.Click += (_, _) => ShowTabsMenu();
        return count;
    }

    /// <summary>The count's words and mark: where the shown tab is, and the most urgent mark among the others.</summary>
    private void ShowCount()
    {
        if (_shown == null || _order.Count == 0)
        {
            return;
        }

        var position = $"{_order.IndexOf(_shown) + 1} of {_order.Count}";
        var others = _order.Where(t => t != _shown).ToList();
        _countText.Text = position;
        _countDot.Show(others.Select(t => t.Attention).DefaultIfEmpty().Max());
        var words = AttentionWords.Summary([.. others.Select(t => (t.Attention, t.FailedExit))]);
        AutomationProperties.SetName(_count, AttentionWords.Name($"Tab {position}", words));
        ToolTipService.SetToolTip(_count, AttentionWords.ToolTip($"Tab {position}", words));
    }

    /// <summary>Fits the tabs in <paramref name="space"/> (theirs and the +'s): shortened, as icons, or folded into the count.</summary>
    private void ArrangeTabs(double space)
    {
        if (_header != PaneHeaderKind.Tabs || _shown == null || !_order.Contains(_shown) || _order.Any(t => !_items.ContainsKey(t)))
        {
            _count.Visibility = Visibility.Collapsed;
            _add.Visibility = _settings.PaneTabs ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var countWidth = MeasureWidth(_count);
        var fit = space > 0
            ? PaneHeader.FitTabs(space, [.. _order.Select(t => _items[t].TitleWidth)], _order.IndexOf(_shown),
                TabItem.Chrome, TabItem.IconWidth, AddWidth + 2, countWidth + 2, _settings.TabsAs == PaneTabsAs.Menu)
            : new TabsFit(TabsStage.Full, double.PositiveInfinity, double.PositiveInfinity);
        foreach (var tab in _order)
        {
            _items[tab].Fit(fit, tab == _shown);
        }

        var folded = fit.Stage == TabsStage.Folded;
        _count.Visibility = folded ? Visibility.Visible : Visibility.Collapsed;
        _add.Visibility = folded || !_settings.PaneTabs ? Visibility.Collapsed : Visibility.Visible; // the + is in the count's menu
        _tabs.MaxWidth = Math.Max(0, space - (folded ? countWidth : AddWidth) - 2);
    }

    /// <summary>An element's width as it would show (a collapsed one measures as 0).</summary>
    private static double MeasureWidth(FrameworkElement element)
    {
        var collapsed = element.Visibility == Visibility.Collapsed;
        element.Visibility = Visibility.Visible;
        element.Measure(new Windows.Foundation.Size(double.PositiveInfinity, StripHeight));
        var width = element.DesiredSize.Width;
        if (collapsed)
        {
            element.Visibility = Visibility.Collapsed;
        }

        return width;
    }

    /// <summary>The count's menu: every tab in order (the shown one checked) with its status in words, then New tab in this pane.</summary>
    private void ShowTabsMenu()
    {
        var menu = new MenuFlyout();
        foreach (var tab in _order)
        {
            var title = TitleOf(tab);
            var words = AttentionWords.For(tab.Attention, tab.FailedExit) is { } w
                ? char.ToUpperInvariant(w[0]) + w[1..]
                : PaneHeader.StatusWords(tab.Control.Progress, tab.Control.ExitCode);
            var item = new ToggleMenuFlyoutItem
            {
                Text = title,
                IsChecked = tab == _shown,
                Icon = new IconSourceElement { IconSource = ProfileIcons.For(tab.Profile) },
                KeyboardAcceleratorTextOverride = words ?? "", // the status, right-aligned where a shortcut would be
            };
            AutomationProperties.SetName(item, AttentionWords.Name(title, words));
            item.Click += (_, _) => TabClicked?.Invoke(tab);
            menu.Items.Add(item);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var add = new MenuFlyoutItem { Text = "New tab in this pane", Icon = new FontIcon { Glyph = "" } };
        add.Click += (_, _) => NewTabClicked?.Invoke();
        menu.Items.Add(add);
        menu.ShowAt(_count, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }
}
