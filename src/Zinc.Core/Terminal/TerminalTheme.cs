using Zinc.Ghostty;

namespace Zinc.Terminal;

/// <summary>
/// Colors 16-255 made from a scheme's 16 (Ghostty's <c>palette-generate</c>), and whether a light scheme's keep their
/// order from background to text (<c>palette-harmonious</c>) instead of running dark to light.
/// </summary>
public readonly record struct PaletteGeneration(bool Generate, bool Harmonious);

public sealed record TerminalTheme(GhosttyColorRgb Background, GhosttyColorRgb Foreground, GhosttyColorRgb Cursor)
{
    /// <summary>
    /// Zinc's own colors: "Lit Cell", from the brand (phosphor green on near-black). Its 16 base
    /// colors; the rest of the 256 are libghostty's.
    /// </summary>
    public static TerminalTheme Default { get; } = new(
        GhosttyColorRgb.FromHex(0x0C110E),
        GhosttyColorRgb.FromHex(0xCFDDD3),
        GhosttyColorRgb.FromHex(0x3DF2A0))
    {
        Palette = new Dictionary<int, GhosttyColorRgb>
        {
            [0] = GhosttyColorRgb.FromHex(0x1C2420),
            [1] = GhosttyColorRgb.FromHex(0xFF6B6B),
            [2] = GhosttyColorRgb.FromHex(0x3DF2A0),
            [3] = GhosttyColorRgb.FromHex(0xFFB000),
            [4] = GhosttyColorRgb.FromHex(0x6FB3F2),
            [5] = GhosttyColorRgb.FromHex(0xC99AF2),
            [6] = GhosttyColorRgb.FromHex(0x48D6CF),
            [7] = GhosttyColorRgb.FromHex(0xB9C7BE),
            [8] = GhosttyColorRgb.FromHex(0x758679),
            [9] = GhosttyColorRgb.FromHex(0xFF9494),
            [10] = GhosttyColorRgb.FromHex(0x86FFC6),
            [11] = GhosttyColorRgb.FromHex(0xFFCB5C),
            [12] = GhosttyColorRgb.FromHex(0x9CCBFF),
            [13] = GhosttyColorRgb.FromHex(0xDDBCFF),
            [14] = GhosttyColorRgb.FromHex(0x86ECE6),
            [15] = GhosttyColorRgb.FromHex(0xEDEFEA),
        },
        SelectionBackground = GhosttyColorRgb.FromHex(0x1E4533),
        SelectionForeground = GhosttyColorRgb.FromHex(0xEDEFEA),
        CursorText = GhosttyColorRgb.FromHex(0x0C110E),
    };

    /// <summary>
    /// What a color scheme starts from: the default's background, text and cursor (schemes nearly
    /// always set their own), but libghostty's palette and inverted selection, so a scheme that leaves
    /// them out doesn't show Zinc's.
    /// </summary>
    public static TerminalTheme SchemeBase { get; } = new(Default.Background, Default.Foreground, Default.Cursor);

    /// <summary>Overrides for palette entries (0-255); the rest keep libghostty's defaults.</summary>
    public IReadOnlyDictionary<int, GhosttyColorRgb> Palette { get; init; } = new Dictionary<int, GhosttyColorRgb>();

    /// <summary>Behind selected text; null: selection inverts the colors.</summary>
    public GhosttyColorRgb? SelectionBackground { get; init; }

    /// <summary>Selected text; null: the text keeps its color (or inverts, without a selection background).</summary>
    public GhosttyColorRgb? SelectionForeground { get; init; }

    /// <summary>
    /// The character under a block cursor; null: the cell's background. Drawing only (no escape
    /// sequence sets it, so libghostty has no option for it), kept even when a program recolors the
    /// cursor (OSC 12), as in Ghostty.
    /// </summary>
    public GhosttyColorRgb? CursorText { get; init; }

    /// <summary>The 16 base colors as a terminal shows them: this theme's, else libghostty's built-in ones.</summary>
    public IReadOnlyList<GhosttyColorRgb> BaseColors =>
        [.. Enumerable.Range(0, 16).Select(i => Palette.TryGetValue(i, out var c) ? c : TerminalSession.BuiltinBaseColors[i])];
}
