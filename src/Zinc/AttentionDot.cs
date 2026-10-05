using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Zinc.Ghostty;
using Zinc.Terminal;
using Windows.UI;

namespace Zinc;

/// <summary>What a terminal tab has to show you, least to most urgent (a workspace shows its most urgent).</summary>
internal enum Attention
{
    None,

    /// <summary>A long command finished out of view.</summary>
    Finished,

    /// <summary>A program sent a notification (OSC 9 / 777) out of view.</summary>
    Notified,

    /// <summary>The bell rang out of view: something wants you (the brand's caution color).</summary>
    Bell,

    /// <summary>A long command failed out of view.</summary>
    Failed,
}

/// <summary>
/// A dot's state in words (docs/ux-writing.md, "Attention and notifications"), for tooltips, accessible names and the
/// tab switcher: "finished", "failed (exit 1)", "needs you" (the bell), "has a notification".
/// </summary>
internal static class AttentionWords
{
    public static string? For(Attention attention, int? exitCode = null) => attention switch
    {
        Attention.Finished => "finished",
        Attention.Failed => exitCode is { } code ? $"failed (exit {code})" : "failed",
        Attention.Bell => "needs you",
        Attention.Notified => "has a notification",
        _ => null,
    };

    /// <summary>Several tabs' states, most urgent first: "failed (exit 1)", or "1 failed, 2 finished".</summary>
    public static string? Summary(IReadOnlyList<(Attention Attention, int? ExitCode)> tabs)
    {
        var wanting = tabs.Where(t => t.Attention != Attention.None).ToList();
        if (wanting.Count <= 1)
        {
            return wanting.Count == 0 ? null : For(wanting[0].Attention, wanting[0].ExitCode);
        }

        return string.Join(", ", wanting.GroupBy(t => t.Attention).OrderByDescending(g => g.Key).Select(g => g.Key switch
        {
            Attention.Bell => $"{g.Count()} {(g.Count() == 1 ? "needs" : "need")} you",
            Attention.Notified => $"{g.Count()} {(g.Count() == 1 ? "notification" : "notifications")}",
            _ => $"{g.Count()} {For(g.Key)}",
        }));
    }

    /// <summary>"Title, failed (exit 1)": an accessible name with the state after the title.</summary>
    public static string Name(string title, string? words) => words == null ? title : $"{title}, {words}";

    /// <summary>The title, and the state on a line of its own ("Failed (exit 1)").</summary>
    public static string ToolTip(string title, string? words) =>
        words == null ? title : $"{title}\n{char.ToUpperInvariant(words[0])}{words[1..]}";
}

/// <summary>
/// A tab's status mark: the <see cref="Attention"/> dot, a lit cell (a rounded square, docs/brand.md
/// §9: green finished or a program's notification, amber the bell, red failed), else a small ring
/// while a program reports progress (OSC 9;4). After a tab's title, or on its icon's corner (ringed
/// in the background color) where there's no title. It fades 100% → 35% → 100% every 1.6 s until
/// you look (still when Windows' animations are off). A workspace with two or more tabs to look
/// at shows how many in a pill instead (not on a corner: no room).
/// </summary>
internal sealed partial class AttentionDot : Grid
{
    private static readonly Windows.UI.ViewManagement.UISettings Animations = new();

    // [notify] finished-color, bell-color, failed-color (null: the brand's, per theme). Dots shown with an
    // older set recolor the next time they're shown (MainWindow.ApplyConfig shows them all again).
    private static Color? _finished, _bell, _failed;
    private static int _colorsVersion;
    private int _shownVersion = -1;

    /// <summary>The dot colors from the config in force.</summary>
    public static void SetColors(Config config)
    {
        static Color? From(GhosttyColorRgb? c) => c is { } rgb ? Color.FromArgb(0xFF, rgb.R, rgb.G, rgb.B) : null;
        (Color?, Color?, Color?) next = (From(config.DotFinishedColor), From(config.DotBellColor), From(config.DotFailedColor));
        if (next != (_finished, _bell, _failed))
        {
            (_finished, _bell, _failed) = next;
            _colorsVersion++;
        }
    }

    private readonly Rectangle _dot = new();
    private readonly TextBlock _countText = new()
    {
        FontSize = 10,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _pill = new()
    {
        Height = 14,
        MinWidth = 14,
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(4, 0, 4, 0),
        Visibility = Visibility.Collapsed,
    };
    private bool _corner;
    private int _count;
    private readonly ProgressRing _ring = new() { MinWidth = 0, MinHeight = 0, IsActive = false, Visibility = Visibility.Collapsed };
    private Attention _shown;
    private TerminalProgress _progress = TerminalProgress.None;
    private Storyboard? _pulse;

    public AttentionDot()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        _dot.Stroke = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        _pill.Child = _countText;
        Children.Add(_ring);
        Children.Add(_dot);
        Children.Add(_pill);
        Corner = true;
    }

    /// <summary>On an icon's corner (true), or on its own after a title.</summary>
    public bool Corner
    {
        set
        {
            _corner = value;
            // After a title about the size of the tab's close glyph (the guide's 6 epx was easy to miss); on an
            // icon's corner a little bigger, with its ring.
            var size = value ? 9.0 : 8.0;
            _dot.Width = _dot.Height = size;
            _dot.RadiusX = _dot.RadiusY = size * 0.12 + (value ? 1.5 : 0); // the lit cell's corner (the ring rounds it out)
            _dot.StrokeThickness = value ? 1.5 : 0;
            _ring.Width = _ring.Height = value ? 10 : 12;
            Width = Height = value ? 10 : 12;
            HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            VerticalAlignment = value ? VerticalAlignment.Top : VerticalAlignment.Center;
            Margin = value ? new Thickness(0, -4, -4, 0) : new Thickness(8, 0, 2, 0);
            Refresh();
        }
    }

    /// <summary>Whether it shows anything (a dot or a ring).</summary>
    public bool HasContent => _shown != Attention.None || _progress.Active;

    /// <param name="count">How many tabs it stands for (a workspace's): 2 or more shows the number.</param>
    public void Show(Attention attention, int count = 1)
    {
        count = attention == Attention.None ? 0 : Math.Max(1, count);
        if (attention == _shown && count == _count && _shownVersion == _colorsVersion)
        {
            return;
        }

        _shownVersion = _colorsVersion;

        // Pulses when it appears, and when one more tab wants you.
        var news = count > _count;
        _shown = attention;
        _count = count;
        if (attention != Attention.None)
        {
            var dark = Brand.IsDark(this);
            var brush = new SolidColorBrush(attention switch
            {
                Attention.Failed => _failed ?? Brand.ErrorFor(dark),
                Attention.Bell => _bell ?? Brand.CautionFor(dark),
                _ => _finished ?? Brand.Attention(dark),
            });
            _dot.Fill = brush;
            _pill.Background = brush;
            _countText.Foreground = new SolidColorBrush(Brand.OnAccent(dark));
            _countText.Text = count > 9 ? "9+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        Refresh();
        if (attention == Attention.None)
        {
            StopPulse();
        }
        else if (news && !TestHooks.Still)
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
            TerminalProgressState.Error => new SolidColorBrush(Brand.ErrorFor(Brand.IsDark(this))),
            TerminalProgressState.Pause => (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            _ => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        };
        Refresh();
    }

    private void Refresh()
    {
        var dot = _shown != Attention.None;
        var ring = !dot && _progress.Active;
        var pill = dot && _count >= 2 && !_corner;
        _pill.Visibility = pill ? Visibility.Visible : Visibility.Collapsed;
        Width = Height = pill ? double.NaN : _corner ? 10 : 12;
        dot &= !pill;
        _dot.Visibility = dot ? Visibility.Visible : Visibility.Collapsed;
        _ring.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        _ring.IsActive = ring;
        Visibility = dot || ring || pill ? Visibility.Visible : Visibility.Collapsed;
    }

    public void PulseIfShown()
    {
        if (_shown != Attention.None && !TestHooks.Still)
        {
            Pulse();
        }
    }

    /// <summary>
    /// The brand's pulse: opacity 100% → 35% → 100% over 1.6 s, until you look; it never scales,
    /// bounces or flashes (the old pulse grew twice and stopped). Held steady when Windows'
    /// "Animation effects" are off.
    /// </summary>
    private void Pulse()
    {
        StopPulse();
        if (!Animations.AnimationsEnabled)
        {
            return;
        }

        // The dot's and pill's own opacity, never this element's: hosts hide the dot by its Opacity (the
        // sidebar and pane tabs, when hovering swaps it for the close button), and an animation running
        // on that property would override them.
        _pulse = new Storyboard();
        foreach (var target in new UIElement[] { _dot, _pill })
        {
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0.35,
                Duration = TimeSpan.FromMilliseconds(800),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, "Opacity");
            _pulse.Children.Add(fade);
        }

        _pulse.Begin();
    }

    private void StopPulse()
    {
        _pulse?.Stop(); // Stop puts the animated values back (1)
        _pulse = null;
    }
}
