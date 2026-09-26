using Phantom.Ghostty;

namespace Phantom.Terminal;

public sealed record TerminalTheme(GhosttyColorRgb Background, GhosttyColorRgb Foreground, GhosttyColorRgb Cursor)
{
    // Catppuccin Mocha base colors; the 256-color palette uses libghostty's defaults.
    public static TerminalTheme Default { get; } = new(
        GhosttyColorRgb.FromHex(0x1E1E2E),
        GhosttyColorRgb.FromHex(0xCDD6F4),
        GhosttyColorRgb.FromHex(0xF5E0DC));
}
