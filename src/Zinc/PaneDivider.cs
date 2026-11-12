using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Zinc.Panes;

namespace Zinc;

/// <summary>
/// The draggable band between two panes of a split. Sits in the middle row/column of the split's Grid and rewrites the
/// Grid's star sizes while dragged. At gap 0 it's the divider (a 1 px line in a 5 px band). Above gap 0 it's the gap
/// itself.
/// </summary>
internal sealed partial class PaneDivider : Grid
{
    private readonly PaneSplit _split;
    private readonly Grid _owner;
    private readonly Rectangle _line;
    private bool _dragging;

    public PaneDivider(PaneSplit split, Grid owner, Brush lineBrush)
    {
        _split = split;
        _owner = owner;
        var horizontal = split.Orientation == SplitOrientation.Horizontal;

        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable
        ProtectedCursor = InputSystemCursor.Create(horizontal ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
        _line = new Rectangle
        {
            Fill = lineBrush,
            Width = horizontal ? 1 : double.NaN,
            Height = horizontal ? double.NaN : 1,
            HorizontalAlignment = horizontal ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Center,
        };
        Children.Add(_line);

        // Panels have no OnPointer* overrides (those live on Control).
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
    }

    /// <summary>The band's width (<see cref="PaneGap.Band"/>), written into the Grid by <see cref="ShowOnly"/>.</summary>
    private double Band { get; set; } = PaneGap.Divider;

    /// <summary>Applies the split's ratio to the owning Grid's star sizes.</summary>
    public static void ApplyRatio(Grid grid, PaneSplit split)
    {
        var first = new GridLength(split.Ratio, GridUnitType.Star);
        var second = new GridLength(1 - split.Ratio, GridUnitType.Star);
        if (split.Orientation == SplitOrientation.Horizontal)
        {
            grid.ColumnDefinitions[0].Width = first;
            grid.ColumnDefinitions[2].Width = second;
        }
        else
        {
            grid.RowDefinitions[0].Height = first;
            grid.RowDefinitions[2].Height = second;
        }
    }

    /// <summary>
    /// Shows one side of the split alone (<paramref name="first"/> true or false, when a maximized pane is on that
    /// side), or both at the split's ratio (null), with a band for <paramref name="gap"/> between them. The other side
    /// and the divider are collapsed, never taken out of the Grid, because removing panes would freeze them (Win2D's
    /// Loaded/Unloaded count). A collapsed terminal also keeps its rows and columns (it skips a 0 size) instead of
    /// reflowing.
    /// </summary>
    public static void ShowOnly(Grid grid, PaneSplit split, bool? first, int gap)
    {
        var horizontal = split.Orientation == SplitOrientation.Horizontal;
        var band = PaneGap.Band(gap);
        var shown = first switch { true => 0, false => 2, null => -1 };
        for (var slot = 0; slot < 3; slot++)
        {
            var size = shown < 0 ? new GridLength(band) // (the sides get the ratio below)
                : slot == shown ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            if (horizontal)
            {
                grid.ColumnDefinitions[slot].Width = size;
            }
            else
            {
                grid.RowDefinitions[slot].Height = size;
            }
        }

        if (shown < 0)
        {
            ApplyRatio(grid, split);
        }

        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            var slot = horizontal ? GetColumn(child) : GetRow(child);
            child.Visibility = shown < 0 || slot == shown ? Visibility.Visible : Visibility.Collapsed;
            if (child is PaneDivider divider)
            {
                divider.Band = band;
                divider._line.Visibility = PaneGap.Line(gap) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var pos = e.GetCurrentPoint(_owner).Position;
        var horizontal = _split.Orientation == SplitOrientation.Horizontal;
        if (PaneGap.RatioAt(horizontal ? pos.X : pos.Y, horizontal ? _owner.ActualWidth : _owner.ActualHeight, Band) is { } ratio)
        {
            PaneTree<object>.SetRatio(_split, ratio);
            ApplyRatio(_owner, _split);
        }

        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}
