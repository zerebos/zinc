using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace Phosphor.Terminal;

/// <summary>
/// The font's glyphs, each drawn once into a texture and then copied, tinted with its cell's color, by one
/// sprite batch per frame, instead of a Direct2D glyph run per color run (thousands of calls on a busy screen:
/// measured 2026-10-02, a full-color screen went from ~9.5 ms to ~2 ms a frame this way). The approach of
/// Windows Terminal's AtlasEngine and the GPU terminals, inside Win2D.
///
/// A tile is three cells wide and two tall, with the glyph's own cell in its middle, so glyphs that reach past
/// their cell (Nerd Font icons, italics) keep their overhang. The glyph is drawn at the grid's fractional pixel
/// offset (from the padding), so its anti-aliasing is what a glyph run on screen would get. Direct2D's grayscale
/// text contrast depends on the text's color, so each glyph has a variant for light text and one for dark text;
/// both are stored as white coverage (premultiplied: a, a, a, a) for the batch to tint.
///
/// Per renderer, in 512 px pages (about 1 MB each) added as needed; <see cref="Clear"/> on any change of font,
/// cell size, scale, device or padding offset.
/// </summary>
internal sealed class GlyphAtlas : IDisposable
{
    private const int PagePixels = 512;

    private readonly List<CanvasRenderTarget> _pages = [];
    private readonly Dictionary<(int Glyph, int Variant, bool Dark), Tile?> _tiles = [];
    private int _next; // the next free tile slot on the last page
    private int _tileW, _tileH, _perRow, _perPage;
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
        _perRow = Math.Max(1, PagePixels / _tileW);
        _perPage = _perRow * Math.Max(1, PagePixels / _tileH);
    }

    /// <summary>Where the tile goes for a cell whose top-left device pixel is (<paramref name="cellX"/>, <paramref name="cellY"/>), in DIPs.</summary>
    public Rect Destination(int cellX, int cellY)
    {
        var s = _metrics.Scale;
        return new Rect((cellX - (int)_metrics.WidthPx) / s, (cellY - (int)_metrics.HeightPx / 2) / s, _tileW / s, _tileH / s);
    }

    /// <summary>The tile for <paramref name="glyph"/> (drawn now if new); null if the glyph has no ink.</summary>
    public Tile? Get(ICanvasResourceCreatorWithDpi device, CanvasFontFace face, float fontSize, int glyph, int variant, bool dark)
    {
        if (_tiles.TryGetValue((glyph, variant, dark), out var tile))
        {
            return tile;
        }

        tile = Draw(device, face, fontSize, glyph, dark);
        _tiles[(glyph, variant, dark)] = tile;
        return tile;
    }

    private Tile? Draw(ICanvasResourceCreatorWithDpi device, CanvasFontFace face, float fontSize, int glyph, bool dark)
    {
        var s = _metrics.Scale;
        if (_pages.Count == 0 || _next >= _perPage)
        {
            _pages.Add(new CanvasRenderTarget(device, PagePixels / s, PagePixels / s, 96 * s));
            _next = 0;
        }

        var page = _pages[^1];
        var slot = _next++;
        var px = slot % _perRow * _tileW;
        var py = slot / _perRow * _tileH;
        var source = new Rect(px / s, py / s, _tileW / s, _tileH / s);
        using (var ds = page.CreateDrawingSession())
        {
            ds.Blend = CanvasBlend.Copy;
            ds.FillRectangle(source, Color.FromArgb(0, 0, 0, 0));
            ds.Blend = CanvasBlend.SourceOver;
            ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
            // The glyph's cell starts one cell in and half a cell down; the origin is its baseline, at the grid's
            // fractional offset.
            var origin = new Vector2((px + (int)_metrics.WidthPx + _fracX) / s,
                (py + (int)_metrics.HeightPx / 2 + _fracY) / s + _metrics.Baseline);
            using var brush = new Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush(ds, dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255));
            ds.DrawGlyphRun(origin, face, fontSize, [new CanvasGlyph { Index = glyph, Advance = _metrics.Width }], false, 0, brush);
        }

        // White coverage either way, so the batch's tint gives the cell's color (a dark glyph is drawn black,
        // for dark text's contrast, then whitened).
        var pixels = page.GetPixelBytes(px, py, _tileW, _tileH);
        var ink = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            pixels[i] = pixels[i + 1] = pixels[i + 2] = a;
            ink |= a != 0;
        }

        if (!ink)
        {
            _next--; // nothing to draw: give the slot back
            return null;
        }

        page.SetPixelBytes(pixels, px, py, _tileW, _tileH);
        return new Tile(page, source);
    }

    public void Clear()
    {
        foreach (var page in _pages)
        {
            page.Dispose();
        }

        _pages.Clear();
        _tiles.Clear();
        _next = 0;
        _tileW = 0;
    }

    public void Dispose() => Clear();
}
