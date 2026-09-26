using Phantom.Ghostty;

namespace Phantom.Terminal;

public sealed record TerminalTheme(GhosttyColorRgb Background, GhosttyColorRgb Foreground, GhosttyColorRgb Cursor)
{
    // Catppuccin Mocha base colors; the 256-color palette uses libghostty's defaults.
    public static TerminalTheme Default { get; } = new(
        GhosttyColorRgb.FromHex(0x1E1E2E),
        GhosttyColorRgb.FromHex(0xCDD6F4),
        GhosttyColorRgb.FromHex(0xF5E0DC));

    /// <summary>Overrides for palette entries (0-255); the rest keep libghostty's defaults.</summary>
    public IReadOnlyDictionary<int, GhosttyColorRgb> Palette { get; init; } = new Dictionary<int, GhosttyColorRgb>();
}
