using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Zinc.Ghostty;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using Windows.UI.Text;
using Colors = Microsoft.UI.Colors;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace Zinc.Terminal;

/// <summary>Cell geometry. DIP values derive from whole device pixels so cell edges land on pixels.</summary>
public readonly record struct CellMetrics(float Width, float Height, float Baseline, uint WidthPx, uint HeightPx, float Scale);

/// <summary>Visual state the renderer needs from its view.</summary>
public readonly record struct CursorPresentation(bool Focused, bool BlinkOn);

/// <summary>
/// Draws a libghostty-vt render state with Win2D: backgrounds as merged
/// rectangles, text as glyph runs with a fixed one-cell advance (so every glyph
/// sits on the grid), DirectWrite layout for glyphs the font lacks, decorations
/// and the cursor.
/// </summary>
public sealed unsafe class TerminalRenderer : IDisposable
{
    private Config _config;

    /// <summary>Where frame times go while a pane is measured (ZINC_PERF, the benchmark); null otherwise.</summary>
    internal FramePerf? Perf { get; set; }

    /// <summary>The settings drawn with (font family, padding); replaced on a config reload.</summary>
    public Config Config
    {
        get => _config;
        set
        {
            if (value.AdjustBoxThickness != _config.AdjustBoxThickness)
            {
                ClearSprites(); // drawn with the old line thickness
            }

            _config = value;
        }
    }
    private readonly nint _renderState, _rowIterator, _rowCells;
    private readonly CanvasTextFormat?[] _formats = new CanvasTextFormat?[4];
    private readonly CanvasFontFace?[] _faces = new CanvasFontFace?[4];
    private readonly Dictionary<(uint, int), int> _glyphCache = new();
    // The first 2,048 codepoints (Latin, Greek, Cyrillic...) per variant: glyph index + 1, 0 = not looked up yet.
    // Every cell of every frame asks; an array beats the dictionary's tuple hashing.
    private readonly int[]?[] _glyphFast = new int[]?[4];

    // The bundled Cascadia Mono NF, tried before Windows' font fallback for cell-filling shapes
    // the configured font lacks (it covers most of Symbols for Legacy Computing, which no Windows
    // font does: flow draws its tabs with them). Its cell at our font size, for fitting them.
    private readonly CanvasFontFace?[] _bundledFaces = new CanvasFontFace?[4];
    private readonly Dictionary<(uint, int), int> _bundledGlyphCache = new();
    private float _bundledAdvance, _bundledLineHeight, _bundledBaseline;

    // Ghostty's sprites (box drawing, blocks, Legacy Computing, Powerline, Braille: Sprites.cs),
    // one white coverage bitmap per codepoint at the current cell size (null: drew nothing),
    // tinted per cell by a sprite batch. This row's sprite cells wait in _rowSprites.
    private readonly Dictionary<uint, (CanvasBitmap Bitmap, int X, int Y)?> _sprites = new();
    private readonly List<(int Column, uint Codepoint, Color Color)> _rowSprites = [];
    private bool _spriteBatchSupported;
    private CanvasSolidColorBrush? _brush;
    private string _fontFamily;
    private float _fontSize;

    // Per-row scratch buffers reused across frames.
    private Cell[] _cells = [];
    private KittyPlaceholders.Cell?[] _placeholders = [];
    private bool _rowHasPlaceholders;

    // The frame's rows, all read before anything is drawn: the backgrounds go first, as one bitmap
    // (DrawBackgrounds), then each row's text and the rest. _cells, _placeholders and _rowHasPlaceholders
    // point at the row being read or drawn; character data is one buffer for the frame (_codepointsUsed).
    private Cell[][] _frameCells = [];
    private KittyPlaceholders.Cell?[][] _framePlaceholders = [];
    private bool[] _frameHasPlaceholders = [];
    private int[] _frameCounts = [];
    private int _frameColumns, _columns, _codepointsUsed;

    /// <summary>One pixel per cell (premultiplied BGRA): its background, transparent for the default one.</summary>
    private byte[] _bgPixels = [];
    private CanvasBitmap? _bgBitmap;

    // Text through the glyph atlas: each frame's glyphs (and box-drawing sprites) are collected while the rows
    // are drawn, then copied in one sprite batch. ZINC_GLYPH_ATLAS=0 keeps the glyph runs (for comparisons).
    private static readonly bool AtlasEnabled = Environment.GetEnvironmentVariable("ZINC_GLYPH_ATLAS") != "0";
    private readonly GlyphAtlas _atlas = new();
    private bool _batching;
    private int _gridX, _gridY; // the grid's top-left device pixel (the padding, rounded down)
    private readonly List<(CanvasBitmap Bitmap, Rect Destination, Rect? Source, Vector4 Tint)> _frameSprites = [];
    private readonly List<KittyPlaceholders.Run> _runs = [];
    private ulong _viewportTop; // screen row at the top of the viewport, this frame
    private uint[] _codepoints = new uint[1024];
    private CanvasGlyph[] _glyphs = [];

    // Kitty graphics: this frame's placements, and one GPU bitmap per image id (with the
    // image generation it was made from).
    private readonly List<ImagePlacement> _images = [];
    private readonly Dictionary<uint, (ulong Generation, CanvasBitmap Bitmap)> _bitmaps = new();
    private readonly HashSet<uint> _pendingImages = [];
    private readonly Func<uint, ulong, bool> _needsPixels;

    public TerminalRenderer(Config config)
    {
        _needsPixels = (id, generation) =>
            (!_bitmaps.TryGetValue(id, out var cached) || cached.Generation != generation) && _pendingImages.Add(id);
        _config = config;
        _fontFamily = config.FontFamily;
        Check(Native.RenderStateNew(0, out _renderState));
        Check(Native.RowIteratorNew(0, out _rowIterator));
        Check(Native.RowCellsNew(0, out _rowCells));
    }

    public CellMetrics Metrics { get; private set; }

    /// <summary>Current font size in DIPs.</summary>
    public float FontSize => _fontSize;

    /// <summary>The regular face's family as XAML text can name it (the bundled font by its file), for the IME's box.</summary>
    public string TextFontFamily { get; private set; } = "Consolas";

    /// <summary>The cursor's cell in the viewport at the last frame (null: off screen), where input methods compose.</summary>
    public (int Column, int Row)? CursorCell { get; private set; }

    /// <summary>What custom shaders read from the last frame (palette, colors, the cursor as drawn); null: not kept.</summary>
    public ShaderFrame? ForShaders { get; set; }

    public bool HasResources => _brush != null && Metrics.Width > 0;

    // The grid's top-left corner (DIPs): the padding, plus half the spare room with padding-balance (SetGridOrigin).
    private float? _gridLeft, _gridTop;
    private float PadLeft => _gridLeft ?? _config.PaddingLeft;
    private float PadTop => _gridTop ?? _config.PaddingTop;

    /// <summary>Where the pane put the grid (<see cref="GridLayout"/>).</summary>
    public void SetGridOrigin(float left, float top) => (_gridLeft, _gridTop) = (left, top);

    public void CreateResources(ICanvasResourceCreator device)
    {
        _brush?.Dispose(); // the old device's (a lost device, a replaced canvas)
        _brush = new CanvasSolidColorBrush(device, Colors.White);
        // Bitmaps belong to the old device.
        foreach (var entry in _bitmaps.Values)
        {
            entry.Bitmap.Dispose();
        }

        _bitmaps.Clear();
        ClearSprites();
        _bgBitmap?.Dispose(); // the old device's
        _bgBitmap = null;
        _spriteBatchSupported = CanvasSpriteBatch.IsSupported(device.Device);
        if (!_spriteBatchSupported)
        {
            Log.Write("sprites: this device can't batch sprites; box drawing etc. come from the font");
        }
    }

    private void ClearSprites()
    {
        _atlas.Clear(); // same reasons: font, cell size, device
        foreach (var sprite in _sprites.Values)
        {
            sprite?.Bitmap.Dispose();
        }

        _sprites.Clear();
    }

    /// <summary>Loads the font at <paramref name="sizeDip"/> and recomputes cell metrics.</summary>
    public void SetFont(ICanvasResourceCreator device, float sizeDip, float scale)
    {
        _fontSize = sizeDip;
        var fonts = CanvasFontSet.GetSystemFontSet();
        // Fall back if the configured family isn't installed (DirectWrite would
        // otherwise substitute a proportional font and break the grid). Cascadia Mono NF ships
        // with Zinc, so it's there even when not installed.
        var bundled = false;
        foreach (var candidate in new[] { _config.FontFamily, BundledFonts.Family, "Cascadia Mono", "Consolas" })
        {
            if (fonts.GetMatchingFonts(candidate, FontWeights.Normal, FontStretch.Normal, FontStyle.Normal).Fonts.Count > 0)
            {
                _fontFamily = candidate;
                break;
            }

            if (candidate.Equals(BundledFonts.Family, StringComparison.OrdinalIgnoreCase) && BundledFonts.Set(italic: false) is { } set &&
                set.GetMatchingFonts(BundledFonts.Family, FontWeights.Normal, FontStretch.Normal, FontStyle.Normal).Fonts.Count > 0)
            {
                _fontFamily = BundledFonts.Family;
                bundled = true;
                break;
            }
        }

        Log.Write($"font: {_fontFamily}{(bundled ? " (bundled)" : "")}");
        TextFontFamily = bundled ? BundledFonts.FormatFamily(italic: false) : _fontFamily;
        for (var v = 0; v < 4; v++)
        {
            var weight = (v & 1) != 0 ? FontWeights.Bold : FontWeights.Normal;
            var style = (v & 2) != 0 ? FontStyle.Italic : FontStyle.Normal;
            var italic = (v & 2) != 0;
            _formats[v]?.Dispose(); // the previous size's (each zoom step, each scale change)
            _formats[v] = new CanvasTextFormat
            {
                FontFamily = bundled ? BundledFonts.FormatFamily(italic) : _fontFamily,
                FontSize = _fontSize,
                FontWeight = weight,
                FontStyle = style,
                WordWrapping = CanvasWordWrapping.NoWrap,
                Options = CanvasDrawTextOptions.EnableColorFont,
            };

            var faces = bundled ? BundledFonts.Set(italic) ?? fonts : fonts;
            var matches = faces.GetMatchingFonts(_fontFamily, weight, FontStretch.Normal, style).Fonts;
            _faces[v] = matches.Count > 0 ? matches[0] : _faces[0];

            // (Not when it is the font: what that lacks, it lacks.)
            var bundledMatches = bundled ? null : BundledFonts.Set(italic)?.GetMatchingFonts(BundledFonts.Family, weight, FontStretch.Normal, style).Fonts;
            _bundledFaces[v] = bundledMatches is { Count: > 0 } ? bundledMatches[0] : null;
        }

        _glyphCache.Clear();
        Array.Clear(_glyphFast);
        _bundledGlyphCache.Clear();
        ClearSprites(); // drawn for the old cell size
        if (_bundledFaces[0] != null)
        {
            using var format = new CanvasTextFormat { FontFamily = BundledFonts.FormatFamily(italic: false), FontSize = _fontSize, WordWrapping = CanvasWordWrapping.NoWrap };
            using var cell = new CanvasTextLayout(device, "M", format, float.MaxValue, float.MaxValue);
            _bundledAdvance = (float)cell.LayoutBounds.Width;
            _bundledLineHeight = (float)cell.LayoutBounds.Height;
            _bundledBaseline = cell.LineMetrics[0].Baseline;
        }

        // Measure a run of M's: the advance of a monospace glyph and the
        // natural line height of the font.
        using var layout = new CanvasTextLayout(device, "MMMMMMMMMM", _formats[0], float.MaxValue, float.MaxValue);
        var bounds = layout.LayoutBounds;
        var widthPx = (uint)Math.Max(1, Math.Round(bounds.Width / 10 * scale));
        var heightPx = (uint)Math.Max(1, Math.Ceiling(bounds.Height * scale));
        Metrics = new CellMetrics(
            widthPx / scale, heightPx / scale,
            MathF.Round(layout.LineMetrics[0].Baseline * scale) / scale,
            widthPx, heightPx, scale);
    }

    /// <summary>Grows the per-row buffers to hold <paramref name="cols"/> cells.</summary>
    public void EnsureColumns(int cols)
    {
        _columns = Math.Max(_columns, cols);
        if (_cells.Length < cols)
        {
            _cells = new Cell[cols];
            _glyphs = new CanvasGlyph[cols];
            _placeholders = new KittyPlaceholders.Cell?[cols];
        }
    }

    /// <summary>Find results, oldest first (screen rows); highlighted where on screen.</summary>
    public IReadOnlyList<SearchMatch>? SearchMatches { get; set; }

    /// <summary>Index into <see cref="SearchMatches"/> of the selected match (drawn stronger).</summary>
    public int CurrentMatch { get; set; } = -1;

    public static readonly Color MatchColor = Color.FromArgb(0x60, 0xFF, 0xD5, 0x4A);
    public static readonly Color CurrentMatchColor = Color.FromArgb(0xC0, 0xFF, 0x96, 0x1A);

    /// <summary>A Find highlight: <c>find-highlight</c> / <c>find-highlight-current</c> (solid), else Zinc's own (see-through).</summary>
    public static Color MatchColorFor(Config config, bool current) =>
        (current ? config.SearchSelectedBackground : config.SearchBackground) is { } chosen
            ? ToColor(chosen)
            : current ? CurrentMatchColor : MatchColor;

    /// <summary>The palette's colors 8-15 this frame (bold-is-bright).</summary>
    private readonly Color[] _bright = new Color[8];

    /// <summary>The link under the pointer while Ctrl is held; drawn underlined.</summary>
    public LinkMatch? HoverLink { get; set; }

    /// <summary>Draws one frame. Returns whether the cursor is visible and blinking.</summary>
    public bool Draw(CanvasDrawingSession ds, TerminalSession session, int rows, CursorPresentation presentation)
    {
        if (!HasResources)
        {
            return false;
        }

        _images.Clear();
        _pendingImages.Clear();
        lock (session.Lock)
        {
            // Synchronized output: while a program holds the screen, draw the frame captured when
            // the hold began (CaptureHeldFrame); give up on a hold that lasts over a second.
            if (session.RenderHeld && Environment.TickCount64 - session.RenderHeldSince >= RenderHoldTimeoutMs)
            {
                Log.Write("render hold: released after a second (the program never ended it)");
                session.EndRenderHold();
            }

            if (!session.RenderHeld)
            {
                lock (_stateLock)
                {
                    if (Native.RenderStateUpdate(_renderState, session.Handle) != GhosttyResult.Success)
                    {
                        return false;
                    }
                }
            }

            // Under the same lock, so images and text come from the same terminal state.
            session.CollectImages(_images, _needsPixels);
            GhosttyTerminalScrollbar scrollbar;
            Native.TerminalGet(session.Handle, TerminalData.Scrollbar, &scrollbar);
            _viewportTop = scrollbar.Offset;
        }

        UpdateImageCache(ds);

        // The render state is a snapshot, so the rest runs without the terminal's lock; only a
        // hold beginning (on the reader thread) may refresh it meanwhile, hence _stateLock.
        lock (_stateLock)
        {
            return DrawSnapshot(ds, rows, presentation);
        }
    }

    /// <summary>How long a program may hold the screen (synchronized output) before we draw anyway.</summary>
    public const int RenderHoldTimeoutMs = 1000;

    private readonly object _stateLock = new();

    /// <summary>
    /// A program began holding the screen: keep what's there now. Called by the session on the
    /// reader thread, under its lock (TerminalSession.RenderHoldBegan).
    /// </summary>
    public void CaptureHeldFrame(TerminalSession session)
    {
        lock (_stateLock)
        {
            Native.RenderStateUpdate(_renderState, session.Handle);
        }
    }

    private bool DrawSnapshot(CanvasDrawingSession ds, int rows, CursorPresentation presentation)
    {
        var colors = new GhosttyRenderStateColors { Size = (nuint)sizeof(GhosttyRenderStateColors) };
        Native.RenderStateGet(_renderState, RenderStateData.Colors, &colors);
        var cursor = new GhosttyRenderStateCursor { Size = (nuint)sizeof(GhosttyRenderStateCursor) };
        Native.RenderStateGet(_renderState, RenderStateData.Cursor, &cursor);

        var defaultFg = ToColor(colors.Foreground);
        var defaultBg = ToColor(colors.Background);
        var cursorColor = colors.CursorHasValue != 0 ? ToColor(colors.Cursor) : defaultFg;
        if (Config.CursorOpacity < 1)
        {
            cursorColor.A = (byte)(255 * Config.CursorOpacity);
        }

        if (ForShaders is { } shaders)
        {
            new ReadOnlySpan<byte>(colors.Palette, 768).CopyTo(shaders.Palette);
            (shaders.Background, shaders.Foreground) = (defaultBg, defaultFg);
            shaders.CursorColor = colors.CursorHasValue != 0 ? ToColor(colors.Cursor) : null;
            shaders.CursorVisible = cursor.Visible != 0;
            shaders.CursorDrawn = false;
        }

        // The palette's bright colors as programs left them (OSC 4), for bold-is-bright.
        for (var i = 0; i < 8; i++)
        {
            _bright[i] = Color.FromArgb(255, colors.Palette[(i + 8) * 3], colors.Palette[(i + 8) * 3 + 1], colors.Palette[(i + 8) * 3 + 2]);
        }

        // A see-through default background (background-opacity) lets the window's backdrop show.
        ds.Clear(Config.BackgroundOpacity < 1 ? Color.FromArgb((byte)(255 * Config.BackgroundOpacity), defaultBg.R, defaultBg.G, defaultBg.B) : defaultBg);
        DrawImages(ds, rows, belowText: true);

        var iterator = _rowIterator;
        Native.RenderStateGet(_renderState, RenderStateData.RowIterator, &iterator);

        var m = Metrics;
        var perf = Perf;
        var t = perf != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        // Every row first (the backgrounds are drawn before any text)...
        var cols = Math.Max(_columns, _cells.Length);
        EnsureFrame(rows, cols);
        _codepointsUsed = 0;
        var read = 0;
        while (Native.RowIteratorNext(_rowIterator) != 0 && read < rows)
        {
            _cells = _frameCells[read];
            _placeholders = _framePlaceholders[read];
            _frameCounts[read] = ReadRow(defaultFg, defaultBg);
            _frameHasPlaceholders[read] = _rowHasPlaceholders;
            read++;
        }

        if (perf != null) t = perf.Mark(0, t);
        DrawBackgrounds(ds, read, cols);
        if (perf != null) t = perf.Mark(1, t);

        // ...then each row's highlights and text (into the frame's sprite batch where the atlas is on), the batch,
        // and each row's decorations and cursor on top.
        _batching = AtlasEnabled && _spriteBatchSupported;
        // Left over only if a frame failed halfway (OnDraw catches): its sprites may point into pages since disposed
        // (a device lost), which would fail every frame after.
        _frameSprites.Clear();
        _rowSprites.Clear();
        if (_batching)
        {
            var padX = PadLeft * m.Scale;
            var padY = PadTop * m.Scale;
            _gridX = (int)MathF.Floor(padX);
            _gridY = (int)MathF.Floor(padY);
            _atlas.Prepare(m, padX - _gridX, padY - _gridY);
        }

        for (var y = 0; y < read; y++)
        {
            var count = _frameCounts[y];
            _cells = _frameCells[y];

            _placeholders = _framePlaceholders[y];
            _rowHasPlaceholders = _frameHasPlaceholders[y];
            var top = PadTop + y * m.Height;
            DrawMatches(ds, y, count, top);
            if (perf != null) t = perf.Mark(1, t);
            if (_rowHasPlaceholders)
            {
                DrawPlaceholders(ds, count, top);
            }

            DrawText(ds, count, top, y);
            if (perf != null) t = perf.Mark(2, t);
            if (_batching)
            {
                continue; // decorations and the cursor go over the batch, below
            }

            DrawDecorations(ds, count, top);
            if (HoverLink?.ColumnsOn(y, count) is { } span)
            {
                DrawLinkUnderline(ds, span.First, span.Last, count, top);
            }

            if (cursor.Visible != 0 && cursor.ViewportHasValue != 0 && cursor.ViewportY == y)
            {
                DrawCursor(ds, cursor, count, top, cursorColor, defaultBg, presentation);
            }

            if (perf != null) t = perf.Mark(3, t);
        }

        if (_batching)
        {
            DrawFrameSprites(ds);
            _batching = false; // the cursor redraws its cell's glyph directly
            if (perf != null) t = perf.Mark(2, t);
            for (var y = 0; y < read; y++)
            {
                _cells = _frameCells[y];
                var count = _frameCounts[y];
                var top = PadTop + y * m.Height;
                DrawDecorations(ds, count, top);
                if (HoverLink?.ColumnsOn(y, count) is { } span)
                {
                    DrawLinkUnderline(ds, span.First, span.Last, count, top);
                }

                if (cursor.Visible != 0 && cursor.ViewportHasValue != 0 && cursor.ViewportY == y)
                {
                    DrawCursor(ds, cursor, count, top, cursorColor, defaultBg, presentation);
                }
            }

            if (perf != null) t = perf.Mark(3, t);
        }

        DrawImages(ds, rows, belowText: false);
        Native.RenderStateClean(_renderState);
        CursorCell = cursor.ViewportHasValue != 0 ? ((int)cursor.ViewportX, (int)cursor.ViewportY) : null;
        return cursor.Blinking != 0 && cursor.Visible != 0;
    }

    public void Dispose()
    {
        foreach (var entry in _bitmaps.Values)
        {
            entry.Bitmap.Dispose();
        }

        _bitmaps.Clear();
        ClearSprites();
        _bgBitmap?.Dispose();
        _atlas.Dispose();
        Native.RowCellsFree(_rowCells);
        Native.RowIteratorFree(_rowIterator);
        Native.RenderStateFree(_renderState);
    }

    // ---- rows ----

    [Flags]
    private enum CellFlags : ushort
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Faint = 4,
        Invisible = 8,
        Strikethrough = 16,
        Overline = 32,
        Wide = 64,
        SpacerTail = 128,
        Placeholder = 256, // kitty graphics Unicode placeholder: an image slice, not text
    }

    private struct Cell
    {
        public int CpStart;
        public int CpLen;
        public Color Fg;
        public Color Bg;
        public bool HasBg;
        public CellFlags Flags;
        public int Underline;

        public readonly int FontVariant => (int)(Flags & (CellFlags.Bold | CellFlags.Italic));
    }

    private int ReadRow(Color defaultFg, Color defaultBg)
    {
        var cells = _rowCells;
        Native.RowGet(_rowIterator, RenderStateRowData.Cells, &cells);

        var sel = new GhosttyRenderStateRowSelection { Size = (nuint)sizeof(GhosttyRenderStateRowSelection) };
        var hasSelection = Native.RowGet(_rowIterator, RenderStateRowData.Selection, &sel) == GhosttyResult.Success;
        var theme = Config.Theme;
        Color? selectionBg = hasSelection && theme.SelectionBackground is { } sb ? ToColor(sb) : null;
        Color? selectionFg = hasSelection && theme.SelectionForeground is { } sf ? ToColor(sf) : null;

        var x = 0;
        var cp = _codepointsUsed; // the frame's rows share one buffer
        _rowHasPlaceholders = false;
        while (x < _cells.Length && Native.RowCellsNext(_rowCells) != 0)
        {
            ref var cell = ref _cells[x];
            cell = default;

            uint len = 0;
            Native.RowCellsGet(_rowCells, RenderStateRowCellsData.GraphemesLen, &len);
            if (len > 0)
            {
                if (cp + len > _codepoints.Length)
                {
                    Array.Resize(ref _codepoints, Math.Max(_codepoints.Length * 2, cp + (int)len));
                }

                fixed (uint* p = &_codepoints[cp])
                {
                    Native.RowCellsGet(_rowCells, RenderStateRowCellsData.GraphemesBuf, p);
                }

                cell.CpStart = cp;
                cell.CpLen = (int)len;
                cp += (int)len;
            }

            ulong raw;
            if (Native.RowCellsGet(_rowCells, RenderStateRowCellsData.Raw, &raw) == GhosttyResult.Success)
            {
                int wide;
                if (Native.CellGet(raw, CellData.Wide, &wide) == GhosttyResult.Success)
                {
                    if (wide == (int)CellWide.Wide) cell.Flags |= CellFlags.Wide;
                    else if (wide == (int)CellWide.SpacerTail) cell.Flags |= CellFlags.SpacerTail;
                }
            }

            _placeholders[x] = null;
            var inverse = false;
            uint fgId = 0, underlineId = 0;
            var (fgTag, fgValue) = (0, 0UL);
            byte hasStyling = 0;
            Native.RowCellsGet(_rowCells, RenderStateRowCellsData.HasStyling, &hasStyling);
            if (hasStyling != 0)
            {
                var style = new GhosttyStyle { Size = (nuint)sizeof(GhosttyStyle) };
                Native.RowCellsGet(_rowCells, RenderStateRowCellsData.Style, &style);
                if (style.Bold != 0) cell.Flags |= CellFlags.Bold;
                if (style.Italic != 0) cell.Flags |= CellFlags.Italic;
                if (style.Faint != 0) cell.Flags |= CellFlags.Faint;
                if (style.Invisible != 0) cell.Flags |= CellFlags.Invisible;
                if (style.Strikethrough != 0) cell.Flags |= CellFlags.Strikethrough;
                if (style.Overline != 0) cell.Flags |= CellFlags.Overline;
                cell.Underline = style.Underline;
                inverse = style.Inverse != 0;
                fgId = KittyPlaceholders.ColorId(style.FgColorTag, style.FgColorValue);
                (fgTag, fgValue) = (style.FgColorTag, style.FgColorValue);
                underlineId = KittyPlaceholders.ColorId(style.UnderlineColorTag, style.UnderlineColorValue);
            }

            if (cell.CpLen > 0 && _codepoints[cell.CpStart] == KittyPlaceholders.Placeholder)
            {
                _placeholders[x] = KittyPlaceholders.Parse(_codepoints.AsSpan(cell.CpStart + 1, cell.CpLen - 1), fgId, underlineId);
                cell.Flags |= CellFlags.Placeholder;
                _rowHasPlaceholders = true;
            }

            GhosttyColorRgb rgb;
            cell.Fg = Native.RowCellsGet(_rowCells, RenderStateRowCellsData.FgColor, &rgb) == GhosttyResult.Success
                ? ToColor(rgb)
                : defaultFg;
            if (Native.RowCellsGet(_rowCells, RenderStateRowCellsData.BgColor, &rgb) == GhosttyResult.Success)
            {
                cell.Bg = ToColor(rgb);
                cell.HasBg = true;
            }

            // Bold: palette colors 0-7 in their bright versions, the default color in bold-color.
            if ((cell.Flags & CellFlags.Bold) != 0)
            {
                if (fgTag == 1 && (fgValue & 0xFF) < 8 && Config.BoldIsBright)
                {
                    cell.Fg = _bright[fgValue & 0xFF];
                }
                else if (fgTag == 0 && Config.BoldColor is { } bold)
                {
                    cell.Fg = ToColor(bold);
                }
            }

            // Selection: the scheme's (or config's) selection colors, else inverse video, which
            // works with any theme.
            var selected = hasSelection && x >= sel.StartX && x <= sel.EndX;
            if (selected && selectionBg is { } selBg)
            {
                inverse = false;
                cell.Bg = selBg;
                cell.HasBg = true;
            }
            else if (selected)
            {
                inverse = !inverse;
            }

            if (inverse)
            {
                var bg = cell.HasBg ? cell.Bg : defaultBg;
                cell.Bg = cell.Fg;
                cell.Fg = bg;
                cell.HasBg = true;
            }

            if (selected && selectionFg is { } selFg)
            {
                cell.Fg = selFg;
            }

            if (Config.MinimumContrast > 1)
            {
                var (r, g, b) = MinimumContrast.Ensure((cell.Fg.R, cell.Fg.G, cell.Fg.B),
                    cell.HasBg ? (cell.Bg.R, cell.Bg.G, cell.Bg.B) : (defaultBg.R, defaultBg.G, defaultBg.B), Config.MinimumContrast);
                cell.Fg = Color.FromArgb(255, r, g, b);
            }

            if ((cell.Flags & CellFlags.Faint) != 0)
            {
                cell.Fg = Blend(cell.Fg, cell.HasBg ? cell.Bg : defaultBg, 1 - (float)Config.FaintOpacity);
            }

            x++;
        }

        _codepointsUsed = cp;
        return x;
    }

    /// <summary>Room for <paramref name="rows"/> rows of <paramref name="cols"/> cells (grown, never shrunk).</summary>
    private void EnsureFrame(int rows, int cols)
    {
        if (_frameCells.Length >= rows && _frameColumns >= cols)
        {
            return;
        }

        rows = Math.Max(rows, _frameCells.Length);
        cols = Math.Max(cols, _frameColumns);
        _frameCells = new Cell[rows][];
        _framePlaceholders = new KittyPlaceholders.Cell?[rows][];
        for (var y = 0; y < rows; y++)
        {
            _frameCells[y] = new Cell[cols];
            _framePlaceholders[y] = new KittyPlaceholders.Cell?[cols];
        }

        _frameHasPlaceholders = new bool[rows];
        _frameCounts = new int[rows];
        _frameColumns = cols;
    }

    /// <summary>
    /// Every cell's background in one draw: a bitmap with one pixel per cell, stretched over the grid without
    /// smoothing (cells are whole device pixels, so each pixel covers exactly one cell). It replaced a rectangle
    /// per run of same-colored cells (thousands of Direct2D calls on a busy screen). Measured 2026-10-02: the
    /// backgrounds went from ~2 ms to ~0.1 ms a frame, but ~1.5 ms of that was Direct2D work it defers, which now
    /// lands on the text: ~3% overall. The point is the order it sets up: every background before any text, which
    /// drawing text in one batch (a glyph atlas) needs. The default background is transparent here: the Clear
    /// (with background-opacity) shows.
    /// </summary>
    private void DrawBackgrounds(CanvasDrawingSession ds, int rows, int cols)
    {
        if (rows == 0 || cols == 0)
        {
            return;
        }

        var size = rows * cols * 4;
        if (_bgPixels.Length != size) // exactly the bitmap's size: no copy per frame
        {
            _bgPixels = new byte[size];
        }

        for (var y = 0; y < rows; y++)
        {
            var cells = _frameCells[y];
            var count = _frameCounts[y];
            var i = y * cols * 4;
            for (var x = 0; x < cols; x++, i += 4)
            {
                if (x < count && cells[x].HasBg)
                {
                    var c = cells[x].Bg;
                    _bgPixels[i] = (byte)(c.B * c.A / 255);
                    _bgPixels[i + 1] = (byte)(c.G * c.A / 255);
                    _bgPixels[i + 2] = (byte)(c.R * c.A / 255);
                    _bgPixels[i + 3] = c.A;
                }
                else
                {
                    _bgPixels[i] = _bgPixels[i + 1] = _bgPixels[i + 2] = _bgPixels[i + 3] = 0;
                }
            }
        }

        if (_bgBitmap is not { } bitmap || bitmap.SizeInPixels.Width != cols || bitmap.SizeInPixels.Height != rows)
        {
            _bgBitmap?.Dispose();
            bitmap = _bgBitmap = CanvasBitmap.CreateFromBytes(ds, _bgPixels, cols, rows,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        }
        else
        {
            bitmap.SetPixelBytes(_bgPixels);
        }

        var m = Metrics;
        ds.DrawImage(bitmap, new Rect(PadLeft, PadTop, cols * m.Width, rows * m.Height), new Rect(0, 0, cols, rows), 1,
            CanvasImageInterpolation.NearestNeighbor);
    }

    private void DrawText(CanvasDrawingSession ds, int count, float top, int row)
    {
        // Batch cells into glyph runs with a fixed advance of one cell, so each
        // glyph lands exactly on the grid. Cells the primary font can't render
        // (emoji, CJK, clusters) fall back to DirectWrite text layout.
        var cellW = Metrics.Width;
        var runStart = -1;
        var runLen = 0;
        var runVariant = 0;
        Color runColor = default;

        for (var x = 0; x < count; x++)
        {
            ref var cell = ref _cells[x];
            var blank = cell.CpLen == 0 || (cell.Flags & (CellFlags.Invisible | CellFlags.SpacerTail | CellFlags.Placeholder)) != 0 ||
                        (cell.CpLen == 1 && _codepoints[cell.CpStart] == ' ');

            if (blank)
            {
                if (runLen > 0)
                {
                    // Keep the run going across blank cells with a space glyph.
                    _glyphs[runLen++] = new CanvasGlyph { Index = GlyphIndex(' ', runVariant), Advance = cellW };
                }

                continue;
            }

            if (IsSprite(ref cell))
            {
                // Ghostty draws it (after the row's text, in one batch).
                FlushRun(ds, runStart, runLen, runVariant, runColor, top);
                runLen = 0;
                _rowSprites.Add((x, _codepoints[cell.CpStart], cell.Fg));
                continue;
            }

            var variant = cell.FontVariant;
            var glyph = cell.CpLen == 1 && (cell.Flags & CellFlags.Wide) == 0
                ? GlyphIndex(_codepoints[cell.CpStart], variant)
                : 0;

            if (glyph == 0)
            {
                FlushRun(ds, runStart, runLen, runVariant, runColor, top);
                runLen = 0;
                if (!_batching || !AddFallback(ds, ref cell, x, row))
                {
                    DrawFallback(ds, ref cell, x, top);
                }

                continue;
            }

            if (_batching)
            {
                AddGlyph(ds, glyph, variant, cell.Fg, x, row);
                continue;
            }

            if (runLen > 0 && (variant != runVariant || cell.Fg != runColor))
            {
                FlushRun(ds, runStart, runLen, runVariant, runColor, top);
                runLen = 0;
            }

            if (runLen == 0)
            {
                runStart = x;
                runVariant = variant;
                runColor = cell.Fg;
            }

            _glyphs[runLen++] = new CanvasGlyph { Index = glyph, Advance = cellW };
        }

        FlushRun(ds, runStart, runLen, runVariant, runColor, top);
        DrawRowSprites(ds, top);
    }

    /// <summary>Whether Ghostty's sprite rasterizer draws this cell (one narrow codepoint it covers, builtin-glyphs on).</summary>
    private bool IsSprite(ref Cell cell) =>
        _spriteBatchSupported && _config.BuiltinGlyphs && cell.CpLen == 1 && (cell.Flags & CellFlags.Wide) == 0 &&
        Sprites.Has(_codepoints[cell.CpStart]);

    /// <summary>The sprite for <paramref name="codepoint"/> at the current cell size, made on first use.</summary>
    private (CanvasBitmap Bitmap, int X, int Y)? Sprite(ICanvasResourceCreator device, uint codepoint)
    {
        if (_sprites.TryGetValue(codepoint, out var cached))
        {
            return cached;
        }

        var m = Metrics;
        var ascent = (int)MathF.Round(m.Baseline * m.Scale);
        // Lines as thick as the font's underline in device pixels, rounded up: Ghostty's rule
        // (Metrics.zig: box_thickness = max(1, ceil(underline thickness))).
        var underline = _faces[0] is { UnderlineThickness: > 0 } face ? face.UnderlineThickness * _fontSize : _fontSize / 14;
        var thickness = _config.AdjustedBoxThickness((int)MathF.Max(1, MathF.Ceiling(underline * m.Scale)));
        (CanvasBitmap, int, int)? sprite = null;
        if (Sprites.Draw(codepoint, (int)m.WidthPx, ascent, (int)m.HeightPx - ascent, thickness) is { } image)
        {
            // White, premultiplied: the batch's tint makes it the cell's color.
            var pixels = new byte[image.Width * image.Height * 4];
            for (var i = 0; i < image.Coverage.Length; i++)
            {
                var a = image.Coverage[i];
                pixels[4 * i] = pixels[4 * i + 1] = pixels[4 * i + 2] = pixels[4 * i + 3] = a;
            }

            var bitmap = CanvasBitmap.CreateFromBytes(device, pixels, image.Width, image.Height,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 96 * m.Scale, CanvasAlphaMode.Premultiplied);
            sprite = (bitmap, image.X, image.Y);
        }

        _sprites[codepoint] = sprite;
        return sprite;
    }

    /// <summary>This row's sprite cells, each at its cell (and past it where the shape reaches), in its color.</summary>
    private void DrawRowSprites(CanvasDrawingSession ds, float top)
    {
        if (_rowSprites.Count == 0)
        {
            return;
        }

        var m = Metrics;
        if (_batching)
        {
            // Into the frame's batch with the glyphs.
            foreach (var (column, codepoint, color) in _rowSprites)
            {
                if (Sprite(ds, codepoint) is { } sprite)
                {
                    var size = sprite.Bitmap.SizeInPixels;
                    _frameSprites.Add((sprite.Bitmap, new Rect(PadLeft + column * m.Width + sprite.X / m.Scale, top + sprite.Y / m.Scale,
                        size.Width / m.Scale, size.Height / m.Scale), null, new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f)));
                }
            }

            _rowSprites.Clear();
            return;
        }

        using (var batch = ds.CreateSpriteBatch(CanvasSpriteSortMode.None, CanvasImageInterpolation.NearestNeighbor))
        {
            foreach (var (column, codepoint, color) in _rowSprites)
            {
                if (Sprite(ds, codepoint) is not { } sprite)
                {
                    continue;
                }

                var size = sprite.Bitmap.SizeInPixels;
                var rect = new Rect(PadLeft + column * m.Width + sprite.X / m.Scale, top + sprite.Y / m.Scale,
                    size.Width / m.Scale, size.Height / m.Scale);
                batch.Draw(sprite.Bitmap, rect, new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
            }
        }

        _rowSprites.Clear();
    }

    /// <summary>A glyph's atlas tile into the frame's batch, at its cell, tinted with its color.</summary>
    private void AddGlyph(CanvasDrawingSession ds, int glyph, int variant, Color color, int column, int row)
    {
        if (_faces[variant] is not { } face)
        {
            return;
        }

        // Direct2D's grayscale contrast depends on the text's lightness: dark text has its own tiles.
        var dark = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B < 128;
        var tile = _atlas.Get(ds, face, _fontSize, glyph, variant, dark);

        var m = Metrics;
        _frameSprites.Add((tile.Page, _atlas.Destination(_gridX + column * (int)m.WidthPx, _gridY + row * (int)m.HeightPx), tile.Source,
            new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f)));
    }

    /// <summary>
    /// A character the font lacks, from the atlas like a glyph (CJK, symbols; emoji in color). False: drawn the old way
    /// (a cell-filling shape from the bundled font, or no format yet).
    /// </summary>
    private bool AddFallback(CanvasDrawingSession ds, ref Cell cell, int column, int row)
    {
        var wide = (cell.Flags & CellFlags.Wide) != 0;
        var single = cell.CpLen == 1;
        var codepoint = (int)_codepoints[cell.CpStart];
        if ((single && !wide && _bundledFaces[cell.FontVariant] != null && FillsCell((uint)codepoint)) ||
            _formats[cell.FontVariant] is not { } format)
        {
            return false;
        }

        var cluster = single ? null : CellText(ref cell);
        var color = cell.Fg;
        GlyphAtlas.Tile tile;
        Vector4 tint;
        if (IsEmoji(codepoint, cluster, wide))
        {
            tile = _atlas.GetColor(ds, codepoint, cluster, format, cell.FontVariant, wide, color);
            tint = Vector4.One;
        }
        else
        {
            var dark = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B < 128;
            tile = _atlas.GetText(ds, codepoint, cluster, format, cell.FontVariant, wide, dark);
            tint = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }

        var m = Metrics;
        _frameSprites.Add((tile.Page, _atlas.Destination(_gridX + column * (int)m.WidthPx, _gridY + row * (int)m.HeightPx), tile.Source, tint));
        return true;
    }

    /// <summary>
    /// Whether a fallback character shows in color: emoji presentation (VS16, a ZWJ sequence, a keycap) or a wide
    /// pictograph. Narrow symbols like U+2714 stay text, and CJK is never color (as Windows Terminal draws them).
    /// </summary>
    private static bool IsEmoji(int codepoint, string? cluster, bool wide)
    {
        if (cluster != null && (cluster.Contains('\uFE0F') || cluster.Contains('\u200D') || cluster.Contains('\u20E3')))
        {
            return true;
        }

        return wide && (codepoint >= 0x1F000 || codepoint is >= 0x2300 and <= 0x2BFF or 0x3030 or 0x303D or 0x3297 or 0x3299);
    }

    /// <summary>The frame's glyphs and box-drawing sprites, in one sprite batch.</summary>
    private void DrawFrameSprites(CanvasDrawingSession ds)
    {
        _atlas.Flush(ds);
        if (_frameSprites.Count == 0)
        {
            return;
        }

        using (var batch = ds.CreateSpriteBatch(CanvasSpriteSortMode.None, CanvasImageInterpolation.NearestNeighbor))
        {
            foreach (var (bitmap, destination, source, tint) in _frameSprites)
            {
                if (source is { } from)
                {
                    batch.DrawFromSpriteSheet(bitmap, destination, from, tint);
                }
                else
                {
                    batch.Draw(bitmap, destination, tint);
                }
            }
        }

        _frameSprites.Clear();
    }

    private void FlushRun(CanvasDrawingSession ds, int start, int len, int variant, Color color, float top)
    {
        // Trailing blanks carry no ink.
        while (len > 0 && _glyphs[len - 1].Index == GlyphIndex(' ', variant))
        {
            len--;
        }

        if (len <= 0 || _faces[variant] is not { } face)
        {
            return;
        }

        _brush!.Color = color;
        ds.DrawGlyphRun(
            new Vector2(PadLeft + start * Metrics.Width, top + Metrics.Baseline),
            face, _fontSize, _glyphs.AsSpan(0, len).ToArray(), false, 0, _brush);
    }

    private void DrawFallback(CanvasDrawingSession ds, ref Cell cell, int x, float top)
    {
        if (DrawBundled(ds, ref cell, x, top))
        {
            return;
        }

        var m = Metrics;
        var text = CellText(ref cell);
        var width = (cell.Flags & CellFlags.Wide) != 0 ? 2 * m.Width : m.Width;
        var format = _formats[cell.FontVariant]!;
        format.HorizontalAlignment = CanvasHorizontalAlignment.Center;
        using var layout = new CanvasTextLayout(ds, text, format, width, m.Height);
        format.HorizontalAlignment = CanvasHorizontalAlignment.Left;
        // Color glyphs only for emoji presentation. Default-emoji characters are
        // East Asian Wide (two cells); narrow symbols like U+2714 are text unless
        // followed by VS16 (U+FE0F), matching how Windows Terminal renders them.
        var emoji = (cell.Flags & CellFlags.Wide) != 0 || text.Contains('️');
        layout.Options = emoji ? CanvasDrawTextOptions.EnableColorFont : CanvasDrawTextOptions.Default;

        // Align the fallback font's baseline with the grid's baseline.
        var lines = layout.LineMetrics;
        var offset = lines.Length > 0 ? m.Baseline - lines[0].Baseline : 0;
        ds.DrawTextLayout(layout, PadLeft + x * m.Width, top + offset, cell.Fg);
    }

    /// <summary>
    /// A cell-filling shape (box drawing, blocks, Symbols for Legacy Computing) the configured
    /// font lacks, from the bundled Cascadia Mono NF, stretched from Cascadia's cell to ours so
    /// it meets its neighbours. Only those: other characters keep Windows' fallback (as in
    /// Windows Terminal). False if it isn't one, or Cascadia lacks it too.
    /// </summary>
    private bool DrawBundled(CanvasDrawingSession ds, ref Cell cell, int x, float top)
    {
        if (cell.CpLen != 1 || (cell.Flags & CellFlags.Wide) != 0 || _bundledFaces[cell.FontVariant] is not { } face ||
            !FillsCell(_codepoints[cell.CpStart]))
        {
            return false;
        }

        var codepoint = _codepoints[cell.CpStart];
        if (!_bundledGlyphCache.TryGetValue((codepoint, cell.FontVariant), out var glyph))
        {
            glyph = face.GetGlyphIndices([codepoint])[0];
            _bundledGlyphCache[(codepoint, cell.FontVariant)] = glyph;
        }

        if (glyph == 0 || _bundledAdvance <= 0 || _bundledLineHeight <= 0)
        {
            return false;
        }

        var m = Metrics;
        var left = PadLeft + x * m.Width;
        _brush!.Color = cell.Fg;
        var glyphs = new[] { new CanvasGlyph { Index = glyph, Advance = _bundledAdvance } };
        // Half a pixel past the cell on every side: the anti-aliased edges of neighbouring
        // shapes overlap instead of leaving a hairline between them.
        var bleed = 0.5f / m.Scale;
        var saved = ds.Transform;
        ds.Transform = Matrix3x2.CreateScale((m.Width + 2 * bleed) / _bundledAdvance, (m.Height + 2 * bleed) / _bundledLineHeight) *
                       Matrix3x2.CreateTranslation(left - bleed, top - bleed) * saved;
        ds.DrawGlyphRun(new Vector2(0, _bundledBaseline), face, _fontSize, glyphs, false, 0, _brush);
        ds.Transform = saved;
        return true;
    }

    /// <summary>Box drawing, block elements and Symbols for Legacy Computing: shapes drawn edge to edge of the cell.</summary>
    private static bool FillsCell(uint codepoint) =>
        codepoint is >= 0x2500 and <= 0x259F or >= 0x1FB00 and <= 0x1FBFF;

    private void DrawDecorations(CanvasDrawingSession ds, int count, float top)
    {
        var m = Metrics;
        var thickness = MathF.Max(1, MathF.Round(_fontSize / 14 * m.Scale)) / m.Scale;
        for (var x = 0; x < count; x++)
        {
            ref var cell = ref _cells[x];
            if ((cell.Flags & (CellFlags.Invisible | CellFlags.Placeholder)) != 0)
            {
                continue;
            }

            var left = PadLeft + x * m.Width;
            var width = (cell.Flags & CellFlags.Wide) != 0 ? 2 * m.Width : m.Width;
            if (cell.Underline != 0)
            {
                var uy = top + m.Baseline + 2 * thickness;
                ds.FillRectangle(left, uy, width, thickness, cell.Fg);
                if (cell.Underline == 2)
                {
                    // Double underline.
                    ds.FillRectangle(left, uy + 2 * thickness, width, thickness, cell.Fg);
                }
            }

            if ((cell.Flags & CellFlags.Strikethrough) != 0)
            {
                ds.FillRectangle(left, top + m.Height * 0.55f, width, thickness, cell.Fg);
            }

            if ((cell.Flags & CellFlags.Overline) != 0)
            {
                ds.FillRectangle(left, top, width, thickness, cell.Fg);
            }
        }
    }

    // ---- kitty graphics ----

    private void UpdateImageCache(ICanvasResourceCreator device)
    {
        foreach (var p in _images)
        {
            if (p.Pixels is { } pixels)
            {
                if (_bitmaps.Remove(p.ImageId, out var stale))
                {
                    stale.Bitmap.Dispose();
                }

                // 96 DPI: one bitmap DIP per image pixel, so source rects are in pixels.
                var bitmap = CanvasBitmap.CreateFromBytes(device, pixels, p.ImageWidth, p.ImageHeight,
                    DirectXPixelFormat.R8G8B8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
                _bitmaps[p.ImageId] = (p.Generation, bitmap);
            }
        }

        // Drop images no longer placed on screen (deleted, or scrolled out of view).
        if (_bitmaps.Count > 0)
        {
            foreach (var id in _bitmaps.Keys.Where(id => !_images.Exists(p => p.ImageId == id)).ToList())
            {
                _bitmaps.Remove(id, out var gone);
                gone.Bitmap.Dispose();
            }
        }

        _images.Sort((a, b) => a.Z.CompareTo(b.Z));
    }

    /// <summary>Highlights find results on viewport row <paramref name="y"/>, under the text.</summary>
    private void DrawMatches(CanvasDrawingSession ds, int y, int count, float top)
    {
        if (SearchMatches is not { Count: > 0 } matches)
        {
            return;
        }

        // Matches are sorted and don't overlap, so their end rows are sorted too: find the first
        // ending on or after this row, then walk those that start on or before it.
        var row = _viewportTop + (ulong)y;
        int lo = 0, hi = matches.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (matches[mid].EndRow < row) lo = mid + 1;
            else hi = mid;
        }

        var m = Metrics;
        for (var i = lo; i < matches.Count && matches[i].Row <= row; i++)
        {
            // A match spanning a soft wrap covers the rest of its first row, whole middle rows,
            // and the start of its last row.
            var match = matches[i];
            var start = match.Row == row ? match.StartCol : 0;
            var end = match.EndRow == row ? match.EndCol : count - 1;
            var rect = new Rect(PadLeft + start * m.Width, top, (end - start + 1) * m.Width, m.Height);
            ds.FillRectangle(rect, MatchColorFor(Config, i == CurrentMatch));
        }
    }

    /// <summary>Draws the image slices this row's placeholder cells stand for.</summary>
    private void DrawPlaceholders(CanvasDrawingSession ds, int count, float top)
    {
        var m = Metrics;
        _runs.Clear();
        KittyPlaceholders.Runs(_placeholders.AsSpan(0, count), _runs);
        foreach (var run in _runs)
        {
            // The run's virtual placement: the one it names, else the image's first.
            var placement = _images.Find(p => p.Virtual && p.ImageId == run.ImageId &&
                                              (run.PlacementId == 0 || p.PlacementId == run.PlacementId));
            if (!placement.Virtual || !_bitmaps.TryGetValue(run.ImageId, out var entry) ||
                KittyPlaceholders.Geometry(run, placement.ImageWidth, placement.ImageHeight, placement.Columns,
                    placement.Rows, m.WidthPx, m.HeightPx) is not { } slice)
            {
                continue;
            }

            var dest = new Rect(PadLeft + run.X * m.Width + slice.OffsetX / m.Scale, top + slice.OffsetY / m.Scale,
                slice.DestWidth / m.Scale, slice.DestHeight / m.Scale);
            ds.DrawImage(entry.Bitmap, dest, new Rect(slice.SourceX, slice.SourceY, slice.SourceWidth, slice.SourceHeight));
        }
    }

    /// <summary>
    /// Draws placements below the text (negative z) or above it, clipped to the grid rows.
    /// Below-text images go under all rows (so under cell backgrounds that set a color too).
    /// </summary>
    private void DrawImages(CanvasDrawingSession ds, int rows, bool belowText)
    {
        if (_images.Count == 0)
        {
            return;
        }

        var m = Metrics;
        using var clip = ds.CreateLayer(1f, new Rect(0, PadTop, 100_000, rows * m.Height));
        foreach (var p in _images)
        {
            if (p.Virtual || (p.Z < 0) != belowText || !_bitmaps.TryGetValue(p.ImageId, out var entry))
            {
                continue;
            }

            var dest = new Rect(
                PadLeft + p.Col * m.Width + p.OffsetX / m.Scale,
                PadTop + p.Row * m.Height + p.OffsetY / m.Scale,
                p.PixelWidth / m.Scale,
                p.PixelHeight / m.Scale);
            ds.DrawImage(entry.Bitmap, dest, new Rect(p.SourceX, p.SourceY, p.SourceWidth, p.SourceHeight));
        }
    }

    private void DrawLinkUnderline(CanvasDrawingSession ds, ushort first, ushort last, int count, float top)
    {
        var m = Metrics;
        var thickness = MathF.Max(1, MathF.Round(_fontSize / 14 * m.Scale)) / m.Scale;
        for (var x = first; x <= last && x < count; x++)
        {
            ds.FillRectangle(PadLeft + x * m.Width, top + m.Baseline + 2 * thickness, m.Width, thickness, _cells[x].Fg);
        }
    }

    private void DrawCursor(CanvasDrawingSession ds, GhosttyRenderStateCursor cursor, int count, float top,
        Color color, Color defaultBg, CursorPresentation presentation)
    {
        var m = Metrics;
        var x = (int)cursor.ViewportX;
        var left = PadLeft + x * m.Width;
        var wide = x < count && (_cells[x].Flags & CellFlags.Wide) != 0;
        var width = wide ? 2 * m.Width : m.Width;
        var thin = MathF.Max(1, MathF.Round(m.Scale)) / m.Scale;

        var style = presentation.Focused ? cursor.VisualStyle : CursorVisualStyle.BlockHollow;
        if (presentation.Focused && cursor.Blinking != 0 && !presentation.BlinkOn)
        {
            return;
        }

        if (ForShaders is { } shaders)
        {
            shaders.CursorDrawn = true;
            shaders.CursorStyle = style;
            shaders.CursorDrawnColor = color;
            shaders.Cursor = style switch
            {
                CursorVisualStyle.Bar => new(left, top, 2 * thin, m.Height),
                CursorVisualStyle.Underline => new(left, top + m.Height - 2 * thin, width, 2 * thin),
                _ => new(left, top, width, m.Height),
            };
        }

        switch (style)
        {
            case CursorVisualStyle.Bar:
                ds.FillRectangle(left, top, 2 * thin, m.Height, color);
                break;
            case CursorVisualStyle.Underline:
                ds.FillRectangle(left, top + m.Height - 2 * thin, width, 2 * thin, color);
                break;
            case CursorVisualStyle.BlockHollow:
                ds.DrawRectangle(left + thin / 2, top + thin / 2, width - thin, m.Height - thin, color, thin);
                break;
            default:
                ds.FillRectangle(left, top, width, m.Height, color);
                if (x < count && _cells[x].CpLen > 0 && color.A == 255) // see-through: the character shows as it is
                {
                    // Redraw the covered glyph in the cursor-text color, else the cell's background.
                    ref var cell = ref _cells[x];
                    var fg = cell.Fg;
                    cell.Fg = Config.Theme.CursorText is { } text ? ToColor(text) : cell.HasBg ? cell.Bg : defaultBg;
                    var glyph = cell.CpLen == 1 && !wide ? GlyphIndex(_codepoints[cell.CpStart], cell.FontVariant) : 0;
                    if (IsSprite(ref cell))
                    {
                        _rowSprites.Add((x, _codepoints[cell.CpStart], cell.Fg));
                        DrawRowSprites(ds, top);
                    }
                    else if (glyph != 0 && _faces[cell.FontVariant] is { } face)
                    {
                        _brush!.Color = cell.Fg;
                        ds.DrawGlyphRun(new Vector2(left, top + m.Baseline), face, _fontSize,
                            [new CanvasGlyph { Index = glyph, Advance = m.Width }], false, 0, _brush);
                    }
                    else
                    {
                        DrawFallback(ds, ref cell, x, top);
                    }

                    cell.Fg = fg;
                }

                break;
        }
    }

    private int GlyphIndex(uint codepoint, int variant)
    {
        if (codepoint < 0x800)
        {
            var fast = _glyphFast[variant] ??= new int[0x800];
            if (fast[codepoint] == 0)
            {
                fast[codepoint] = (_faces[variant] is { } f ? f.GetGlyphIndices([codepoint])[0] : 0) + 1;
            }

            return fast[codepoint] - 1;
        }

        if (_glyphCache.TryGetValue((codepoint, variant), out var index))
        {
            return index;
        }

        index = _faces[variant] is { } face ? face.GetGlyphIndices([codepoint])[0] : 0;
        _glyphCache[(codepoint, variant)] = index;
        return index;
    }

    private string CellText(ref Cell cell)
    {
        Span<char> chars = stackalloc char[cell.CpLen * 2];
        var n = 0;
        for (var i = 0; i < cell.CpLen; i++)
        {
            var rune = new System.Text.Rune(_codepoints[cell.CpStart + i]);
            n += rune.EncodeToUtf16(chars[n..]);
        }

        return new string(chars[..n]);
    }

    public static Color ToColor(GhosttyColorRgb c) => Color.FromArgb(255, c.R, c.G, c.B);

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static void Check(GhosttyResult result)
    {
        if (result != GhosttyResult.Success)
        {
            throw new InvalidOperationException($"libghostty-vt call failed: {result}");
        }
    }
}
