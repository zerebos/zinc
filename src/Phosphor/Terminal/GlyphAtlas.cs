using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace Phosphor.Terminal;

/// <summary>
/// The font's glyphs, each drawn once into a texture and then copied, tinted with its cell's color, by one
/// sprite batch per frame, instead of a Direct2D glyph run per color run (thousands of calls on a busy screen:
/// measured 2026-10-02, a full-color screen went from ~9.5 ms to ~2.9 ms a frame this way). The approach of
/// Windows Terminal's AtlasEngine and the GPU terminals, inside Win2D.
///
/// A tile is three cells wide and two tall, with the glyph's own cell in its middle, so glyphs that reach past
/// their cell (Nerd Font icons, italics) keep their overhang. The glyph is drawn at the grid's fractional pixel
/// offset (from the padding), so its anti-aliasing is what a glyph run on screen would get. Direct2D's grayscale
/// text contrast depends on the text's color, so each glyph has a variant for light text and one for dark text;
/// both are stored as white coverage (premultiplied: a, a, a, a) for the batch to tint.
///
/// New glyphs are batched: <see cref="Get"/> reserves a tile, <see cref="Flush"/> (before the frame's sprite batch)
/// draws every reserved glyph in one session onto a scratch copy of the page and whitens it onto the page in one
/// effect draw (the scratch's empty parts come out transparent, so tiles already there stay as they are). Two
/// drawing sessions per frame, however many glyphs are new: doing it per glyph, or reading each tile back to
/// whiten it on the CPU, cost a screenful of new glyphs (a new pane, a zoom step) 20-40 ms.
///
/// Lookups are arrays indexed by glyph number (one per font variant and light/dark), not a dictionary: they run
/// for every cell of every frame.
///
/// Per renderer, in 512 px pages (about 1 MB each; larger only if one tile is) added as needed; <see cref="Clear"/>
/// on any change of font, cell size, scale, device or padding offset, and after a flush that failed halfway.
/// </summary>
internal sealed class GlyphAtlas : IDisposable
{
    private const int PagePixels = 512;

    private readonly List<CanvasRenderTarget> _pages = [];
    private readonly List<Tile> _tiles = [];

    /// <summary>Per (variant, dark): glyph index → 0 unknown, else tile number + 1.</summary>
    private readonly int[][] _slots = new int[8][];

    /// <summary>Glyphs reserved since the last <see cref="Flush"/>, not drawn yet.</summary>
    private readonly List<(CanvasFontFace Face, float FontSize, int Glyph, bool Dark, int Page, Vector2 Origin, Rect Clip)> _pending = [];

    /// <summary>Pages not cleared yet: a new render target can come with recycled GPU memory, an old atlas's tiles in it.</summary>
    private readonly HashSet<int> _unclearedPages = [];

    private CanvasRenderTarget? _scratch;
    private ColorMatrixEffect? _whiten;
    private int _next; // the next free tile slot on the last page
    private int _tileW, _tileH, _perRow, _perPage;
    private int _pageW, _pageH; // PagePixels, or a tile's size where one is larger (big zoom at a high display scale)
    private CellMetrics _metrics;
    private float _fracX, _fracY;

    /// <summary>A glyph's place in the atlas: the page and the tile's rectangle on it (DIPs of the page).</summary>
    public readonly record struct Tile(CanvasRenderTarget Page, Rect Source);

    /// <summary>Tile size and the glyph's origin in it, for these metrics; drops everything if they changed.</summary>
    public void Prepare(CellMetrics m, float fracX, float fracY)
    {
        if (m == _metrics && fracX == _fracX && fracY == _fracY && _tileW > 0)
        {
            return;
        }

        Clear();
        _metrics = m;
        _fracX = fracX;
        _fracY = fracY;
        _tileW = 3 * (int)m.WidthPx;
        _tileH = 2 * (int)m.HeightPx;
        _pageW = Math.Max(PagePixels, _tileW);
        _pageH = Math.Max(PagePixels, _tileH);
        _perRow = _pageW / _tileW;
        _perPage = _perRow * (_pageH / _tileH);
    }

    /// <summary>Where the tile goes for a cell whose top-left device pixel is (<paramref name="cellX"/>, <paramref name="cellY"/>), in DIPs.</summary>
    public Rect Destination(int cellX, int cellY)
    {
        var s = _metrics.Scale;
        return new Rect((cellX - (int)_metrics.WidthPx) / s, (cellY - (int)_metrics.HeightPx / 2) / s, _tileW / s, _tileH / s);
    }

    /// <summary>The tile for <paramref name="glyph"/>; a new one is reserved now and drawn by <see cref="Flush"/>.</summary>
    public Tile Get(ICanvasResourceCreatorWithDpi device, CanvasFontFace face, float fontSize, int glyph, int variant, bool dark)
    {
        var key = variant * 2 + (dark ? 1 : 0);
        var slots = _slots[key];
        if (slots == null || glyph >= slots.Length)
        {
            Array.Resize(ref slots, Math.Max(glyph + 1, Math.Max(256, (slots?.Length ?? 0) * 2)));
            _slots[key] = slots;
        }

        if (slots[glyph] is var slot and > 0)
        {
            return _tiles[slot - 1];
        }

        var s = _metrics.Scale;
        if (_pages.Count == 0 || _next >= _perPage)
        {
            _pages.Add(new CanvasRenderTarget(device, _pageW / s, _pageH / s, 96 * s));
            _unclearedPages.Add(_pages.Count - 1);
            _next = 0;
        }

        var place = _next++;
        var px = place % _perRow * _tileW;
        var py = place / _perRow * _tileH;
        // The glyph's cell is one cell in and half a cell down its tile; the origin is on the baseline, at the
        // grid's fractional offset.
        var origin = new Vector2((px + (int)_metrics.WidthPx + _fracX) / s, (py + (int)_metrics.HeightPx / 2 + _fracY) / s + _metrics.Baseline);
        var tile = new Tile(_pages[^1], new Rect(px / s, py / s, _tileW / s, _tileH / s));
        _pending.Add((face, fontSize, glyph, dark, _pages.Count - 1, origin, tile.Source));
        _tiles.Add(tile);
        slots[glyph] = _tiles.Count;
        return tile;
    }

    /// <summary>Draws the glyphs reserved since the last flush into their tiles (before the batch that copies them).</summary>
    public void Flush(ICanvasResourceCreatorWithDpi device)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        try
        {
            FlushPending(device);
        }
        catch
        {
            // Half drawn: the reserved tiles would stay blank (or get drawn twice). Start over next frame.
            Clear();
            throw;
        }
    }

    private void FlushPending(ICanvasResourceCreatorWithDpi device)
    {
        var s = _metrics.Scale;
        _scratch ??= new CanvasRenderTarget(device, _pageW / s, _pageH / s, 96 * s);
        _whiten ??= new ColorMatrixEffect
        {
            // Straight color in, premultiplied out: color = white, alpha as drawn, so coverage a becomes a, a, a, a
            // (and the scratch's empty parts stay 0, 0, 0, 0).
            ColorMatrix = new Matrix5x4 { M44 = 1, M51 = 1, M52 = 1, M53 = 1 },
            AlphaMode = CanvasAlphaMode.Premultiplied,
        };

        foreach (var page in _pending.Select(p => p.Page).Distinct().ToList())
        {
            // The page's new glyphs alone, on transparent, at their tiles: white for light text, black for dark
            // (Direct2D's contrast for each)...
            using (var ds = _scratch.CreateDrawingSession())
            {
                ds.Clear(Color.FromArgb(0, 0, 0, 0));
                ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
                using var brush = new CanvasSolidColorBrush(ds, Color.FromArgb(255, 255, 255, 255));
                foreach (var (face, fontSize, glyph, dark, _, origin, clip) in _pending.Where(p => p.Page == page))
                {
                    // Clipped to its tile: ink past it (a few Nerd Font glyphs reach ~5% of a cell further) would
                    // land on a neighbor's tile and show up beside another character.
                    using var layer = ds.CreateLayer(1, clip);
                    brush.Color = dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255);
                    ds.DrawGlyphRun(origin, face, fontSize, [new CanvasGlyph { Index = glyph, Advance = _metrics.Width }], false, 0, brush);
                }
            }

            // ...then onto the page as white coverage. Normal blending: everything else in the scratch is transparent,
            // and each tile is written once. (CanvasBlend.Copy with an effect drew nothing, 2026-10-02.) A new page is
            // cleared first: it may hold a previous atlas's tiles (after a zoom step, the old pages' freed memory came
            // back with them, and new glyphs were blended over the fragments: garbled text).
            _whiten.Source = _scratch;
            using (var ds = _pages[page].CreateDrawingSession())
            {
                if (_unclearedPages.Remove(page))
                {
                    ds.Clear(Color.FromArgb(0, 0, 0, 0));
                }

                ds.DrawImage(_whiten);
            }
        }

        _pending.Clear();
    }

    public void Clear()
    {
        foreach (var page in _pages)
        {
            page.Dispose();
        }

        _pages.Clear();
        _tiles.Clear();
        _pending.Clear();
        _unclearedPages.Clear();
        Array.Clear(_slots);
        _scratch?.Dispose();
        _scratch = null;
        _whiten?.Dispose();
        _whiten = null;
        _next = 0;
        _tileW = 0;
    }

    public void Dispose() => Clear();
}
