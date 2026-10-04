using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Zinc;

/// <summary>
/// An invisible strip along one edge (or a square at one corner) of the quick terminal, with the resize pointer: the
/// window has no frame to resize it by, so the window drags these itself (MainWindow.QuickResize.cs).
/// </summary>
internal sealed partial class QuickResizeEdge : Grid
{
    /// <summary>How far in from the window's edge a strip reaches, in DIPs; corners are squares twice as wide.</summary>
    public const double Thickness = 6;

    public QuickTerminalEdge Edge { get; }

    /// <summary>
    /// <paramref name="cover"/>: the whole window instead, with this edge's pointer, laid over it while that edge is
    /// dragged: the window follows the pointer a moment late, so the pointer kept slipping off the strip onto the
    /// terminal, whose own pointer flickered in.
    /// </summary>
    public QuickResizeEdge(QuickTerminalEdge edge, bool cover = false)
    {
        Edge = edge;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable
        var left = edge is QuickTerminalEdge.Left or QuickTerminalEdge.TopLeft or QuickTerminalEdge.BottomLeft;
        var right = edge is QuickTerminalEdge.Right or QuickTerminalEdge.TopRight or QuickTerminalEdge.BottomRight;
        var top = edge is QuickTerminalEdge.Top or QuickTerminalEdge.TopLeft or QuickTerminalEdge.TopRight;
        var bottom = edge is QuickTerminalEdge.Bottom or QuickTerminalEdge.BottomLeft or QuickTerminalEdge.BottomRight;
        var corner = (left || right) && (top || bottom);
        HorizontalAlignment = left ? HorizontalAlignment.Left : right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        VerticalAlignment = top ? VerticalAlignment.Top : bottom ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        Width = left || right ? (corner ? 2 * Thickness : Thickness) : double.NaN;
        Height = top || bottom ? (corner ? 2 * Thickness : Thickness) : double.NaN;
        if (cover)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            Width = Height = double.NaN;
        }

        ProtectedCursor = InputSystemCursor.Create(edge switch
        {
            QuickTerminalEdge.Left or QuickTerminalEdge.Right => InputSystemCursorShape.SizeWestEast,
            QuickTerminalEdge.Top or QuickTerminalEdge.Bottom => InputSystemCursorShape.SizeNorthSouth,
            QuickTerminalEdge.TopLeft or QuickTerminalEdge.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
            _ => InputSystemCursorShape.SizeNortheastSouthwest,
        });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, $"Resize quick terminal ({edge})");
    }
}
