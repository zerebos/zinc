using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Phosphor;

/// <summary>
/// Text on the chrome's pane-colored surfaces (the selected tab flows into its terminal, so it
/// takes the terminal's background or the tab's color). The chrome is dark, with white text; on a
/// light surface (a light scheme, a yellow tab) the text needs to be dark instead.
/// </summary>
internal static class Contrast
{
    private static readonly SolidColorBrush Dark = new(Color.FromArgb(0xE4, 0x00, 0x00, 0x00));

    /// <summary>Whether <paramref name="background"/> is light enough that white text on it is hard to read.</summary>
    public static bool IsLight(Color background) =>
        0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B > 150;

    /// <summary>A dark text brush for a light <paramref name="background"/>; null (keep the chrome's own) otherwise.</summary>
    public static Brush? TextOn(Color background) => IsLight(background) ? Dark : null;
}
