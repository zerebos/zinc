using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Zinc;

/// <summary>
/// Workspaces along the top: the attention dot takes the close button's place in the TabViewItem, as in
/// the sidebar and the pane tabs (one slot; hovering swaps them, so nothing moves). The close button
/// is in TabView's template, so the dot goes into the template's own grid, in the close button's cell;
/// the button turns transparent rather than collapsing, keeping the tab's width. Until the template is
/// there, or if it has no close button, the dot stays after the title (<see cref="TabHeader"/>).
/// </summary>
internal sealed class TabItemDot
{
    private readonly TabViewItem _item;
    private readonly TabHeader _header;
    private readonly AttentionDot _dot = new() { Corner = false, HorizontalAlignment = HorizontalAlignment.Center };
    private Button? _close;
    private bool _hovered;

    public TabItemDot(TabViewItem item, TabHeader header)
    {
        _item = item;
        _header = header;
        item.Loaded += (_, _) => Attach();
        item.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) => { _hovered = true; Update(); }), handledEventsToo: true);
        item.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => { _hovered = false; Update(); }), handledEventsToo: true);
        header.AttentionChanged += Update;
        header.PulseRequested += () => _dot.PulseIfShown();
    }

    /// <summary>Finds the template's close button and puts the dot in its cell (once; Loaded runs again on every reload).</summary>
    private void Attach()
    {
        if (_close != null)
        {
            return;
        }

        _close = Find<Button>(_item, "CloseButton");
        if (_close is null || VisualTreeHelper.GetParent(_close) is not Grid grid)
        {
            _close = null;
            return;
        }

        Grid.SetColumn(_dot, Grid.GetColumn(_close));
        Grid.SetRow(_dot, Grid.GetRow(_close));
        Grid.SetColumnSpan(_dot, Grid.GetColumnSpan(_close));
        _dot.VerticalAlignment = _close.VerticalAlignment;
        _dot.Margin = _close.Margin;
        grid.Children.Add(_dot);
        _header.DotElsewhere = true;
        Update();
    }

    private void Update()
    {
        if (_close == null)
        {
            return;
        }

        _dot.Show(_header.Attention, _header.AttentionCount);
        _dot.ShowProgress(_header.Progress);
        // The close button keeps its place (and still works when hovered); the dot covers it otherwise.
        var dot = _dot.HasContent && !_hovered;
        _dot.Visibility = dot ? Visibility.Visible : Visibility.Collapsed;
        _close.Opacity = dot ? 0 : 1;
    }

    private static T? Find<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (Find<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
