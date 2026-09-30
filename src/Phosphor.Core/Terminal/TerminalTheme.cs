using Phosphor.Ghostty;

namespace Phosphor.Terminal;

public sealed record TerminalTheme(GhosttyColorRgb Background, GhosttyColorRgb Foreground, GhosttyColorRgb Cursor)
{
    // Catppuccin Mocha base colors; the 256-color palette uses libghostty's defaults.
    public static TerminalTheme Default { get; } = new(
        GhosttyColorRgb.FromHex(0x1E1E2E),
        GhosttyColorRgb.FromHex(0xCDD6F4),
        GhosttyColorRgb.FromHex(0xF5E0DC));

    /// <summary>Overrides for palette entries (0-255); the rest keep libghostty's defaults.</summary>
    public IReadOnlyDictionary<int, GhosttyColorRgb> Palette { get; init; } = new Dictionary<int, GhosttyColorRgb>();

    /// <summary>The 16 base colors as a terminal shows them: this theme's, else libghostty's built-in ones.</summary>
    public IReadOnlyList<GhosttyColorRgb> BaseColors =>
        [.. Enumerable.Range(0, 16).Select(i => Palette.TryGetValue(i, out var c) ? c : TerminalSession.BuiltinBaseColors[i])];
}
