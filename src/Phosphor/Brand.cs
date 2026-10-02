using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Phosphor;

/// <summary>
/// Zinc's look in the app's chrome (docs/brand.md §8-9): the UI color tokens for dark and light
/// themes and the accent that replaces Windows' (<c>[window] accent</c>). The UI keeps Segoe UI
/// (Geist Mono was tried for the wordmark, counts and command names: not native enough, too subtle
/// to matter). Never terminal content: the color scheme always wins there.
/// </summary>
internal static class Brand
{
    /// <summary>Phosphor green: the accent on dark.</summary>
    public static readonly Color Green = Color.FromArgb(0xFF, 0x3D, 0xF2, 0xA0);

    /// <summary>The accent on light (4.9:1 on Mica).</summary>
    public static readonly Color GreenOnLight = Color.FromArgb(0xFF, 0x0B, 0x7A, 0x4B);

    /// <summary>Needs-you states: the bell, a program waiting.</summary>
    public static readonly Color Caution = Color.FromArgb(0xFF, 0xFF, 0xB0, 0x00);
    public static readonly Color CautionOnLight = Color.FromArgb(0xFF, 0x9A, 0x5B, 0x00);

    /// <summary>Failures (red, never amber).</summary>
    public static readonly Color Error = Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B);
    public static readonly Color ErrorOnLight = Color.FromArgb(0xFF, 0xB4, 0x2A, 0x28);

    /// <summary>Text and icons on a filled accent.</summary>
    public static readonly Color Screen = Color.FromArgb(0xFF, 0x0F, 0x13, 0x11);

    public static bool IsDark(FrameworkElement element) => element.ActualTheme != ElementTheme.Light;

    public static Color Attention(bool dark) => dark ? Green : GreenOnLight;

    public static Color CautionFor(bool dark) => dark ? Caution : CautionOnLight;

    public static Color ErrorFor(bool dark) => dark ? Error : ErrorOnLight;

    public static Color OnAccent(bool dark) => dark ? Screen : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// The caption buttons (minimize, maximize, close) of a window drawing its own title bar: glyphs in
    /// the app's light or dark ink, a faint tint of it on hover and press, the background left to the window.
    /// </summary>
    public static void StyleCaptionButtons(Microsoft.UI.Windowing.AppWindowTitleBar titleBar)
    {
        var ink = Application.Current.RequestedTheme == ApplicationTheme.Light ? Color.FromArgb(0xFF, 0, 0, 0) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        var transparent = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = ink;
        titleBar.ButtonHoverForegroundColor = ink;
        titleBar.ButtonPressedForegroundColor = ink;
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0x80, ink.R, ink.G, ink.B);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x20, ink.R, ink.G, ink.B);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x40, ink.R, ink.G, ink.B);
    }

    /// <summary>
    /// Replaces Windows' accent with Zinc's for every control in the app (switches, checkboxes,
    /// selections, focus lines, progress, links). WinUI's theme brushes are built from these colors:
    /// dark themes fill with Light2 and write text with Light3, light themes fill with Dark1 and write
    /// with Dark2/Dark3. Set before any window exists (controls resolve them when they're made).
    /// </summary>
    public static void ApplyAccent(ResourceDictionary resources)
    {
        static Color Hex(uint rgb) => Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        resources["SystemAccentColor"] = Green;
        resources["SystemAccentColorLight1"] = Hex(0x2FE07A); // the lit gradient's foot
        resources["SystemAccentColorLight2"] = Green;
        resources["SystemAccentColorLight3"] = Hex(0x86FFC6); // Lit Cell's bright green
        resources["SystemAccentColorDark1"] = GreenOnLight;
        resources["SystemAccentColorDark2"] = Hex(0x09663F); // Lit Cell Light's green
        resources["SystemAccentColorDark3"] = Hex(0x064D2F);
    }
}
