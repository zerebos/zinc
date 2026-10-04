using Windows.Foundation;
using Windows.UI;
using Zinc.Ghostty;

namespace Zinc.Terminal;

/// <summary>
/// What custom shaders read from a frame the renderer drew (<see cref="TerminalRenderer.ForShaders"/>): the terminal's
/// live colors (programs may change the palette) and the cursor as drawn, in DIPs.
/// </summary>
public sealed class ShaderFrame
{
    public byte[] Palette { get; } = new byte[256 * 3];
    public Color Background { get; set; }
    public Color Foreground { get; set; }

    /// <summary>A cursor color the scheme or a program set (else it's the text color).</summary>
    public Color? CursorColor { get; set; }

    /// <summary>Whether the terminal shows a cursor (a program may hide it).</summary>
    public bool CursorVisible { get; set; }

    /// <summary>Whether this frame drew one (not in a blink's off phase); the fields below are from the last one drawn.</summary>
    public bool CursorDrawn { get; set; }

    public Rect Cursor { get; set; }
    public Color CursorDrawnColor { get; set; }
    public CursorVisualStyle CursorStyle { get; set; }
}
