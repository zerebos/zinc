using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Zinc;

/// <summary>
/// Text on the chrome's pane-colored surfaces (the selected tab flows into its terminal, so it
/// takes the terminal's background or the tab's color). Dark chrome has white text, which a light
/// surface (a light scheme, a yellow tab) turns dark. Light chrome has dark text, which a dark surface
/// (a dark scheme in a light window) turns white. Accent marks (selection pills, focus lines) on those
/// surfaces are kept visible the same way.
/// </summary>
internal static class Contrast
{
    private static readonly SolidColorBrush Dark = new(Color.FromArgb(0xE4, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush Light = new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    /// <summary>The app's own text is dark (light windows, <c>[window] appearance</c>).</summary>
    private static bool LightChrome => Microsoft.UI.Xaml.Application.Current.RequestedTheme == Microsoft.UI.Xaml.ApplicationTheme.Light;

    /// <summary>What an indicator needs against what's around it (WCAG 2.1, non-text contrast).</summary>
    private const double IndicatorRatio = 3;

    private static readonly Dictionary<(Color, Color), SolidColorBrush> Accents = [];

    /// <summary>Whether <paramref name="background"/> is light enough that white text on it is hard to read.</summary>
    public static bool IsLight(Color background) =>
        0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B > 150;

    /// <summary>
    /// Text for <paramref name="background"/> where the chrome's own would be hard to read, or null to keep the chrome's.
    /// </summary>
    public static Brush? TextOn(Color background) => IsLight(background) == LightChrome ? null : IsLight(background) ? Dark : Light;

    /// <summary>
    /// <paramref name="accent"/> as drawn on <paramref name="background"/>: itself if it stands out
    /// enough (3:1), else blended toward white (on a dark surface) or black (on a light one) just
    /// until it does, so it keeps its hue where it can. A tab colored close to the accent no longer
    /// hides its selection pill.
    /// </summary>
    public static Brush AccentOn(Color background, Brush accent)
    {
        if (accent is not SolidColorBrush { Color: var color } || Ratio(color, background) >= IndicatorRatio)
        {
            return accent;
        }

        if (Accents.TryGetValue((background, color), out var cached))
        {
            return cached;
        }

        var toward = Luminance(background) > 0.18 ? Color.FromArgb(0xFF, 0, 0, 0) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        var adjusted = toward;
        for (var t = 0.1; t < 1; t += 0.1)
        {
            var mixed = Mix(color, toward, t);
            if (Ratio(mixed, background) >= IndicatorRatio)
            {
                adjusted = mixed;
                break;
            }
        }

        return Accents[(background, color)] = new SolidColorBrush(adjusted);
    }

    /// <summary>The WCAG contrast ratio between two colors (1 to 21).</summary>
    public static double Ratio(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>WCAG relative luminance (sRGB, linearized).</summary>
    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static Color Mix(Color from, Color to, double t) => Color.FromArgb(0xFF,
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));
}
