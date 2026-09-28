using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
/// The dot for <see cref="Attention"/> (green finished, red failed): after a tab's title, or on
/// its icon's corner (ringed in the background color) where there's no title. It pulses a few
/// times when it appears, then stays.
/// </summary>
internal sealed partial class AttentionDot : Microsoft.UI.Xaml.Controls.Grid
{
    private readonly Ellipse _dot = new();
    private readonly ScaleTransform _scale = new();
    private Attention _shown;
    private Storyboard? _pulse;

    public AttentionDot()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        _dot.Stroke = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        _dot.RenderTransform = _scale;
        _dot.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        Children.Add(_dot);
        Corner = true;
    }

    /// <summary>On an icon's corner (true), or on its own after a title.</summary>
    public bool Corner
    {
        set
        {
            _dot.Width = _dot.Height = value ? 10 : 8;
            _dot.StrokeThickness = value ? 1.5 : 0;
            Width = Height = value ? 10 : 8;
            HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            VerticalAlignment = value ? VerticalAlignment.Top : VerticalAlignment.Center;
            Margin = value ? new Thickness(0, -4, -4, 0) : new Thickness(8, 0, 2, 0);
        }
    }

    public void Show(Attention attention)
    {
        if (attention == _shown)
        {
            return;
        }

        var appeared = _shown == Attention.None;
        _shown = attention;
        Visibility = attention == Attention.None ? Visibility.Collapsed : Visibility.Visible;
        if (attention == Attention.None)
        {
            return;
        }

        // The command marks' colors (TerminalControl.Commands.cs), so a dot and a bar match.
        _dot.Fill = new SolidColorBrush(attention == Attention.Failed
            ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B)
            : Windows.UI.Color.FromArgb(0xFF, 0x3D, 0xDC, 0x97));
        if (appeared && !TestHooks.Deterministic)
        {
            Pulse();
        }
    }

    public void PulseIfShown()
    {
        if (_shown != Attention.None && !TestHooks.Deterministic)
        {
            Pulse();
        }
    }

    /// <summary>Grows and shrinks twice (the user tried three: a bit much): easy to catch from the corner of an eye, then still.</summary>
    private void Pulse()
    {
        _pulse?.Stop();
        _pulse = new Storyboard();
        foreach (var axis in new[] { "ScaleX", "ScaleY" })
        {
            var grow = new DoubleAnimation
            {
                From = 1,
                To = 1.8,
                Duration = TimeSpan.FromMilliseconds(300),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(grow, _scale);
            Storyboard.SetTargetProperty(grow, axis);
            _pulse.Children.Add(grow);
        }

        _pulse.Begin();
    }
}
