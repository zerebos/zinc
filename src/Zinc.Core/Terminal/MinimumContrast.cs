namespace Zinc.Terminal;

/// <summary>
/// Text that reads against its background (<c>minimum-contrast</c>, as in Ghostty). The setting is a WCAG
/// contrast ratio from 1 (anything goes) to 21 (black on white), and text below it turns black or white,
/// whichever reads better.
/// </summary>
public static class MinimumContrast
{
    /// <summary>The WCAG contrast ratio of two sRGB colors (1 to 21).</summary>
    public static double Ratio((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary><paramref name="fg"/>, or black or white (the better) if it's below <paramref name="minimum"/> against <paramref name="bg"/>.</summary>
    public static (byte R, byte G, byte B) Ensure((byte R, byte G, byte B) fg, (byte R, byte G, byte B) bg, double minimum)
    {
        if (minimum <= 1 || Ratio(fg, bg) >= minimum)
        {
            return fg;
        }

        (byte, byte, byte) white = (255, 255, 255), black = (0, 0, 0);
        return Ratio(white, bg) >= Ratio(black, bg) ? white : black;
    }

    /// <summary>Relative luminance (WCAG 2).</summary>
    private static double Luminance((byte R, byte G, byte B) c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
