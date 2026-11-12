using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Zinc.Terminal;

/// <summary>
/// The bell (BEL). Per <c>bell</c>, the pane flashes and the window is told, which puts a dot on the tab when you
/// aren't looking. With <c>bell-sound</c>, Windows' default sound plays. A burst rings once, because after a bell, more
/// bells stay quiet until none has come for <see cref="BellQuiet"/>. A held key at a prompt that refuses it rings on
/// every repeat, and the shell may still be ringing after the key's release.
/// </summary>
public sealed partial class TerminalControl
{
    private static readonly TimeSpan BellQuiet = TimeSpan.FromMilliseconds(300);

    /// <summary>Until when bells are part of the last burst (each one moves it on).</summary>
    private long _bellQuietUntil;

    private Border? _bellFlash;

    /// <summary>The bell rang and <c>bell</c> isn't "none" (the window marks the tab).</summary>
    public event Action? BellRang;

    private void OnBell()
    {
        var now = Environment.TickCount64;
        var inBurst = now < _bellQuietUntil;
        _bellQuietUntil = now + (long)BellQuiet.TotalMilliseconds;
        if (inBurst)
        {
            return;
        }

        Log.Write($"bell: {_config.Bell}{(_config.BellSound ? " + sound" : "")}");
        if (_config.BellSound)
        {
            MessageBeep(0); // MB_OK: the "Default Beep" sound
        }

        if (_config.Bell == BellMode.None)
        {
            return;
        }

        if (_config.Bell == BellMode.Flash)
        {
            FlashPane();
        }

        BellRang?.Invoke();
    }

    /// <summary>A quick wash of the text color over the pane, fading out.</summary>
    private void FlashPane()
    {
        if (_bellFlash == null)
        {
            _bellFlash = new Border { IsHitTestVisible = false, Opacity = 0 };
            _root.Children.Add(_bellFlash);
        }

        _bellFlash.Background = new SolidColorBrush(TerminalRenderer.ToColor(_config.Theme.Foreground));
        var fade = new DoubleAnimation
        {
            From = 0.2,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(fade, _bellFlash);
        Storyboard.SetTargetProperty(fade, "Opacity");
        new Storyboard { Children = { fade } }.Begin();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint type);
}
