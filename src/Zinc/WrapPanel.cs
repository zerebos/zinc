using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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

    /// <summary>
    /// The most rows shown, or 0 for no limit. Children past the last row get no space, and they're taken out of
    /// the tab order and hidden from screen readers until they fit again.
    /// </summary>
    public int MaxRows
    {
        get => _maxRows;
        set
        {
            if (_maxRows != value)
            {
                _maxRows = value;
                InvalidateMeasure();
            }
        }
    }

    private int _maxRows;

    /// <summary>The children this panel hid, with the <c>IsTabStop</c> and accessibility view they had before.</summary>
    private readonly Dictionary<UIElement, (bool IsTabStop, AccessibilityView View)> _hidden = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Shown(Rows(availableSize.Width, measure: availableSize));
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Width);
        var height = rows.Sum(r => r.Height) + VerticalSpacing * Math.Max(0, rows.Count - 1);
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = Rows(finalSize.Width, measure: null);
        var y = 0.0;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var shown = MaxRows <= 0 || i < MaxRows;
            var x = AlignRight ? Math.Max(0, finalSize.Width - row.Width) : 0;
            foreach (var child in row.Children)
            {
                SetShown(child, shown);
                if (!shown)
                {
                    child.Arrange(default);
                    continue;
                }

                var size = child.DesiredSize;
                child.Arrange(new Rect(x, y + (row.Height - size.Height) / 2, size.Width, size.Height));
                x += size.Width + HorizontalSpacing;
            }

            y += row.Height + VerticalSpacing;
        }

        return finalSize;
    }

    private List<Row> Shown(List<Row> rows) => MaxRows > 0 && rows.Count > MaxRows ? rows[..MaxRows] : rows;

    /// <summary>
    /// Takes a child that gets no space out of the tab order and away from screen readers, and puts it back once it
    /// fits. A child that was never hidden is left alone, and a hidden one gets its own <c>IsTabStop</c> and
    /// accessibility view back. Neither of those affects layout, so setting them here starts no new layout pass.
    /// </summary>
    private void SetShown(UIElement child, bool shown)
    {
        if (shown)
        {
            if (_hidden.Remove(child, out var own))
            {
                if (child is Control control)
                {
                    control.IsTabStop = own.IsTabStop;
                }

                AutomationProperties.SetAccessibilityView(child, own.View);
            }
        }
        else if (!_hidden.ContainsKey(child))
        {
            _hidden[child] = (child is Control { IsTabStop: true }, AutomationProperties.GetAccessibilityView(child));
            if (child is Control control)
            {
                control.IsTabStop = false;
            }

            AutomationProperties.SetAccessibilityView(child, AccessibilityView.Raw);
        }
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
