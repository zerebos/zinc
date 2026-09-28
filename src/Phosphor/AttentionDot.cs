using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Phosphor.Ghostty;
using Phosphor.Terminal;
using Windows.UI;

namespace Phosphor;

/// <summary>What a terminal tab has to show you, least to most urgent (a workspace shows its most urgent).</summary>
internal enum Attention
{
    None,

    /// <summary>A long command finished out of view.</summary>
    Finished,

    /// <summary>A program sent a notification (OSC 9 / 777) out of view.</summary>
    Notified,

    /// <summary>A long command failed out of view.</summary>
    Failed,
}

/// <summary>
/// A tab's status mark: the <see cref="Attention"/> dot (green finished, blue a program's
/// notification, red failed), else a small ring while a program reports progress (OSC 9;4).
/// After a tab's title, or on its icon's corner (ringed in the background color) where there's
/// no title. The dot pulses twice when it appears, then stays.
/// </summary>
internal sealed partial class AttentionDot : Grid
{
    private static readonly Color FailedColor = Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B);   // the command marks' (TerminalControl.Commands.cs)
    private static readonly Color FinishedColor = Color.FromArgb(0xFF, 0x3D, 0xDC, 0x97);
    private static readonly Color NotifiedColor = Color.FromArgb(0xFF, 0x6C, 0xB6, 0xFF);

    private readonly Ellipse _dot = new();
    private readonly ScaleTransform _scale = new();
    private readonly ProgressRing _ring = new() { MinWidth = 0, MinHeight = 0, IsActive = false, Visibility = Visibility.Collapsed };
    private Attention _shown;
    private TerminalProgress _progress = TerminalProgress.None;
    private Storyboard? _pulse;

    public AttentionDot()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        _dot.Stroke = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        _dot.RenderTransform = _scale;
        _dot.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        Children.Add(_ring);
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
            _ring.Width = _ring.Height = value ? 10 : 12;
            Width = Height = value ? 10 : 12;
            HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            VerticalAlignment = value ? VerticalAlignment.Top : VerticalAlignment.Center;
            Margin = value ? new Thickness(0, -4, -4, 0) : new Thickness(8, 0, 2, 0);
        }
    }

    /// <summary>Whether it shows anything (a dot or a ring).</summary>
    public bool HasContent => _shown != Attention.None || _progress.Active;

    public void Show(Attention attention)
    {
        if (attention == _shown)
        {
            return;
        }

        var appeared = _shown == Attention.None;
        _shown = attention;
        if (attention != Attention.None)
        {
            _dot.Fill = new SolidColorBrush(attention switch
            {
                Attention.Failed => FailedColor,
                Attention.Notified => NotifiedColor,
                _ => FinishedColor,
            });
        }

        Refresh();
        if (attention != Attention.None && appeared && !TestHooks.Deterministic)
        {
            Pulse();
        }
    }

    /// <summary>A program's progress: a ring (filling, or spinning when it gave no number) while there's no dot.</summary>
    public void ShowProgress(TerminalProgress progress)
    {
        if (progress == _progress)
        {
            return;
        }

        _progress = progress;
        _ring.IsIndeterminate = progress.State == TerminalProgressState.Indeterminate || progress.Value < 0;
        _ring.Value = Math.Clamp(progress.Value, 0, 100);
        _ring.Foreground = progress.State switch
        {
            TerminalProgressState.Error => new SolidColorBrush(FailedColor),
            TerminalProgressState.Pause => (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            _ => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        };
        Refresh();
    }

    private void Refresh()
    {
        var dot = _shown != Attention.None;
        var ring = !dot && _progress.Active;
        _dot.Visibility = dot ? Visibility.Visible : Visibility.Collapsed;
        _ring.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        _ring.IsActive = ring;
        Visibility = dot || ring ? Visibility.Visible : Visibility.Collapsed;
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
