using System.Runtime.InteropServices;

namespace Zinc.Ghostty;

/// <summary>A shape Ghostty drew for one cell: 8-bit coverage, placed relative to the cell's top-left (it may reach past the cell).</summary>
/// <param name="X">Left edge, in pixels from the cell's left (negative: it starts in the cell before).</param>
/// <param name="Y">Top edge, in pixels from the cell's top.</param>
public sealed record SpriteImage(byte[] Coverage, int Width, int Height, int X, int Y);

/// <summary>
/// Ghostty's sprite rasterizer (zinc-sprite.dll, built from the pinned Ghostty by
/// scripts/build/sprites.ps1): box drawing, block elements, Symbols for Legacy Computing,
/// Powerline and Braille, drawn pixel-exact for a cell size instead of taken from a font, so
/// they meet their neighbours in any font. Thread-safe: each call draws on its own canvas.
/// </summary>
public static partial class Sprites
{
    private const string Dll = "zinc-sprite";

    [LibraryImport(Dll, EntryPoint = "zinc_sprite_has")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool NativeHas(uint codepoint);

    [LibraryImport(Dll, EntryPoint = "zinc_sprite_draw")]
    private static unsafe partial int NativeDraw(uint codepoint, uint cellWidth, uint ascent, uint descent, uint thickness,
        byte* output, uint outputWidth, uint outputHeight, int* x, int* y, uint* width, uint* height);

    // Which codepoints Ghostty draws, looked up once each (the renderer asks per cell per frame).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, bool> Known = new();

    /// <summary>
    /// Whether Ghostty draws <paramref name="codepoint"/> itself. Not its "branch drawing" set
    /// (U+F5D0–F60D, kitty's git-graph glyphs): that's private use, where older Nerd Fonts keep
    /// icons, so those stay with the font.
    /// </summary>
    public static bool Has(uint codepoint) =>
        codepoint >= 0x2500 && codepoint is not (>= 0xF5D0 and <= 0xF60D) && Known.GetOrAdd(codepoint, cp => NativeHas(cp));

    /// <summary>
    /// <paramref name="codepoint"/> for a cell <paramref name="cellWidth"/> pixels wide with the
    /// baseline <paramref name="ascent"/> pixels down and <paramref name="descent"/> below it,
    /// lines <paramref name="thickness"/> pixels thick. Null if Ghostty doesn't draw it.
    /// </summary>
    public static unsafe SpriteImage? Draw(uint codepoint, int cellWidth, int ascent, int descent, int thickness)
    {
        if (!Has(codepoint) || cellWidth <= 0 || ascent + descent <= 0)
        {
            return null;
        }

        // The drawing may reach a quarter cell past each edge (plus antialiasing).
        var width = 2 * cellWidth + 8;
        var height = 2 * (ascent + descent) + 8;
        var buffer = new byte[width * height];
        int x, y;
        uint drawnWidth, drawnHeight;
        int result;
        fixed (byte* output = buffer)
        {
            result = NativeDraw(codepoint, (uint)cellWidth, (uint)ascent, (uint)descent, (uint)Math.Max(1, thickness),
                output, (uint)width, (uint)height, &x, &y, &drawnWidth, &drawnHeight);
        }

        if (result != 0 || drawnWidth == 0 || drawnHeight == 0)
        {
            return null;
        }

        var coverage = new byte[drawnWidth * drawnHeight];
        for (var row = 0; row < drawnHeight; row++)
        {
            Array.Copy(buffer, row * width, coverage, row * drawnWidth, drawnWidth);
        }

        return new SpriteImage(coverage, (int)drawnWidth, (int)drawnHeight, x, y);
    }
}
