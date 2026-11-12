using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace Zinc.Terminal;

/// <summary>
/// The font's glyphs, each drawn once into a texture and then copied, tinted with its cell's color, by one
/// sprite batch per frame, instead of a Direct2D glyph run per color run (which is thousands of calls on a busy
/// screen). This is the approach of Windows Terminal's AtlasEngine and the GPU terminals, inside Win2D.
///
/// A tile is three cells wide and two tall, with the glyph's own cell in its middle, so glyphs that reach past
/// their cell (Nerd Font icons, italics) keep their overhang. The glyph is drawn at the grid's fractional pixel
/// offset (from the padding), so its anti-aliasing is what a glyph run on screen would get. Direct2D's grayscale
/// text contrast depends on the text's color, so each glyph has a variant for light text and one for dark text.
/// Both are stored as white coverage (premultiplied, so a, a, a, a) for the batch to tint.
///
/// New glyphs are batched. <see cref="Get"/> reserves a tile, and <see cref="Flush"/> (before the frame's sprite
/// batch) draws every reserved glyph in one session onto a scratch copy of the page, then whitens that onto the page in
/// one effect draw. The scratch's empty parts come out transparent, so tiles already on the page stay as they are.
/// That makes two drawing sessions per frame however many glyphs are new, which keeps a screenful of new glyphs (a new
/// pane, a zoom step) cheap. Drawing per glyph, or reading each tile back to whiten it on the CPU, is far slower.
///
/// Lookups are arrays indexed by glyph number (one per font variant and light/dark), not a dictionary, because they
/// run for every cell of every frame.
///
/// Characters the font lacks (CJK and symbols, which Windows' font fallback finds) go in too, each rendered once with
/// a DirectWrite layout. Their text is stored as white coverage like glyphs, keyed by codepoint, or by its text for a
/// cluster. Emoji go on pages of their own, drawn in color and copied untinted. They're keyed by the text's color too,
/// for the occasional plain glyph in an emoji font. Drawing a layout per cell per frame would be far too slow for a
/// screen of CJK or emoji.
///
/// An atlas is shared by every pane drawing the same font at the same size, scale and sub-pixel offset on the same
/// device (<see cref="GlyphAtlases"/>), so ten panes don't rasterize and keep the same letters ten times. It's used
/// only on the UI thread, one frame at a time, so a pane's frame reserves tiles and flushes them before the next
/// pane's frame runs.
///
/// Tiles live in 512 px pages (about 1 MB each, and larger only if one tile is), added as needed. <see cref="Clear"/>
/// runs on any change of font, cell size, scale, device or padding offset, and after a flush that failed halfway.
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

    /// <summary>
    /// Pages not cleared yet, because a new render target can come with recycled GPU memory that still holds an old
    /// atlas's tiles.
    /// </summary>
    private readonly HashSet<int> _unclearedPages = [];

    // Fallback text (DirectWrite layouts): tile number + 1 by (codepoint or cluster, variant, wide, dark).
    private readonly Dictionary<(int Codepoint, int Variant, bool Wide, bool Dark), int> _textSlots = [];
    private readonly Dictionary<(string Cluster, int Variant, bool Wide, bool Dark), int> _clusterSlots = [];
    private readonly List<(string Text, TextStyle Style, bool Wide, bool Dark, int Page, Vector2 Origin, Rect Clip)> _pendingText = [];

    /// <summary>
    /// The font a fallback character is laid out in. The formats made from it are shared by every atlas and kept for
    /// the process's life. Panes share atlases, and a pane's own formats go away when its font or scale changes, so a
    /// character reserved with a pane's format and drawn after that format was gone would fail the frame.
    /// </summary>
    public readonly record struct TextStyle(string Family, float Size, int Weight, bool Italic);

    // Formats are made outside a frame where possible (PrepareFormat, called from SetFont), because the first layout with
    // a format naming the bundled font's file (file:///...ttf#Cascadia Mono NF) has Win2D resolve the path through
    // Windows.Storage. That's a COM call that pumps messages, and a pointer move pumped inside XAML's render tick ends
    // the process (a FailFast on reentrancy). Once realized, a format doesn't do that again.
    private static readonly Dictionary<TextStyle, CanvasTextFormat> Formats = [];

    private static CanvasTextFormat FormatFor(ICanvasResourceCreator device, TextStyle style)
    {
        if (!Formats.TryGetValue(style, out var format))
        {
            Formats[style] = format = new CanvasTextFormat
            {
                FontFamily = style.Family,
                FontSize = style.Size,
                FontWeight = new Windows.UI.Text.FontWeight((ushort)style.Weight),
                FontStyle = style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
                WordWrapping = CanvasWordWrapping.NoWrap,
                HorizontalAlignment = CanvasHorizontalAlignment.Center, // centered in its cell(s), as a cell showed it
            };
            using var realize = new CanvasTextLayout(device, "M", format, 100, 100);
        }

        return format;
    }

    /// <summary>Makes (and realizes) the shared format for <paramref name="style"/> now, outside any frame.</summary>
    public static void PrepareFormat(ICanvasResourceCreator device, TextStyle style) => FormatFor(device, style);

    // Emoji: their own pages, drawn in color, by (text, variant, wide, color).
    private readonly List<CanvasRenderTarget> _colorPages = [];
    private readonly HashSet<int> _unclearedColorPages = [];
    private readonly Dictionary<(int Codepoint, string? Cluster, int Variant, bool Wide, Color Color), Tile> _colorSlots = [];
    private readonly List<(string Text, TextStyle Style, bool Wide, Color Color, int Page, Vector2 Origin, Rect Clip)> _pendingColor = [];
    private int _nextColor;

    private CanvasRenderTarget? _scratch;
    private ColorMatrixEffect? _whiten;
    private int _next; // the next free tile slot on the last page
    private int _tileW, _tileH, _perRow, _perPage;
    private int _pageW, _pageH; // PagePixels, or a tile's size where one is larger (big zoom at a high display scale)
    private CellMetrics _metrics;
    private float _fracX, _fracY;

    /// <summary>A glyph's place in the atlas: the page and the tile's rectangle on it (DIPs of the page).</summary>
    public readonly record struct Tile(CanvasRenderTarget Page, Rect Source);

    /// <summary>
    /// The most pages an atlas keeps (about 1 MB each). Past that, the next frame starts over with only what's on
    /// screen. Without a limit, an atlas would only grow until a font, size or scale change, and a session paging through
    /// thousands of distinct characters or icons, or emoji in many text colors, could hold 100+ MB. 32 pages hold about
    /// 4,000 tiles at 150%, far more than one screen shows, so starting over is rare and never happens every frame.
    /// </summary>
    public const int MaxPages = 32;

    /// <summary>Pages in use (glyphs and emoji).</summary>
    public int PageCount => _pages.Count + _colorPages.Count;

    /// <summary>
    /// Runs at a frame's start, before any tile of this frame is handed out. Starts over if the atlas has grown past
    /// <see cref="MaxPages"/>, or if a frame failed between reserving tiles and drawing them, because those tiles would
    /// be drawn later with another pane's fonts, which may be gone by then.
    /// </summary>
    public void Trim()
    {
        if (PageCount > MaxPages)
        {
            Log.Write($"atlas: {PageCount} pages, starting over");
            Clear();
        }
        else if (_pending.Count + _pendingText.Count + _pendingColor.Count > 0)
        {
            Clear();
        }
    }

    /// <summary>
    /// Works out the tile size and the glyph's origin in it for these metrics, and drops everything if they changed.
    /// </summary>
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

    /// <summary>The tile for <paramref name="glyph"/>, or a new one reserved now and drawn by <see cref="Flush"/>.</summary>
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

        var (tile, page, cell) = Reserve(device, _pages, _unclearedPages, ref _next);
        // The origin is on the baseline.
        _pending.Add((face, fontSize, glyph, dark, page, cell + new Vector2(0, _metrics.Baseline), tile.Source));
        _tiles.Add(tile);
        slots[glyph] = _tiles.Count;
        return tile;
    }

    /// <summary>
    /// The tile for a character the font lacks (<paramref name="codepoint"/>, or a cluster's <paramref name="cluster"/>
    /// text), as white coverage like a glyph. It's laid out in <paramref name="format"/>, centered in one cell or two
    /// (<paramref name="wide"/>), on the grid's baseline.
    /// </summary>
    public Tile GetText(ICanvasResourceCreatorWithDpi device, int codepoint, string? cluster, TextStyle format, int variant, bool wide, bool dark)
    {
        int slot;
        var found = cluster == null
            ? _textSlots.TryGetValue((codepoint, variant, wide, dark), out slot)
            : _clusterSlots.TryGetValue((cluster, variant, wide, dark), out slot);
        if (found)
        {
            return _tiles[slot - 1];
        }

        var (tile, page, cell) = Reserve(device, _pages, _unclearedPages, ref _next);
        _pendingText.Add((cluster ?? char.ConvertFromUtf32(codepoint), format, wide, dark, page, cell, tile.Source));
        _tiles.Add(tile);
        if (cluster == null)
        {
            _textSlots[(codepoint, variant, wide, dark)] = _tiles.Count;
        }
        else
        {
            _clusterSlots[(cluster, variant, wide, dark)] = _tiles.Count;
        }

        return tile;
    }

    /// <summary>
    /// The tile for an emoji (<paramref name="codepoint"/>, or a cluster's <paramref name="cluster"/> text), drawn in
    /// color and copied untinted. <paramref name="color"/> is the text's color.
    /// </summary>
    public Tile GetColor(ICanvasResourceCreatorWithDpi device, int codepoint, string? cluster, TextStyle format, int variant, bool wide, Color color)
    {
        if (_colorSlots.TryGetValue((codepoint, cluster, variant, wide, color), out var known))
        {
            return known;
        }

        var (tile, page, cell) = Reserve(device, _colorPages, _unclearedColorPages, ref _nextColor);
        _pendingColor.Add((cluster ?? char.ConvertFromUtf32(codepoint), format, wide, color, page, cell, tile.Source));
        _colorSlots[(codepoint, cluster, variant, wide, color)] = tile;
        return tile;
    }

    /// <summary>
    /// A free tile on <paramref name="pages"/>, adding a page when the last one is full, and the top-left of the tile's
    /// cell in DIPs of the page.
    /// </summary>
    private (Tile Tile, int Page, Vector2 Cell) Reserve(ICanvasResourceCreatorWithDpi device, List<CanvasRenderTarget> pages,
        HashSet<int> uncleared, ref int next)
    {
        var s = _metrics.Scale;
        if (pages.Count == 0 || next >= _perPage)
        {
            pages.Add(new CanvasRenderTarget(device, _pageW / s, _pageH / s, 96 * s));
            uncleared.Add(pages.Count - 1);
            next = 0;
        }

        var place = next++;
        var px = place % _perRow * _tileW;
        var py = place / _perRow * _tileH;
        // The glyph's cell is one cell in and half a cell down its tile, at the grid's fractional offset.
        var cell = new Vector2((px + (int)_metrics.WidthPx + _fracX) / s, (py + (int)_metrics.HeightPx / 2 + _fracY) / s);
        return (new Tile(pages[^1], new Rect(px / s, py / s, _tileW / s, _tileH / s)), pages.Count - 1, cell);
    }

    /// <summary>A layout of <paramref name="text"/> as a cell showed it: centered in its cell(s), and how far down its top goes so its baseline is the grid's.</summary>
    private (CanvasTextLayout Layout, float Down) Layout(ICanvasResourceCreator device, string text, TextStyle style, bool wide, bool color)
    {
        var width = wide ? 2 * _metrics.Width : _metrics.Width;
        var format = FormatFor(device, style);
        var layout = new CanvasTextLayout(device, text, format, width, _metrics.Height);
        layout.Options = color ? CanvasDrawTextOptions.EnableColorFont : CanvasDrawTextOptions.Default;
        var lines = layout.LineMetrics;
        return (layout, lines.Length > 0 ? _metrics.Baseline - lines[0].Baseline : 0);
    }

    /// <summary>Draws the glyphs reserved since the last flush into their tiles (before the batch that copies them).</summary>
    public void Flush(ICanvasResourceCreatorWithDpi device)
    {
        if (_pending.Count == 0 && _pendingText.Count == 0 && _pendingColor.Count == 0)
        {
            return;
        }

        try
        {
            FlushPending(device);
        }
        catch
        {
            // Half drawn, so the reserved tiles would stay blank or get drawn twice. Start over next frame.
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

        foreach (var page in _pending.Select(p => p.Page).Concat(_pendingText.Select(p => p.Page)).Distinct().ToList())
        {
            // First the page's new glyphs alone, on transparent, at their tiles. They're white for light text and black
            // for dark, so each gets Direct2D's contrast for its color.
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

                foreach (var (text, format, wide, dark, _, cell, clip) in _pendingText.Where(p => p.Page == page))
                {
                    using var layer = ds.CreateLayer(1, clip);
                    var (layout, down) = Layout(ds, text, format, wide, color: false);
                    using (layout)
                    {
                        ds.DrawTextLayout(layout, cell.X, cell.Y + down, dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255));
                    }
                }
            }

            // Then the scratch goes onto the page as white coverage. Normal blending works because everything else in the
            // scratch is transparent and each tile is written once (CanvasBlend.Copy with an effect draws nothing). A new
            // page is cleared first, because its memory may hold a previous atlas's tiles, and new glyphs blended over
            // those fragments would show as garbled text.
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
        _pendingText.Clear();

        // Emoji go straight onto their pages in color, so there's nothing to whiten.
        foreach (var page in _pendingColor.Select(p => p.Page).Distinct().ToList())
        {
            using var ds = _colorPages[page].CreateDrawingSession();
            if (_unclearedColorPages.Remove(page))
            {
                ds.Clear(Color.FromArgb(0, 0, 0, 0));
            }

            ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
            foreach (var (text, format, wide, color, _, cell, clip) in _pendingColor.Where(p => p.Page == page))
            {
                using var layer = ds.CreateLayer(1, clip);
                var (layout, down) = Layout(ds, text, format, wide, color: true);
                using (layout)
                {
                    ds.DrawTextLayout(layout, cell.X, cell.Y + down, color);
                }
            }
        }

        _pendingColor.Clear();
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
        _textSlots.Clear();
        _clusterSlots.Clear();
        _pendingText.Clear();
        foreach (var page in _colorPages)
        {
            page.Dispose();
        }

        _colorPages.Clear();
        _unclearedColorPages.Clear();
        _colorSlots.Clear();
        _pendingColor.Clear();
        _nextColor = 0;
        _scratch?.Dispose();
        _scratch = null;
        _whiten?.Dispose();
        _whiten = null;
        _next = 0;
        _tileW = 0;
    }

    public void Dispose() => Clear();
}

/// <summary>
/// The glyph atlases panes share (UI thread only). There's one per (device, font, size, cell metrics, sub-pixel
/// offset), kept while a pane uses it. A pane whose font, zoom, scale or padding changes moves to the atlas for its new
/// settings.
/// </summary>
internal static class GlyphAtlases
{
    /// <summary>
    /// What decides an atlas's tiles. The device is keyed by its native pointer, because a projected object may come
    /// back as another wrapper of the same device. The font is keyed as the text format names it (the bundled font by
    /// its file).
    /// </summary>
    public readonly record struct Key(nint Device, string Font, float Size, CellMetrics Metrics, float FracX, float FracY)
    {
        public static nint DeviceOf(CanvasDevice device) => ((WinRT.IWinRTObject)device).NativeObject.ThisPtr;
    }

    private static readonly Dictionary<Key, (GlyphAtlas Atlas, int Users)> Shared = [];

    /// <summary>Panes per atlas now (diagnostics).</summary>
    public static string Describe() => string.Join(", ", Shared.Values.Select(s => $"{s.Users} pane(s), {s.Atlas.PageCount} page(s)"));

    /// <summary>The atlas for <paramref name="key"/>, made if no pane has it. Pair it with <see cref="Release"/>.</summary>
    public static GlyphAtlas Acquire(Key key)
    {
        if (Shared.TryGetValue(key, out var shared))
        {
            Shared[key] = (shared.Atlas, shared.Users + 1);
            return shared.Atlas;
        }

        var atlas = new GlyphAtlas();
        atlas.Prepare(key.Metrics, key.FracX, key.FracY);
        Shared[key] = (atlas, 1);
        Log.Write($"atlas: new for {key.Size:0.##} DIP at {key.Metrics.Scale:0.##}x ({Shared.Count} in use)");
        return atlas;
    }

    /// <summary>A pane stopped using <paramref name="key"/>'s atlas, which is freed once no pane uses it.</summary>
    public static void Release(Key key)
    {
        if (!Shared.TryGetValue(key, out var shared))
        {
            return;
        }

        if (shared.Users > 1)
        {
            Shared[key] = (shared.Atlas, shared.Users - 1);
            return;
        }

        Shared.Remove(key);
        shared.Atlas.Dispose();
        Log.Write($"atlas: freed ({Shared.Count} in use)");
    }
}
