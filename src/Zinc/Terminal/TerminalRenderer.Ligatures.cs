using Microsoft.Graphics.Canvas.Text;

namespace Zinc.Terminal;

// Ligatures ([font] ligatures). Each row's text is shaped by DirectWrite, so a font's ligatures and contextual
// alternates (=> != === -> in Cascadia Code, JetBrains Mono, Fira Code) pick their glyphs. Those fonts draw a ligature
// as one alternate glyph per character, each a cell wide, so every glyph still lands in its own cell (and the atlas,
// keyed by glyph, needs nothing new). Shaping that merges characters into fewer glyphs (a wider glyph spanning cells)
// isn't taken, so those cells keep their plain glyphs. As in Ghostty, a ligature breaks under the cursor and at the
// edges of a selection, so the character there shows as itself.
public sealed partial class TerminalRenderer
{
    /// <summary>The shaped glyph for each cell of the row being drawn (0 for the plain one), filled by <see cref="ShapeRow"/>.</summary>
    private int[] _shaped = [];

    /// <summary>
    /// Shaped segments by their text (a hash of the codepoints), length and font variant, each a glyph per character, or
    /// null when shaping changed nothing. The variant is a key of its own, because XORed into the hash it could cancel
    /// out against a character, and italic >= would reuse regular <='s glyphs.
    /// </summary>
    private readonly Dictionary<(ulong Hash, int Length, int Variant), int[]?> _shapeCache = [];

    /// <summary>The cursor's cell this frame (ligatures break there), or (-1, -1).</summary>
    private (int Column, int Row) _frameCursor = (-1, -1);

    private const int MaxShapeCache = 8192;

    /// <summary>This frame's shaped glyphs, drawn after the batch, because a ligature can reach past its atlas tile.</summary>
    private readonly List<(int Column, int Row, int Glyph, int Variant, Windows.UI.Color Color)> _ligatureGlyphs = [];

    /// <summary>Draws (and forgets) the frame's shaped glyphs, each at its cell on the grid's baseline.</summary>
    private void DrawLigatureGlyphs(Microsoft.Graphics.Canvas.CanvasDrawingSession ds)
    {
        var m = Metrics;
        foreach (var (column, row, glyph, variant, color) in _ligatureGlyphs)
        {
            if (_faces[variant] is { } face)
            {
                _brush!.Color = color;
                ds.DrawGlyphRun(new System.Numerics.Vector2(PadLeft + column * m.Width, PadTop + row * m.Height + m.Baseline), face, _fontSize,
                    [new CanvasGlyph { Index = glyph, Advance = m.Width }], false, 0, _brush);
            }
        }

        _ligatureGlyphs.Clear();
    }

    /// <summary>
    /// Fills <see cref="_shaped"/> for the row's <paramref name="count"/> cells. Returns false when there's nothing to
    /// use (ligatures off), so the caller reads plain glyphs.
    /// </summary>
    private bool ShapeRow(int count, int row)
    {
        if (!Config.FontLigatures)
        {
            return false;
        }

        if (_shaped.Length < count)
        {
            _shaped = new int[Math.Max(count, 256)];
        }

        Array.Clear(_shaped, 0, count);
        var start = -1;
        var variant = 0;
        for (var x = 0; x <= count; x++)
        {
            var take = x < count && Shapeable(x, row);
            if (take && start >= 0 && (_cells[x].FontVariant != variant || Boundary(x)))
            {
                ShapeSegment(start, x, variant);
                start = -1;
            }

            if (!take)
            {
                if (start >= 0)
                {
                    ShapeSegment(start, x, variant);
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = x;
                variant = _cells[x].FontVariant;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether shaping works on this cell, which takes one ASCII symbol or punctuation mark (not under the cursor, not a
    /// sprite). Coding fonts' ligatures are made of those (=> -> != === <!-- :: && || //), so a segment is a run of
    /// adjacent symbols. Those runs are short and the same ones come up over and over, so the cache keeps them, where
    /// shaping all text would shape every row of a screen of new text every frame. Letter-based alternates some fonts
    /// have (Fira Code's 0x) are left out.
    /// </summary>
    private bool Shapeable(int x, int row)
    {
        ref var cell = ref _cells[x];
        if (cell.CpLen != 1 || (cell.Flags & (CellFlags.Wide | CellFlags.SpacerTail | CellFlags.Invisible | CellFlags.Placeholder)) != 0 ||
            (x, row) == _frameCursor)
        {
            return false;
        }

        var codepoint = _codepoints[cell.CpStart];
        return codepoint is > ' ' and < 0x7F && (char.IsPunctuation((char)codepoint) || char.IsSymbol((char)codepoint)) && !IsSprite(ref cell);
    }

    /// <summary>Whether a selection starts or ends at <paramref name="x"/>, which a ligature mustn't span.</summary>
    private bool Boundary(int x) => ((_cells[x].Flags ^ _cells[x - 1].Flags) & CellFlags.Selected) != 0;

    /// <summary>Shapes cells [<paramref name="start"/>, <paramref name="end"/>) and puts the glyphs that differ into <see cref="_shaped"/>.</summary>
    private void ShapeSegment(int start, int end, int variant)
    {
        var length = end - start;
        if (length < 2 || _faces[variant] is not { } face)
        {
            return;
        }

        var hash = 14695981039346656037UL;
        for (var x = start; x < end; x++)
        {
            hash = (hash ^ _codepoints[_cells[x].CpStart]) * 1099511628211UL;
        }

        if (!_shapeCache.TryGetValue((hash, length, variant), out var glyphs))
        {
            if (_shapeCache.Count >= MaxShapeCache)
            {
                _shapeCache.Clear();
            }

            glyphs = Shape(start, end, variant, face);
            _shapeCache[(hash, length, variant)] = glyphs;
        }

        if (glyphs != null)
        {
            glyphs.AsSpan().CopyTo(_shaped.AsSpan(start, length));
        }
    }

    /// <summary>
    /// The glyph per character DirectWrite picks for this text, 0 where it's the plain one or not one-to-one, or null if
    /// none differ.
    /// </summary>
    private int[]? Shape(int start, int end, int variant, CanvasFontFace face)
    {
        var length = end - start;
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)_codepoints[_cells[start + i].CpStart];
        }

        var text = new string(chars);
        var result = new int[length];
        var any = false;
        try
        {
            // (Not disposed: its projection offers Dispose, but the object has no IClosable and throws.)
            var analyzer = new CanvasTextAnalyzer(text, CanvasTextDirection.LeftToRightThenTopToBottom);
            foreach (var (range, script) in analyzer.GetScript())
            {
                // No number substitution, and the font's default features (calt and liga, which draw ligatures).
                var glyphs = analyzer.GetGlyphs(range, face, _fontSize, false, false, script, "", null!, null!,
                    out var clusters, out _, out _);
                // Only one glyph per character, in order, so each character's glyph goes to its own cell.
                if (glyphs.Length != range.CharacterCount)
                {
                    continue;
                }

                for (var i = 0; i < range.CharacterCount; i++)
                {
                    if (clusters[i] != i)
                    {
                        continue;
                    }

                    var at = range.CharacterIndex + i;
                    var glyph = glyphs[i].Index;
                    if (glyph != 0 && glyph != GlyphIndex(chars[at], variant))
                    {
                        result[at] = glyph;
                        any = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ligatures: shaping failed ({ex.Message})");
            return null;
        }

        return any ? result : null;
    }
}
