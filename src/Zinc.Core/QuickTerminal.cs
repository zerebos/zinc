using System.Globalization;
using Windows.System;

namespace Zinc;

public enum QuickTerminalPosition
{
    Top,
    Bottom,
    Left,
    Right,
    Center,
}

public enum QuickTerminalScreen
{
    /// <summary>The monitor under the mouse pointer.</summary>
    Mouse,

    /// <summary>The primary monitor.</summary>
    Main,
}

/// <summary>A length as a share of the screen ("60%") or in pixels ("800px" / "800").</summary>
public readonly record struct QuickTerminalLength(double Value, bool Percent)
{
    public int Resolve(int available) =>
        Math.Clamp((int)Math.Round(Percent ? available * Value / 100 : Value), 1, available);

    public static bool TryParse(string s, out QuickTerminalLength length)
    {
        length = default;
        s = s.Trim();
        var percent = s.EndsWith('%');
        var number = percent ? s[..^1] : s.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? s[..^2] : s;
        if (!double.TryParse(number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value <= 0 || (percent && value > 100))
        {
            return false;
        }

        length = new QuickTerminalLength(value, percent);
        return true;
    }
}

/// <summary>The quick terminal's settings (Ghostty's <c>quick-terminal-*</c> keys).</summary>
public sealed record QuickTerminalSettings
{
    /// <summary>Where it appears: centered by default (as Ghostty draws it), or along an edge.</summary>
    public QuickTerminalPosition Position { get; init; } = QuickTerminalPosition.Center;
    public QuickTerminalScreen Screen { get; init; } = QuickTerminalScreen.Mouse;

    /// <summary>
    /// Ghostty's quick-terminal-size: the first value is the size along the axis the
    /// window grows from its edge (height for top/bottom, width for left/right, width
    /// for center); the second, if given, is the other axis.
    /// </summary>
    public QuickTerminalLength? Primary { get; init; }
    public QuickTerminalLength? Secondary { get; init; }

    public bool Autohide { get; init; } = true;

    /// <summary>
    /// How long it takes to slide in and out, in seconds (Ghostty's quick-terminal-animation-duration); 0: it just
    /// appears and disappears.
    /// </summary>
    public double AnimationDuration { get; init; } = 0.2;

    /// <summary>Global hotkey, e.g. "win+grave" (Windows Terminal's quake key).</summary>
    public string Hotkey { get; init; } = "win+grave";
}

/// <summary>Where the quick terminal goes on a monitor's work area (pure, testable).</summary>
public static class QuickTerminalLayout
{
    public readonly record struct Rect(int X, int Y, int Width, int Height);

    public static Rect Place(Rect work, QuickTerminalSettings s)
    {
        // Defaults: half the screen from an edge, or 70% x 60% centered.
        switch (s.Position)
        {
            case QuickTerminalPosition.Top or QuickTerminalPosition.Bottom:
            {
                var h = (s.Primary ?? new QuickTerminalLength(50, true)).Resolve(work.Height);
                var w = (s.Secondary ?? new QuickTerminalLength(100, true)).Resolve(work.Width);
                var x = work.X + (work.Width - w) / 2;
                var y = s.Position == QuickTerminalPosition.Top ? work.Y : work.Y + work.Height - h;
                return new Rect(x, y, w, h);
            }
            case QuickTerminalPosition.Left or QuickTerminalPosition.Right:
            {
                var w = (s.Primary ?? new QuickTerminalLength(50, true)).Resolve(work.Width);
                var h = (s.Secondary ?? new QuickTerminalLength(100, true)).Resolve(work.Height);
                var x = s.Position == QuickTerminalPosition.Left ? work.X : work.X + work.Width - w;
                var y = work.Y + (work.Height - h) / 2;
                return new Rect(x, y, w, h);
            }
            default:
            {
                var w = (s.Primary ?? new QuickTerminalLength(70, true)).Resolve(work.Width);
                var h = (s.Secondary ?? new QuickTerminalLength(60, true)).Resolve(work.Height);
                return new Rect(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h);
            }
        }
    }
}

/// <summary>A system-wide hotkey: RegisterHotKey modifiers plus a virtual key.</summary>
public readonly record struct GlobalHotkey(uint Modifiers, VirtualKey Key)
{
    // RegisterHotKey's MOD_* flags.
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8, NoRepeat = 0x4000;

    /// <summary>Parses "win+grave", "ctrl+alt+t"... (key names as in keybindings).</summary>
    public static GlobalHotkey Parse(string text)
    {
        uint mods = 0;
        var rest = new List<string>();
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "win" or "super" or "cmd": mods |= Win; break;
                default: rest.Add(part); break;
            }
        }

        // Reuse the keybinding parser for ctrl/alt/shift and the key itself.
        var chord = Input.KeyChord.Parse(string.Join('+', rest));
        if (chord.Ctrl) mods |= Control;
        if (chord.Alt) mods |= Alt;
        if (chord.Shift) mods |= Shift;
        if (mods == 0)
        {
            throw new FormatException($"'{text}': a global hotkey needs a modifier");
        }

        return new GlobalHotkey(mods, chord.Key);
    }
}

/// <summary>
/// The quick terminal sliding in (pure, testable). The window sits at its final place throughout; each frame shows the
/// part of it nearest the edge it comes from (a window region) with the content moved along, so it seems to slide out
/// from that edge (a centered one from its own top edge). Moving the window itself from off screen would show it on a
/// neighboring monitor on the way (Windows Terminal's quake window clips the same way).
/// </summary>
public static class QuickTerminalSlide
{
    /// <summary>
    /// At <paramref name="progress"/> (0: hidden, 1: all there; eased here, fast then settling): the part of a
    /// <paramref name="width"/> x <paramref name="height"/> window to show, and how far its content is moved (pixels).
    /// </summary>
    public static (QuickTerminalLayout.Rect Visible, int OffsetX, int OffsetY) At(QuickTerminalPosition position, int width, int height, double progress)
    {
        var eased = 1 - Math.Pow(1 - Math.Clamp(progress, 0, 1), 3);
        var shownHigh = (int)Math.Round(height * eased);
        var shownWide = (int)Math.Round(width * eased);
        return position switch
        {
            QuickTerminalPosition.Bottom => (new(0, height - shownHigh, width, shownHigh), 0, height - shownHigh),
            QuickTerminalPosition.Left => (new(0, 0, shownWide, height), shownWide - width, 0),
            QuickTerminalPosition.Right => (new(width - shownWide, 0, shownWide, height), width - shownWide, 0),
            _ => (new(0, 0, width, shownHigh), 0, shownHigh - height), // top and center: down from the top edge
        };
    }
}
