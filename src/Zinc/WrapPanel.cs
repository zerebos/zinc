using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Zinc;

/// <summary>
/// Lays its children out in rows, left to right, starting a new row when the next one doesn't
/// fit (WinUI has no wrapping panel of its own). Rows can be right-aligned, as the key chips on
/// the settings' Keyboard page are.
/// </summary>
internal sealed partial class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; }

    public double VerticalSpacing { get; set; }

    public bool AlignRight { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Rows(availableSize.Width, measure: availableSize);
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Width);
        var height = rows.Sum(r => r.Height) + VerticalSpacing * Math.Max(0, rows.Count - 1);
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        foreach (var row in Rows(finalSize.Width, measure: null))
        {
            var x = AlignRight ? Math.Max(0, finalSize.Width - row.Width) : 0;
            foreach (var child in row.Children)
            {
                var size = child.DesiredSize;
                child.Arrange(new Rect(x, y + (row.Height - size.Height) / 2, size.Width, size.Height));
                x += size.Width + HorizontalSpacing;
            }

            y += row.Height + VerticalSpacing;
        }

        return finalSize;
    }

    private sealed record Row(List<UIElement> Children, double Width, double Height);

    private List<Row> Rows(double width, Size? measure)
    {
        var rows = new List<Row>();
        var current = new List<UIElement>();
        double rowWidth = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (measure is { } available)
            {
                child.Measure(available);
            }

            var size = child.DesiredSize;
            var needed = current.Count == 0 ? size.Width : rowWidth + HorizontalSpacing + size.Width;
            if (current.Count > 0 && needed > width)
            {
                rows.Add(new Row(current, rowWidth, rowHeight));
                current = [];
                (rowWidth, rowHeight) = (0, 0);
                needed = size.Width;
            }

            current.Add(child);
            rowWidth = needed;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        if (current.Count > 0)
        {
            rows.Add(new Row(current, rowWidth, rowHeight));
        }

        return rows;
    }
}
