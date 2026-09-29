using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Phosphor.Terminal;

// The scrollbar: mirrors libghostty's scrollback position and scrolls it when dragged.
public sealed partial class TerminalControl
{
    /// <summary>
    /// The expanded bar's width (DIPs; TerminalScrollBar.xaml), which is also where the pointer
    /// expands it, as in Windows Terminal: anywhere the full bar will be.
    /// </summary>
    private const double ScrollBarWidth = 16;

    private ScrollBar? _scrollBar;
    private bool _updatingScrollBar;
    private bool _scrollBarExpanded;
    private bool _nearScrollBar;
    private bool _scrollBarPinned;

    /// <summary>A button is held on the bar (dragging the thumb, paging, an arrow): it stays expanded.</summary>
    private bool _scrollBarHeld;

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
            // Windows Terminal's proportions: 16 DIPs, square, a 10-DIP thumb (TerminalScrollBar.xaml).
            Style = (Style)Application.Current.Resources["TerminalScrollBarStyle"],
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
        // Held down on the bar, it stays expanded wherever the pointer goes (dragging the thumb
        // along the bar, or off it); released, it's where the pointer is that counts again.
        bar.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _scrollBarHeld = true), handledEventsToo: true);
        bar.AddHandler(PointerReleasedEvent, new PointerEventHandler(ReleaseScrollBar), handledEventsToo: true);
        bar.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(ReleaseScrollBar), handledEventsToo: true);
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
                   (_scrollBarHeld || IsFromScrollBar(e.OriginalSource) || e.GetCurrentPoint(this).Position.X >= ActualWidth - ScrollBarWidth);
        _nearScrollBar = near;
        SetScrollBarExpanded(near);
        if (near)
        {
            UpdatePointerCursor(arrow: true);
        }
    }

    private void ReleaseScrollBar(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrollBarHeld)
        {
            return;
        }

        _scrollBarHeld = false;
        var position = e.GetCurrentPoint(this).Position;
        var inside = position.X >= 0 && position.Y >= 0 && position.X <= ActualWidth && position.Y <= ActualHeight;
        _nearScrollBar = inside && position.X >= ActualWidth - ScrollBarWidth;
        SetScrollBarExpanded(_nearScrollBar);
    }

    /// <summary>Test hook: expanded, and kept so whatever the pointer does.</summary>
    internal void PinScrollBarExpandedForTest()
    {
        _scrollBarPinned = true;
        SetScrollBarExpanded(true);
    }

    /// <summary>
    /// Switches the bar between its thin and full looks via the visual states a
    /// ScrollViewer uses. (IndicatorMode.None does not mean "full bar": it hides it.)
    /// </summary>
    private void SetScrollBarExpanded(bool expanded)
    {
        expanded |= _scrollBarPinned;
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
