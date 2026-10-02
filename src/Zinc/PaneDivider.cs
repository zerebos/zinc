using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Zinc.Panes;

namespace Zinc;

/// <summary>
/// The draggable bar between two panes of a split. Sits in the middle row/column of
/// the split's Grid and rewrites the Grid's star sizes while dragged.
/// </summary>
internal sealed partial class PaneDivider : Grid
{
    public const double Thickness = 5;

    private readonly PaneSplit _split;
    private readonly Grid _owner;
    private bool _dragging;

    public PaneDivider(PaneSplit split, Grid owner, Brush lineBrush)
    {
        _split = split;
        _owner = owner;
        var horizontal = split.Orientation == SplitOrientation.Horizontal;

        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable
        ProtectedCursor = InputSystemCursor.Create(horizontal ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
        Children.Add(new Rectangle
        {
            Fill = lineBrush,
            Width = horizontal ? 1 : double.NaN,
            Height = horizontal ? double.NaN : 1,
            HorizontalAlignment = horizontal ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Center,
        });

        // Panels have no OnPointer* overrides (those live on Control).
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
    }

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
        var extent = (horizontal ? _owner.ActualWidth : _owner.ActualHeight) - Thickness;
        if (extent > 0)
        {
            PaneTree<object>.SetRatio(_split, ((horizontal ? pos.X : pos.Y) - Thickness / 2) / extent);
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
