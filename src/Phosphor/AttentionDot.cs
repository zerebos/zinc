using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Phosphor;

/// <summary>What a terminal tab has to show you: a long command finished, or failed, out of view.</summary>
internal enum Attention
{
    None,
    Finished,
    Failed,
}

/// <summary>
/// The dot at a tab icon's top-right corner for <see cref="Attention"/> (green finished, red
/// failed), ringed in the background color so it reads over any icon.
/// </summary>
internal sealed partial class AttentionDot : Microsoft.UI.Xaml.Controls.Grid
{
    private readonly Ellipse _dot = new() { Width = 7, Height = 7, StrokeThickness = 1.5 };
    private Attention _shown;

    public AttentionDot()
    {
        Width = 8;
        Height = 8;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, -3, -3, 0);
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        _dot.Stroke = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        Children.Add(_dot);
    }

    public void Show(Attention attention)
    {
        if (attention == _shown)
        {
            return;
        }

        _shown = attention;
        Visibility = attention == Attention.None ? Visibility.Collapsed : Visibility.Visible;
        if (attention != Attention.None)
        {
            // The command marks' colors (TerminalControl.Commands.cs), so a dot and a bar match.
            _dot.Fill = new SolidColorBrush(attention == Attention.Failed
                ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B)
                : Windows.UI.Color.FromArgb(0xFF, 0x3D, 0xDC, 0x97));
        }
    }
}
