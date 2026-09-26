using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Phantom.Terminal;

// The scrollbar: mirrors libghostty's scrollback position and scrolls it when dragged.
public sealed partial class TerminalControl
{
    /// <summary>Width of the right-edge zone (DIPs) that expands the scrollbar on approach.</summary>
    private const double ScrollBarHotZone = 20;

    private ScrollBar? _scrollBar;
    private bool _updatingScrollBar;
    private bool _scrollBarExpanded;
    private bool _nearScrollBar;

    /// <summary>Creates the overlay scrollbar (called once from the constructor).</summary>
    private ScrollBar CreateScrollBar()
    {
        var bar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            // Thin indicator by default; full bar while the pointer is over it.
            IndicatorMode = ScrollingIndicatorMode.MouseIndicator,
            SmallChange = 1,
            Visibility = Visibility.Collapsed,
            IsTabStop = false,
        };
        bar.ValueChanged += (_, e) =>
        {
            if (_updatingScrollBar || _session is not { } session)
            {
                return;
            }

            session.ScrollToRow((ulong)Math.Max(0, Math.Round(e.NewValue)));
            _canvas.Invalidate();
        };
        _scrollBar = bar;
        return bar;
    }

    /// <summary>Syncs the scrollbar with the terminal (called every frame; the read is cheap).</summary>
    private void UpdateScrollBar(TerminalSession session)
    {
        if (_scrollBar is not { } bar)
        {
            return;
        }

        var sb = session.GetScrollbar();
        var max = sb.Total > sb.Len ? (double)(sb.Total - sb.Len) : 0;
        var visibility = max > 0 ? Visibility.Visible : Visibility.Collapsed;

        _updatingScrollBar = true;
        try
        {
            if (bar.Visibility != visibility)
            {
                bar.Visibility = visibility;
            }

            if (max > 0)
            {
                bar.Maximum = max;
                bar.ViewportSize = sb.Len;
                bar.LargeChange = Math.Max(1, sb.Len - 1);
                bar.Value = sb.Offset;
            }
        }
        finally
        {
            _updatingScrollBar = false;
        }
    }

    /// <summary>
    /// Expands the scrollbar while the pointer is over it or within the right-edge hot
    /// zone, so it doesn't have to be hit exactly; the pointer is an arrow there.
    /// </summary>
    private void TrackScrollBarProximity(object sender, PointerRoutedEventArgs e)
    {
        var near = _scrollBar is { Visibility: Visibility.Visible } &&
                   (IsFromScrollBar(e.OriginalSource) || e.GetCurrentPoint(this).Position.X >= ActualWidth - ScrollBarHotZone);
        _nearScrollBar = near;
        SetScrollBarExpanded(near);
        if (near)
        {
            UpdatePointerCursor(arrow: true);
        }
    }

    /// <summary>
    /// Switches the bar between its thin and full looks via the visual states a
    /// ScrollViewer uses. (IndicatorMode.None does not mean "full bar": it hides it.)
    /// </summary>
    private void SetScrollBarExpanded(bool expanded)
    {
        if (_scrollBar is { } bar && expanded != _scrollBarExpanded &&
            VisualStateManager.GoToState(bar, expanded ? "Expanded" : "Collapsed", true))
        {
            // GoToState fails until the template has applied; retry on the next move.
            _scrollBarExpanded = expanded;
        }
    }

    /// <summary>Whether a pointer event started on the scrollbar (so it isn't also a selection).</summary>
    private bool IsFromScrollBar(object? source)
    {
        for (var node = source as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node == _scrollBar)
            {
                return true;
            }

            if (node == this)
            {
                return false;
            }
        }

        return false;
    }
}
