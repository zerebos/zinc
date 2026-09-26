using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Phantom.Ghostty;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using Windows.UI.Text;
using Colors = Microsoft.UI.Colors;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace Phantom.Terminal;

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
    private readonly Config _config;
    private readonly nint _renderState, _rowIterator, _rowCells;
    private readonly CanvasTextFormat?[] _formats = new CanvasTextFormat?[4];
    private readonly CanvasFontFace?[] _faces = new CanvasFontFace?[4];
    private readonly Dictionary<(uint, int), int> _glyphCache = new();
    private CanvasSolidColorBrush? _brush;
    private string _fontFamily;
    private float _fontSize;

    // Per-row scratch buffers reused across frames.
    private Cell[] _cells = [];
    private KittyPlaceholders.Cell?[] _placeholders = [];
    private bool _rowHasPlaceholders;
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

    public bool HasResources => _brush != null && Metrics.Width > 0;

    private float PadLeft => _config.PaddingLeft;
    private float PadTop => _config.PaddingTop;

    public void CreateResources(ICanvasResourceCreator device)
    {
        _brush = new CanvasSolidColorBrush(device, Colors.White);
        // Bitmaps belong to the old device.
        foreach (var entry in _bitmaps.Values)
        {
            entry.Bitmap.Dispose();
        }

        _bitmaps.Clear();
    }

    /// <summary>Loads the font at <paramref name="sizeDip"/> and recomputes cell metrics.</summary>
    public void SetFont(ICanvasResourceCreator device, float sizeDip, float scale)
    {
        _fontSize = sizeDip;
        var fonts = CanvasFontSet.GetSystemFontSet();
        // Fall back if the configured family isn't installed (DirectWrite would
        // otherwise substitute a proportional font and break the grid).
        foreach (var candidate in new[] { _config.FontFamily, "Cascadia Mono", "Consolas" })
        {
            if (fonts.GetMatchingFonts(candidate, FontWeights.Normal, FontStretch.Normal, FontStyle.Normal).Fonts.Count > 0)
            {
                _fontFamily = candidate;
                break;
            }
        }

        for (var v = 0; v < 4; v++)
        {
            var weight = (v & 1) != 0 ? FontWeights.Bold : FontWeights.Normal;
            var style = (v & 2) != 0 ? FontStyle.Italic : FontStyle.Normal;
            _formats[v] = new CanvasTextFormat
            {
                FontFamily = _fontFamily,
                FontSize = _fontSize,
                FontWeight = weight,
                FontStyle = style,
                WordWrapping = CanvasWordWrapping.NoWrap,
                Options = CanvasDrawTextOptions.EnableColorFont,
            };

            var matches = fonts.GetMatchingFonts(_fontFamily, weight, FontStretch.Normal, style).Fonts;
            _faces[v] = matches.Count > 0 ? matches[0] : _faces[0];
        }

        _glyphCache.Clear();

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
            if (Native.RenderStateUpdate(_renderState, session.Handle) != GhosttyResult.Success)
            {
                return false;
            }

            // Under the same lock, so images and text come from the same terminal state.
            session.CollectImages(_images, _needsPixels);
            GhosttyTerminalScrollbar scrollbar;
            Native.TerminalGet(session.Handle, TerminalData.Scrollbar, &scrollbar);
            _viewportTop = scrollbar.Offset;
        }

        UpdateImageCache(ds);

        // The render state is a snapshot, so the rest runs without the lock.
        var colors = new GhosttyRenderStateColors { Size = (nuint)sizeof(GhosttyRenderStateColors) };
        Native.RenderStateGet(_renderState, RenderStateData.Colors, &colors);
        var cursor = new GhosttyRenderStateCursor { Size = (nuint)sizeof(GhosttyRenderStateCursor) };
        Native.RenderStateGet(_renderState, RenderStateData.Cursor, &cursor);

        var defaultFg = ToColor(colors.Foreground);
        var defaultBg = ToColor(colors.Background);
        var cursorColor = colors.CursorHasValue != 0 ? ToColor(colors.Cursor) : defaultFg;
        ds.Clear(defaultBg);
        DrawImages(ds, rows, belowText: true);

        var iterator = _rowIterator;
        Native.RenderStateGet(_renderState, RenderStateData.RowIterator, &iterator);

        var m = Metrics;
        var y = 0;
        while (Native.RowIteratorNext(_rowIterator) != 0 && y < rows)
        {
            var count = ReadRow(defaultFg, defaultBg);
            var top = PadTop + y * m.Height;
            DrawBackgrounds(ds, count, top);
            DrawMatches(ds, y, count, top);
            if (_rowHasPlaceholders)
            {
                DrawPlaceholders(ds, count, top);
            }

            DrawText(ds, count, top);
            DrawDecorations(ds, count, top);
            if (HoverLink is { } link && link.Row == y)
            {
                DrawLinkUnderline(ds, link, count, top);
            }

            if (cursor.Visible != 0 && cursor.ViewportHasValue != 0 && cursor.ViewportY == y)
            {
                DrawCursor(ds, cursor, count, top, cursorColor, defaultBg, presentation);
            }

            y++;
        }

        DrawImages(ds, rows, belowText: false);
        Native.RenderStateClean(_renderState);
        return cursor.Blinking != 0 && cursor.Visible != 0;
    }

    public void Dispose()
    {
        foreach (var entry in _bitmaps.Values)
        {
            entry.Bitmap.Dispose();
        }

        _bitmaps.Clear();
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

        var x = 0;
        var cp = 0;
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

            // Selection renders as inverse video, so it works with any theme.
            if (hasSelection && x >= sel.StartX && x <= sel.EndX)
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

            if ((cell.Flags & CellFlags.Faint) != 0)
            {
                cell.Fg = Blend(cell.Fg, cell.HasBg ? cell.Bg : defaultBg, 0.5f);
            }

            x++;
        }

        return x;
    }

    private void DrawBackgrounds(CanvasDrawingSession ds, int count, float top)
    {
        var m = Metrics;
        ds.Antialiasing = CanvasAntialiasing.Aliased;
        var x = 0;
        while (x < count)
        {
            if (!_cells[x].HasBg)
            {
                x++;
                continue;
            }

            var start = x;
            var color = _cells[x].Bg;
            while (x < count && _cells[x].HasBg && _cells[x].Bg == color)
            {
                x++;
            }

            ds.FillRectangle(PadLeft + start * m.Width, top, (x - start) * m.Width, m.Height, color);
        }

        ds.Antialiasing = CanvasAntialiasing.Antialiased;
    }

    private void DrawText(CanvasDrawingSession ds, int count, float top)
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

            var variant = cell.FontVariant;
            var glyph = cell.CpLen == 1 && (cell.Flags & CellFlags.Wide) == 0
                ? GlyphIndex(_codepoints[cell.CpStart], variant)
                : 0;

            if (glyph == 0)
            {
                FlushRun(ds, runStart, runLen, runVariant, runColor, top);
                runLen = 0;
                DrawFallback(ds, ref cell, x, top);
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
            ds.FillRectangle(rect, i == CurrentMatch ? CurrentMatchColor : MatchColor);
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

    private void DrawLinkUnderline(CanvasDrawingSession ds, LinkMatch link, int count, float top)
    {
        var m = Metrics;
        var thickness = MathF.Max(1, MathF.Round(_fontSize / 14 * m.Scale)) / m.Scale;
        for (var x = link.StartCol; x <= link.EndCol && x < count; x++)
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
                if (x < count && _cells[x].CpLen > 0)
                {
                    // Redraw the covered glyph in the background color.
                    ref var cell = ref _cells[x];
                    var fg = cell.Fg;
                    cell.Fg = cell.HasBg ? cell.Bg : defaultBg;
                    var glyph = cell.CpLen == 1 && !wide ? GlyphIndex(_codepoints[cell.CpStart], cell.FontVariant) : 0;
                    if (glyph != 0 && _faces[cell.FontVariant] is { } face)
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
