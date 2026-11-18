using Zinc.Ghostty;

namespace Zinc.Panes;

/// <summary>
/// How far a pane header's made fill stands from its terminal (<c>[colors] pane-header</c>: terminal, subtle, standard,
/// strong; or a color). Its card's border goes one step further (<see cref="PaneCard.EdgeFill(GhosttyColorRgb, GhosttyColorRgb)"/>).
/// </summary>
public enum HeaderLevel
{
    /// <summary>The default. The terminal's own background (hidden tabs then take the Standard fill).</summary>
    Terminal,

    Subtle,

    Standard,

    Strong,
}

/// <summary>
/// How a pane looks as a card (any gap above 0): rounded corners, a 2 px edge, and its header filled from its own
/// scheme. At gap 0 panes stay flat.
/// </summary>
public static class PaneCard
{
    /// <summary>The radius of the card's corners, in px.</summary>
    public const double Radius = 8;

    /// <summary>
    /// The width of the card's edge. The edge shows the header fill, or the ring once the pane has focus, so focus never
    /// resizes a terminal.
    /// </summary>
    public const double Edge = 2;

    /// <summary>
    /// How far a Standard header's fill stands from its terminal (WCAG contrast ratio). A fixed step toward white
    /// would be faintest on the darkest schemes, so the fill is found by its contrast instead.
    /// </summary>
    public const double HeaderContrast = 1.15;

    /// <summary>
    /// A header this close to its terminal ("Same as the terminal", or a custom color that near) doesn't tell the shown
    /// tab apart, since the shown tab is drawn in the terminal's color. So its hidden tabs take the Standard fill instead.
    /// </summary>
    public const double SeamlessBelow = 1.05;

    /// <summary>
    /// The contrast steps a card's border takes, one past its header's (Subtle -> Standard, Standard -> Strong, Strong -> 1.6:1).
    /// </summary>
    private static readonly double[] EdgeSteps = [HeaderContrast, 1.3, 1.6];

    /// <summary>The contrast a <paramref name="level"/> header stands from its terminal.</summary>
    public static double ContrastOf(HeaderLevel level) => level switch
    {
        HeaderLevel.Terminal => 1,
        HeaderLevel.Subtle => 1.08,
        HeaderLevel.Strong => 1.3,
        _ => HeaderContrast,
    };

    /// <summary>A <c>pane-header</c> level by its config name: terminal, subtle, standard, or strong.</summary>
    public static bool TryLevel(string value, out HeaderLevel level)
    {
        (var known, level) = value switch
        {
            "terminal" => (true, HeaderLevel.Terminal),
            "subtle" => (true, HeaderLevel.Subtle),
            "standard" or "auto" => (true, HeaderLevel.Standard), // auto is its older name
            "strong" => (true, HeaderLevel.Strong),
            _ => (false, HeaderLevel.Standard),
        };
        return known;
    }

    /// <summary>Whether panes are cards at this gap.</summary>
    public static bool At(int gap) => gap > 0;

    /// <summary>Whether white text on <paramref name="color"/> is hard to read (the app's <c>Contrast.IsLight</c>).</summary>
    public static bool IsLight(GhosttyColorRgb color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B > 150;

    /// <summary>
    /// A header's fill: its terminal's <paramref name="background"/> made lighter on a dark scheme, darker on a light one,
    /// just until it stands <paramref name="level"/>'s contrast apart (<see cref="ContrastOf"/>), so the header reads as
    /// part of the card and still stands apart from the terminal the same way on every scheme. At the Terminal level the
    /// fill is the background itself.
    /// </summary>
    public static GhosttyColorRgb HeaderFill(GhosttyColorRgb background, HeaderLevel level = HeaderLevel.Standard) =>
        level == HeaderLevel.Terminal ? background : Away(background, background, ContrastOf(level));

    /// <summary>A profile's header fill: its <c>pane-header</c> color, else made from its background at its level.</summary>
    public static GhosttyColorRgb HeaderFill(Config config) =>
        config.PaneHeaderColor ?? HeaderFill(config.Theme.Background, config.PaneHeaderLevel);

    /// <summary>Whether a <paramref name="header"/> fill is too close to its terminal to tell the shown tab apart.</summary>
    public static bool Seamless(GhosttyColorRgb header, GhosttyColorRgb background) => Ratio(header, background) < SeamlessBelow;

    /// <summary>
    /// What a profile's hidden tabs are filled with: nothing (the header shows through), or the Standard fill when the
    /// header is <see cref="Seamless"/>, so the shown tab is still the one that joins its terminal.
    /// </summary>
    public static GhosttyColorRgb? HiddenTabFill(Config config) =>
        Seamless(HeaderFill(config), config.Theme.Background) ? HeaderFill(config.Theme.Background) : null;

    /// <summary>
    /// A card's unfocused border: its <paramref name="header"/> fill pushed one step further from the terminal's
    /// <paramref name="background"/> (<see cref="EdgeSteps"/>, or a quarter further past the last step), keeping a custom
    /// color's hue. A seamless header's card takes the terminal's color as its border, so the card is all terminal.
    /// </summary>
    public static GhosttyColorRgb EdgeFill(GhosttyColorRgb header, GhosttyColorRgb background)
    {
        var ratio = Ratio(header, background);
        if (ratio < SeamlessBelow)
        {
            return background;
        }

        // A made fill moves in whole channel steps, so it lands a little past its contrast step. The factor 1.04 skips
        // past the step the fill already took.
        var next = EdgeSteps.FirstOrDefault(step => step >= ratio * 1.04);
        return Away(header, background, next > 0 ? next : Math.Min(21, ratio * 1.25));
    }

    /// <summary>A profile's card border (<see cref="EdgeFill(GhosttyColorRgb, GhosttyColorRgb)"/>).</summary>
    public static GhosttyColorRgb EdgeFill(Config config) => EdgeFill(HeaderFill(config), config.Theme.Background);

    /// <summary>
    /// <paramref name="from"/> moved toward white or black, whichever is away from <paramref name="background"/>, just
    /// until it stands <paramref name="contrast"/> apart from it (or all the way, if that's not enough).
    /// </summary>
    private static GhosttyColorRgb Away(GhosttyColorRgb from, GhosttyColorRgb background, double contrast)
    {
        double lf = Luminance(from), lb = Luminance(background);
        var lighter = lf != lb ? lf > lb : !IsLight(background);
        var toward = lighter ? new GhosttyColorRgb(255, 255, 255) : new GhosttyColorRgb(0, 0, 0);
        double low = 0, high = 1;
        for (var i = 0; i < 16; i++)
        {
            var t = (low + high) / 2;
            (low, high) = Ratio(Mix(from, toward, t), background) >= contrast ? (low, t) : (t, high);
        }

        return Mix(from, toward, high);
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
