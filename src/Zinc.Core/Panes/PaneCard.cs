using Zinc.Ghostty;

namespace Zinc.Panes;

/// <summary>
/// How a pane looks as a card (any gap above 0): rounded corners, a 2 px edge, and its header filled from its own
/// scheme. At gap 0 panes stay flat, as they always were.
/// </summary>
public static class PaneCard
{
    /// <summary>The card's corners (the mockup's 8 px).</summary>
    public const double Radius = 8;

    /// <summary>
    /// The card's edge: the header fill, or the ring once the pane has focus, so focus never resizes a terminal
    /// (decisions 21, 58).
    /// </summary>
    public const double Edge = 2;

    /// <summary>
    /// How far a header's fill stands from its terminal (WCAG contrast ratio). A fixed step (e.g. 4% toward
    /// white) was 1.06:1 on a black scheme and 1.135:1 on Dracula: faintest on the darkest schemes.
    /// </summary>
    public const double HeaderContrast = 1.15;

    /// <summary>Whether panes are cards at this gap.</summary>
    public static bool At(int gap) => gap > 0;

    /// <summary>Whether white text on <paramref name="color"/> is hard to read (the app's <c>Contrast.IsLight</c>).</summary>
    public static bool IsLight(GhosttyColorRgb color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B > 150;

    /// <summary>
    /// A header's fill: its terminal's <paramref name="background"/> made lighter on a dark scheme, darker on a light one,
    /// just until it stands <see cref="HeaderContrast"/> apart, so the header reads as part of the card and still stands
    /// apart from the terminal on every scheme.
    /// </summary>
    public static GhosttyColorRgb HeaderFill(GhosttyColorRgb background)
    {
        var toward = IsLight(background) ? new GhosttyColorRgb(0, 0, 0) : new GhosttyColorRgb(255, 255, 255);
        double low = 0, high = 1;
        for (var i = 0; i < 16; i++)
        {
            var t = (low + high) / 2;
            (low, high) = Ratio(Mix(background, toward, t), background) >= HeaderContrast ? (low, t) : (t, high);
        }

        return Mix(background, toward, high);
    }

    /// <summary>The WCAG contrast ratio between two colors (1 to 21), as the app's <c>Contrast.Ratio</c>.</summary>
    public static double Ratio(GhosttyColorRgb a, GhosttyColorRgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(GhosttyColorRgb c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary><paramref name="from"/> moved <paramref name="t"/> (0 to 1) of the way to <paramref name="to"/>.</summary>
    public static GhosttyColorRgb Mix(GhosttyColorRgb from, GhosttyColorRgb to, double t) => new(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));
}
