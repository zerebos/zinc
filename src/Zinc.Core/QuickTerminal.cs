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

    /// <summary>
    /// Its place on <paramref name="work"/> (a monitor's work area): the configured size, or <paramref name="size"/> (one
    /// given by dragging its edges) clamped to the screen; against its edge, or centered on the other axes.
    /// </summary>
    public static Rect Place(Rect work, QuickTerminalSettings s, (int Width, int Height)? size = null)
    {
        var (w, h) = size is { } given
            ? (Math.Clamp(given.Width, 1, work.Width), Math.Clamp(given.Height, 1, work.Height))
            : ConfiguredSize(work, s);
        var x = s.Position switch
        {
            QuickTerminalPosition.Left => work.X,
            QuickTerminalPosition.Right => work.X + work.Width - w,
            _ => work.X + (work.Width - w) / 2,
        };
        var y = s.Position switch
        {
            QuickTerminalPosition.Top => work.Y,
            QuickTerminalPosition.Bottom => work.Y + work.Height - h,
            _ => work.Y + (work.Height - h) / 2,
        };
        return new Rect(x, y, w, h);
    }

    // Defaults: half the screen from an edge, or 70% x 60% centered.
    private static (int Width, int Height) ConfiguredSize(Rect work, QuickTerminalSettings s) => s.Position switch
    {
        QuickTerminalPosition.Top or QuickTerminalPosition.Bottom =>
            ((s.Secondary ?? new QuickTerminalLength(100, true)).Resolve(work.Width), (s.Primary ?? new QuickTerminalLength(50, true)).Resolve(work.Height)),
        QuickTerminalPosition.Left or QuickTerminalPosition.Right =>
            ((s.Primary ?? new QuickTerminalLength(50, true)).Resolve(work.Width), (s.Secondary ?? new QuickTerminalLength(100, true)).Resolve(work.Height)),
        _ => ((s.Primary ?? new QuickTerminalLength(70, true)).Resolve(work.Width), (s.Secondary ?? new QuickTerminalLength(60, true)).Resolve(work.Height)),
    };
}

/// <summary>An edge or corner of the quick terminal, dragged to resize it.</summary>
public enum QuickTerminalEdge
{
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Resizing the quick terminal by its edges (it has no frame: Zinc does it). It keeps its place while it changes size:
/// against its screen edge, centered on the other axes, so a centered one grows on both sides of the dragged edge.
/// </summary>
public static class QuickTerminalResize
{
    /// <summary>The edges it can be dragged by: all but the side (and corners) against the screen edge it sits on.</summary>
    public static IReadOnlyList<QuickTerminalEdge> Edges(QuickTerminalPosition position) =>
        Enum.GetValues<QuickTerminalEdge>().Where(e => !Touches(e, position)).ToList();

    private static bool Touches(QuickTerminalEdge edge, QuickTerminalPosition position) => position switch
    {
        QuickTerminalPosition.Top => edge is QuickTerminalEdge.Top or QuickTerminalEdge.TopLeft or QuickTerminalEdge.TopRight,
        QuickTerminalPosition.Bottom => edge is QuickTerminalEdge.Bottom or QuickTerminalEdge.BottomLeft or QuickTerminalEdge.BottomRight,
        QuickTerminalPosition.Left => edge is QuickTerminalEdge.Left or QuickTerminalEdge.TopLeft or QuickTerminalEdge.BottomLeft,
        QuickTerminalPosition.Right => edge is QuickTerminalEdge.Right or QuickTerminalEdge.TopRight or QuickTerminalEdge.BottomRight,
        _ => false,
    };

    /// <summary>
    /// The size after dragging <paramref name="edge"/> by (<paramref name="dx"/>, <paramref name="dy"/>) pixels from
    /// <paramref name="start"/>: at least <paramref name="min"/>, at most <paramref name="max"/> (the work area).
    /// </summary>
    public static (int Width, int Height) Size(QuickTerminalPosition position, QuickTerminalEdge edge, (int Width, int Height) start,
        int dx, int dy, (int Width, int Height) min, (int Width, int Height) max)
    {
        // A centered axis grows on both sides: the dragged edge follows the pointer, the opposite one mirrors it.
        var xFactor = position is QuickTerminalPosition.Left or QuickTerminalPosition.Right ? 1 : 2;
        var yFactor = position is QuickTerminalPosition.Top or QuickTerminalPosition.Bottom ? 1 : 2;
        var w = start.Width;
        var h = start.Height;
        if (edge is QuickTerminalEdge.Left or QuickTerminalEdge.TopLeft or QuickTerminalEdge.BottomLeft)
        {
            w -= dx * xFactor;
        }
        else if (edge is QuickTerminalEdge.Right or QuickTerminalEdge.TopRight or QuickTerminalEdge.BottomRight)
        {
            w += dx * xFactor;
        }

        if (edge is QuickTerminalEdge.Top or QuickTerminalEdge.TopLeft or QuickTerminalEdge.TopRight)
        {
            h -= dy * yFactor;
        }
        else if (edge is QuickTerminalEdge.Bottom or QuickTerminalEdge.BottomLeft or QuickTerminalEdge.BottomRight)
        {
            h += dy * yFactor;
        }

        return (Math.Clamp(w, Math.Min(min.Width, max.Width), max.Width), Math.Clamp(h, Math.Min(min.Height, max.Height), max.Height));
    }
}

/// <summary>A size the quick terminal was given by dragging, on one monitor, while the config placed it as <see cref="Base"/>.</summary>
public sealed record QuickTerminalSize(string Monitor, int Width, int Height, string Base);

/// <summary>
/// The sizes the quick terminal was dragged to, one per monitor (physical pixels; saved with the session). A size holds
/// only while the config places it as it did then: an edited position or size in the config wins.
/// </summary>
public sealed class QuickTerminalSizes
{
    public const int Capacity = 8;

    private readonly List<QuickTerminalSize> _sizes = [];

    public QuickTerminalSizes(IEnumerable<QuickTerminalSize>? saved = null) => _sizes.AddRange((saved ?? []).Take(Capacity));

    public IReadOnlyList<QuickTerminalSize> All => _sizes;

    /// <summary>What a size depends on in the config: the position and the configured size.</summary>
    public static string BaseOf(QuickTerminalSettings s) =>
        string.Join(' ', s.Position.ToString().ToLowerInvariant(), Text(s.Primary), Text(s.Secondary));

    private static string Text(QuickTerminalLength? length) => length is { } l
        ? l.Value.ToString(CultureInfo.InvariantCulture) + (l.Percent ? "%" : "px")
        : "-";

    public (int Width, int Height)? For(string monitor, QuickTerminalSettings s) =>
        _sizes.FirstOrDefault(e => e.Monitor == monitor) is { } e && e.Base == BaseOf(s) ? (e.Width, e.Height) : null;

    public void Remember(string monitor, QuickTerminalSettings s, int width, int height)
    {
        Forget(monitor);
        _sizes.Insert(0, new QuickTerminalSize(monitor, width, height, BaseOf(s)));
        if (_sizes.Count > Capacity)
        {
            _sizes.RemoveAt(_sizes.Count - 1);
        }
    }

    /// <summary>Back to the configured size on <paramref name="monitor"/>; false if it had none of its own.</summary>
    public bool Forget(string monitor) => _sizes.RemoveAll(e => e.Monitor == monitor) > 0;
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
