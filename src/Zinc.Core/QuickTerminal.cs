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
    /// How long it takes to fade in and out, in seconds (Ghostty's quick-terminal-animation-duration); 0: it just
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
/// How the quick terminal appears (pure, testable): its content starts transparent and, centered, at 70% of its size
/// (grows into place), or along an edge, moved a little toward that edge (comes out of it); then it fades and moves
/// into place. Fading the content rather than clipping the window keeps Windows' own edges out of it (a clipped window
/// showed a white classic frame while it slid) and never shows it on a neighboring monitor on the way.
/// </summary>
public static class QuickTerminalAppear
{
    /// <summary>The scale it grows from when centered.</summary>
    public const double CenterScale = 0.7;

    /// <summary>Where the content starts, for a <paramref name="width"/> x <paramref name="height"/> window (DIPs): its scale and offset.</summary>
    public static (double Scale, double OffsetX, double OffsetY) Start(QuickTerminalPosition position, double width, double height)
    {
        var dy = Math.Min(120, height / 4);
        var dx = Math.Min(120, width / 4);
        return position switch
        {
            QuickTerminalPosition.Top => (1, 0, -dy),
            QuickTerminalPosition.Bottom => (1, 0, dy),
            QuickTerminalPosition.Left => (1, -dx, 0),
            QuickTerminalPosition.Right => (1, dx, 0),
            _ => (CenterScale, 0, 0),
        };
    }
}
