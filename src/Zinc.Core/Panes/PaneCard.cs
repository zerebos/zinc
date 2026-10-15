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

    /// <summary>How far a dark scheme's header is lifted toward white, and a light scheme's lowered toward black (decision 59 A).</summary>
    public const double DarkLift = 0.04, LightDrop = 0.05;

    /// <summary>Whether panes are cards at this gap.</summary>
    public static bool At(int gap) => gap > 0;

    /// <summary>Whether white text on <paramref name="color"/> is hard to read (the app's <c>Contrast.IsLight</c>).</summary>
    public static bool IsLight(GhosttyColorRgb color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B > 150;

    /// <summary>
    /// A header's fill: its terminal's <paramref name="background"/> a few percent lighter on a dark scheme, darker on a
    /// light one, so the header reads as part of the card and still stands apart from the terminal.
    /// </summary>
    public static GhosttyColorRgb HeaderFill(GhosttyColorRgb background) => IsLight(background)
        ? Mix(background, new GhosttyColorRgb(0, 0, 0), LightDrop)
        : Mix(background, new GhosttyColorRgb(255, 255, 255), DarkLift);

    /// <summary><paramref name="from"/> moved <paramref name="t"/> (0 to 1) of the way to <paramref name="to"/>.</summary>
    public static GhosttyColorRgb Mix(GhosttyColorRgb from, GhosttyColorRgb to, double t) => new(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));
}
