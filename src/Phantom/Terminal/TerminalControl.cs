using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Phantom.Ghostty;
using Phantom.Input;
using Windows.System;
using Windows.UI;
using Windows.UI.Text;
using Colors = Microsoft.UI.Colors;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace Phantom.Terminal;

/// <summary>
/// A terminal view: renders a libghostty-vt render state with Win2D and turns
/// keyboard/mouse input into bytes for the child process.
/// </summary>
public sealed unsafe partial class TerminalControl : UserControl
{
    private const float PaddingDip = 8;
    private const float MinFontSize = 6, MaxFontSize = 72;

    private readonly CanvasControl _canvas;
    private readonly DispatcherTimer _blinkTimer;
    private readonly TerminalTheme _theme = TerminalTheme.Default;
    private readonly string _commandLine;
    private readonly string? _workingDirectory;

    private TerminalSession? _session;
    private nint _renderState, _rowIterator, _rowCells;
    private nint _gesture, _gesturePress, _gestureDrag, _gestureRelease;

    // Font and cell metrics. DIP values are derived from whole device pixels
    // so cell edges land on pixel boundaries.
    private readonly Config _config;
    private string _fontFamily;
    private float _fontSize;
    private readonly CanvasTextFormat?[] _formats = new CanvasTextFormat?[4];
    private readonly CanvasFontFace?[] _faces = new CanvasFontFace?[4];
    private readonly Dictionary<(uint, int), int> _glyphCache = new();
    private float _cellW, _cellH, _baseline;
    private uint _cellWPx, _cellHPx;
    private float _scale = 1;
    private ushort _cols, _rows;

    // Per-row scratch buffers reused across frames.
    private Cell[] _cells = [];
    private uint[] _codepoints = new uint[1024];
    private CanvasGlyph[] _glyphs = [];
    private CanvasSolidColorBrush? _brush;

    private int _invalidatePending;
    private bool _blinkOn = true;
    private bool _focused;
    private bool _charHandledByKeyDown;
    private char _pendingHighSurrogate;
    private bool _selecting;
    private MouseButton? _reportedButton;

    private bool _exited;

    public event Action<string>? TitleChanged;

    /// <summary>The terminal is done: the child exited cleanly, or the user dismissed a failure.</summary>
    public event Action? CloseRequested;

    public TerminalControl(Config config, string commandLine, string? workingDirectory)
    {
        _config = config;
        _fontFamily = config.FontFamily;
        _fontSize = Math.Clamp(config.FontSize, MinFontSize, MaxFontSize);
        _commandLine = commandLine;
        _workingDirectory = workingDirectory;

        IsTabStop = true;
        UseSystemFocusVisuals = false;
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);

        _canvas = new CanvasControl { ClearColor = ToColor(_theme.Background) };
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        _canvas.SizeChanged += (_, _) => UpdateGrid();
        Content = _canvas;

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            _canvas.Invalidate();
        };

        Loaded += (_, _) =>
        {
            XamlRoot.Changed += (_, _) => UpdateScale();
            Focus(FocusState.Programmatic);
        };
        Unloaded += (_, _) => Close();
    }

    // ---------------------------------------------------------------- setup

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _brush = new CanvasSolidColorBrush(sender, Colors.White);
        _scale = (float)(XamlRoot?.RasterizationScale ?? 1.0);
        UpdateFont();
        UpdateGrid();
    }

    private void UpdateScale()
    {
        var scale = (float)(XamlRoot?.RasterizationScale ?? 1.0);
        if (Math.Abs(scale - _scale) > 0.001f)
        {
            _scale = scale;
            UpdateFont();
            UpdateGrid();
        }
    }

    private void UpdateFont()
    {
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
        using var layout = new CanvasTextLayout(_canvas, "MMMMMMMMMM", _formats[0], float.MaxValue, float.MaxValue);
        var bounds = layout.LayoutBounds;
        _cellWPx = (uint)Math.Max(1, Math.Round(bounds.Width / 10 * _scale));
        _cellHPx = (uint)Math.Max(1, Math.Ceiling(bounds.Height * _scale));
        _cellW = _cellWPx / _scale;
        _cellH = _cellHPx / _scale;
        _baseline = MathF.Round(layout.LineMetrics[0].Baseline * _scale) / _scale;
    }

    private void UpdateGrid()
    {
        if (_cellW <= 0 || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0)
        {
            return;
        }

        var cols = (ushort)Math.Clamp((int)((_canvas.ActualWidth - 2 * PaddingDip) / _cellW), 2, 1000);
        var rows = (ushort)Math.Clamp((int)((_canvas.ActualHeight - 2 * PaddingDip) / _cellH), 1, 1000);

        if (_session == null)
        {
            StartSession(cols, rows);
        }
        else
        {
            // Resize even when the grid is unchanged: the cell pixel size
            // (reported to apps via XTWINOPS/mouse pixels) may have changed.
            _session.Resize(cols, rows, _cellWPx, _cellHPx);
        }

        _cols = cols;
        _rows = rows;
        if (_cells.Length < cols)
        {
            _cells = new Cell[cols];
            _glyphs = new CanvasGlyph[cols];
        }

        _canvas.Invalidate();
    }

    private void StartSession(ushort cols, ushort rows)
    {
        var session = new TerminalSession(cols, rows, _theme);
        session.Resize(cols, rows, _cellWPx, _cellHPx);

        Check(Native.RenderStateNew(0, out _renderState));
        Check(Native.RowIteratorNew(0, out _rowIterator));
        Check(Native.RowCellsNew(0, out _rowCells));
        Check(Native.SelectionGestureNew(0, out _gesture));
        Check(Native.SelectionGestureEventNew(0, out _gesturePress, SelectionGestureEventType.Press));
        Check(Native.SelectionGestureEventNew(0, out _gestureDrag, SelectionGestureEventType.Drag));
        Check(Native.SelectionGestureEventNew(0, out _gestureRelease, SelectionGestureEventType.Release));

        var dq = DispatcherQueue;
        session.OutputProcessed += RequestRedraw;
        session.TitleChanged += title => dq.TryEnqueue(() => TitleChanged?.Invoke(title));
        session.ProcessExited += code => dq.TryEnqueue(() => OnProcessExited(code));

        _session = session;
        session.Start(_commandLine, _workingDirectory, cols, rows);
    }

    private void OnProcessExited(uint code)
    {
        if (_session == null)
        {
            // Already closed (e.g. the window was closed, which killed the child).
            return;
        }

        if (code == 0)
        {
            CloseRequested?.Invoke();
            return;
        }

        // Like Windows Terminal: keep a failed session on screen so its output can be read.
        _exited = true;
        _session?.WriteToScreen($"\r\n\x1b[0;2m[process exited with code {code} (0x{code:X8}); press any key to close]\x1b[0m");
    }

    private void RequestRedraw()
    {
        // Coalesce bursts of PTY output into a single invalidate per UI tick.
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Volatile.Write(ref _invalidatePending, 0);
                _canvas.Invalidate();
            });
        }
    }

    public void Close()
    {
        _blinkTimer.Stop();
        var session = _session;
        if (session == null)
        {
            return;
        }

        _session = null;
        lock (session.Lock)
        {
            Native.SelectionGestureEventFree(_gesturePress);
            Native.SelectionGestureEventFree(_gestureDrag);
            Native.SelectionGestureEventFree(_gestureRelease);
            Native.SelectionGestureFree(_gesture, session.Handle);
        }

        session.Dispose();
        Native.RowCellsFree(_rowCells);
        Native.RowIteratorFree(_rowIterator);
        Native.RenderStateFree(_renderState);
    }

    // ------------------------------------------------------------ rendering

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

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var session = _session;
        if (session == null || _brush == null)
        {
            return;
        }

        lock (session.Lock)
        {
            if (Native.RenderStateUpdate(_renderState, session.Handle) != GhosttyResult.Success)
            {
                return;
            }
        }

        // The render state is a snapshot, so the rest runs without the lock.
        var colors = new GhosttyRenderStateColors { Size = (nuint)sizeof(GhosttyRenderStateColors) };
        Native.RenderStateGet(_renderState, RenderStateData.Colors, &colors);
        var cursor = new GhosttyRenderStateCursor { Size = (nuint)sizeof(GhosttyRenderStateCursor) };
        Native.RenderStateGet(_renderState, RenderStateData.Cursor, &cursor);

        var defaultFg = ToColor(colors.Foreground);
        var defaultBg = ToColor(colors.Background);
        var cursorColor = colors.CursorHasValue != 0 ? ToColor(colors.Cursor) : defaultFg;
        ds.Clear(defaultBg);

        UpdateBlinkTimer(cursor.Blinking != 0 && cursor.Visible != 0);

        var iterator = _rowIterator;
        Native.RenderStateGet(_renderState, RenderStateData.RowIterator, &iterator);

        var y = 0;
        while (Native.RowIteratorNext(_rowIterator) != 0 && y < _rows)
        {
            var count = ReadRow(defaultFg, defaultBg);
            var top = PaddingDip + y * _cellH;
            DrawBackgrounds(ds, count, top);
            DrawText(ds, count, top);
            DrawDecorations(ds, count, top);

            if (cursor.Visible != 0 && cursor.ViewportHasValue != 0 && cursor.ViewportY == y)
            {
                DrawCursor(ds, cursor, count, top, cursorColor, defaultBg);
            }

            y++;
        }

        Native.RenderStateClean(_renderState);
    }

    private int ReadRow(Color defaultFg, Color defaultBg)
    {
        var cells = _rowCells;
        Native.RowGet(_rowIterator, RenderStateRowData.Cells, &cells);

        var sel = new GhosttyRenderStateRowSelection { Size = (nuint)sizeof(GhosttyRenderStateRowSelection) };
        var hasSelection = Native.RowGet(_rowIterator, RenderStateRowData.Selection, &sel) == GhosttyResult.Success;

        var x = 0;
        var cp = 0;
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

            var inverse = false;
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

            ds.FillRectangle(PaddingDip + start * _cellW, top, (x - start) * _cellW, _cellH, color);
        }

        ds.Antialiasing = CanvasAntialiasing.Antialiased;
    }

    private void DrawText(CanvasDrawingSession ds, int count, float top)
    {
        // Batch cells into glyph runs with a fixed advance of one cell, so each
        // glyph lands exactly on the grid. Cells the primary font can't render
        // (emoji, CJK, clusters) fall back to DirectWrite text layout.
        var runStart = -1;
        var runLen = 0;
        var runVariant = 0;
        Color runColor = default;

        for (var x = 0; x < count; x++)
        {
            ref var cell = ref _cells[x];
            var blank = cell.CpLen == 0 || (cell.Flags & (CellFlags.Invisible | CellFlags.SpacerTail)) != 0 ||
                        (cell.CpLen == 1 && _codepoints[cell.CpStart] == ' ');

            if (blank)
            {
                if (runLen > 0)
                {
                    // Keep the run going across blank cells with a space glyph.
                    _glyphs[runLen++] = new CanvasGlyph { Index = GlyphIndex(' ', runVariant), Advance = _cellW };
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

            _glyphs[runLen++] = new CanvasGlyph { Index = glyph, Advance = _cellW };
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
            new Vector2(PaddingDip + start * _cellW, top + _baseline),
            face, _fontSize, _glyphs.AsSpan(0, len).ToArray(), false, 0, _brush);
    }

    private void DrawFallback(CanvasDrawingSession ds, ref Cell cell, int x, float top)
    {
        var text = CellText(ref cell);
        var width = (cell.Flags & CellFlags.Wide) != 0 ? 2 * _cellW : _cellW;
        var format = _formats[cell.FontVariant]!;
        format.HorizontalAlignment = CanvasHorizontalAlignment.Center;
        using var layout = new CanvasTextLayout(ds, text, format, width, _cellH);
        format.HorizontalAlignment = CanvasHorizontalAlignment.Left;
        // Color glyphs only for emoji presentation. Default-emoji characters are
        // East Asian Wide (two cells); narrow symbols like U+2714 are text unless
        // followed by VS16 (U+FE0F), matching how Windows Terminal renders them.
        var emoji = (cell.Flags & CellFlags.Wide) != 0 || text.Contains('️');
        layout.Options = emoji ? CanvasDrawTextOptions.EnableColorFont : CanvasDrawTextOptions.Default;

        // Align the fallback font's baseline with the grid's baseline.
        var lines = layout.LineMetrics;
        var offset = lines.Length > 0 ? _baseline - lines[0].Baseline : 0;
        ds.DrawTextLayout(layout, PaddingDip + x * _cellW, top + offset, cell.Fg);
    }

    private void DrawDecorations(CanvasDrawingSession ds, int count, float top)
    {
        var thickness = MathF.Max(1, MathF.Round(_fontSize / 14 * _scale)) / _scale;
        for (var x = 0; x < count; x++)
        {
            ref var cell = ref _cells[x];
            if ((cell.Flags & CellFlags.Invisible) != 0)
            {
                continue;
            }

            var left = PaddingDip + x * _cellW;
            var width = (cell.Flags & CellFlags.Wide) != 0 ? 2 * _cellW : _cellW;
            if (cell.Underline != 0)
            {
                var uy = top + _baseline + 2 * thickness;
                ds.FillRectangle(left, uy, width, thickness, cell.Fg);
                if (cell.Underline == 2)
                {
                    // Double underline.
                    ds.FillRectangle(left, uy + 2 * thickness, width, thickness, cell.Fg);
                }
            }

            if ((cell.Flags & CellFlags.Strikethrough) != 0)
            {
                ds.FillRectangle(left, top + _cellH * 0.55f, width, thickness, cell.Fg);
            }

            if ((cell.Flags & CellFlags.Overline) != 0)
            {
                ds.FillRectangle(left, top, width, thickness, cell.Fg);
            }
        }
    }

    private void DrawCursor(CanvasDrawingSession ds, GhosttyRenderStateCursor cursor, int count, float top,
        Color color, Color defaultBg)
    {
        var x = (int)cursor.ViewportX;
        var left = PaddingDip + x * _cellW;
        var wide = x < count && (_cells[x].Flags & CellFlags.Wide) != 0;
        var width = wide ? 2 * _cellW : _cellW;
        var thin = MathF.Max(1, MathF.Round(_scale)) / _scale;

        var style = _focused ? cursor.VisualStyle : CursorVisualStyle.BlockHollow;
        if (_focused && cursor.Blinking != 0 && !_blinkOn)
        {
            return;
        }

        switch (style)
        {
            case CursorVisualStyle.Bar:
                ds.FillRectangle(left, top, 2 * thin, _cellH, color);
                break;
            case CursorVisualStyle.Underline:
                ds.FillRectangle(left, top + _cellH - 2 * thin, width, 2 * thin, color);
                break;
            case CursorVisualStyle.BlockHollow:
                ds.DrawRectangle(left + thin / 2, top + thin / 2, width - thin, _cellH - thin, color, thin);
                break;
            default:
                ds.FillRectangle(left, top, width, _cellH, color);
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
                        ds.DrawGlyphRun(new Vector2(left, top + _baseline), face, _fontSize,
                            [new CanvasGlyph { Index = glyph, Advance = _cellW }], false, 0, _brush);
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

    private void UpdateBlinkTimer(bool blinking)
    {
        if (blinking && _focused)
        {
            if (!_blinkTimer.IsEnabled)
            {
                _blinkOn = true;
                _blinkTimer.Start();
            }
        }
        else if (_blinkTimer.IsEnabled)
        {
            _blinkTimer.Stop();
            _blinkOn = true;
        }
    }

    private void ResetBlink()
    {
        _blinkOn = true;
        if (_blinkTimer.IsEnabled)
        {
            _blinkTimer.Stop();
            _blinkTimer.Start();
        }
    }

    // ------------------------------------------------------------- keyboard

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        // Each key's WM_CHAR arrives after its KeyDown and before the next
        // key's KeyDown, so this flag scopes suppression to a single key.
        _charHandledByKeyDown = false;
        var session = _session;
        if (session == null)
        {
            return;
        }

        var vk = (int)e.Key;
        if (Keyboard.IsModifierKey(vk))
        {
            return;
        }

        if (_exited)
        {
            e.Handled = true;
            _charHandledByKeyDown = true;
            CloseRequested?.Invoke();
            return;
        }

        var status = e.KeyStatus;
        var state = Keyboard.State.Capture();

        if (HandleShortcut(e.Key, state, session))
        {
            e.Handled = true;
            _charHandledByKeyDown = true;
            return;
        }

        var fullText = state.Translate((uint)vk, status.ScanCode);
        if (fullText == null)
        {
            // Dead key: let the system compose it and deliver the result via CharacterReceived.
            return;
        }

        var kitty = session.KittyKeyboardFlags != 0;
        if (Keyboard.IsPrintable(fullText) && !kitty)
        {
            // Plain text (and AltGr text, which Windows reports as Ctrl+Alt)
            // arrives through CharacterReceived.
            if (!state.Ctrl && !state.Alt || state.Ctrl && state.Alt)
            {
                return;
            }
        }

        var key = KeyMap.FromScanCode(status.ScanCode, status.IsExtendedKey, e.Key);
        var text = state.WithoutCtrlAlt().Translate((uint)vk, status.ScanCode);
        if (!Keyboard.IsPrintable(text))
        {
            text = null;
        }

        var unshifted = state.WithoutCtrlAlt().WithoutShift().Translate((uint)vk, status.ScanCode);
        uint unshiftedCp = Keyboard.IsPrintable(unshifted) ? (uint)char.ConvertToUtf32(unshifted!, 0) : 0;

        var mods = state.Mods;
        var consumed = GhosttyMods.None;
        if (text != null && state.Shift && text != unshifted)
        {
            consumed |= GhosttyMods.Shift;
        }

        var action = status.WasKeyDown ? KeyAction.Repeat : KeyAction.Press;
        var bytes = session.EncodeKey(action, key, mods, consumed, text, unshiftedCp);
        if (bytes.Length > 0)
        {
            session.SendInput(bytes);
            ResetBlink();
            e.Handled = true;
            _charHandledByKeyDown = true;
        }
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        // A key's WM_CHAR is always retrieved before its WM_KEYUP, so suppression
        // ends here; later chars without a KeyDown (on-screen keyboards, voice
        // typing, automation) must not be swallowed.
        _charHandledByKeyDown = false;

        var session = _session;
        if (session == null || Keyboard.IsModifierKey((int)e.Key))
        {
            return;
        }

        // Key releases are only reported under kitty's "report event types" flag.
        const byte reportEvents = 1 << 1;
        if ((session.KittyKeyboardFlags & reportEvents) == 0)
        {
            return;
        }

        var status = e.KeyStatus;
        var state = Keyboard.State.Capture();
        var key = KeyMap.FromScanCode(status.ScanCode, status.IsExtendedKey, e.Key);
        var bytes = session.EncodeKey(KeyAction.Release, key, state.Mods, GhosttyMods.None, null, 0);
        if (bytes.Length > 0)
        {
            session.SendInput(bytes);
            e.Handled = true;
        }
    }

    protected override void OnCharacterReceived(CharacterReceivedRoutedEventArgs e)
    {
        var session = _session;
        if (session == null)
        {
            return;
        }

        e.Handled = true;
        if (_charHandledByKeyDown)
        {
            return;
        }

        // Anything reaching here wasn't encoded by OnKeyDown (plain text,
        // AltGr text, composed dead keys, or input synthesized without key
        // state), so forward it verbatim, control characters included.
        var c = e.Character;
        if (char.IsHighSurrogate(c))
        {
            _pendingHighSurrogate = c;
            return;
        }

        string text;
        if (char.IsLowSurrogate(c))
        {
            if (_pendingHighSurrogate == 0)
            {
                return;
            }

            text = new string([_pendingHighSurrogate, c]);
            _pendingHighSurrogate = '\0';
        }
        else
        {
            text = c.ToString();
        }

        session.SendInput(text);
        ResetBlink();
    }

    private bool HandleShortcut(VirtualKey key, Keyboard.State state, TerminalSession session)
    {
        var ctrl = state.Ctrl && !state.Alt;
        var ctrlShift = ctrl && state.Shift;

        if (ctrlShift && key == VirtualKey.C || ctrl && !state.Shift && key == VirtualKey.C && HasSelection())
        {
            CopySelection();
            return true;
        }

        if (ctrlShift && key == VirtualKey.V || state.Shift && !state.Ctrl && key == VirtualKey.Insert)
        {
            PasteFromClipboard();
            return true;
        }

        if (ctrl && key is (VirtualKey)187 or VirtualKey.Add) // '=' / numpad +
        {
            SetFontSize(_fontSize + 1);
            return true;
        }

        if (ctrl && key is (VirtualKey)189 or VirtualKey.Subtract) // '-' / numpad -
        {
            SetFontSize(_fontSize - 1);
            return true;
        }

        if (ctrl && key is VirtualKey.Number0 or VirtualKey.NumberPad0)
        {
            SetFontSize(_config.FontSize);
            return true;
        }

        if (state.Shift && !state.Ctrl && !state.Alt && key is VirtualKey.PageUp or VirtualKey.PageDown)
        {
            session.ScrollLines(key == VirtualKey.PageUp ? -(_rows - 1) : _rows - 1);
            _canvas.Invalidate();
            return true;
        }

        if (ctrlShift && key is VirtualKey.Home or VirtualKey.End)
        {
            session.ScrollViewport(new GhosttyTerminalScrollViewport
            {
                Tag = key == VirtualKey.Home ? ScrollViewportTag.Top : ScrollViewportTag.Bottom,
            });
            _canvas.Invalidate();
            return true;
        }

        return false;
    }

    private void SetFontSize(float size)
    {
        _fontSize = Math.Clamp(size, MinFontSize, MaxFontSize);
        UpdateFont();
        UpdateGrid();
    }

    // ------------------------------------------------------------ clipboard

    private bool HasSelection() => _session?.GetSelectionText() != null;

    private void CopySelection()
    {
        var text = _session?.GetSelectionText();
        if (text == null)
        {
            return;
        }

        ClipboardText.Set(text);
        _session!.ClearSelection();
        _canvas.Invalidate();
    }

    private void PasteFromClipboard()
    {
        if (_session is { } session)
        {
            ClipboardText.PasteInto(session);
        }
    }

    // ---------------------------------------------------------------- mouse

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var session = _session;
        if (session == null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_canvas);
        var props = point.Properties;
        CapturePointer(e.Pointer);
        e.Handled = true;

        if (ShouldReportMouse(session, e))
        {
            var button = props.IsLeftButtonPressed ? MouseButton.Left
                : props.IsRightButtonPressed ? MouseButton.Right
                : props.IsMiddleButtonPressed ? MouseButton.Middle
                : MouseButton.Unknown;
            _reportedButton = button;
            SendMouse(session, MouseAction.Press, button, e, anyPressed: true);
            return;
        }

        if (props.IsRightButtonPressed)
        {
            // Windows Terminal convention: right-click copies a selection, or pastes.
            if (HasSelection()) CopySelection();
            else PasteFromClipboard();
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            _selecting = true;
            RunGesture(session, _gesturePress, point.Position, isPress: true);
        }
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        var session = _session;
        if (session == null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_canvas);
        var reporting = !_selecting && ShouldReportMouse(session, e);
        UpdatePointerCursor(arrow: reporting);

        if (_selecting)
        {
            RunGesture(session, _gestureDrag, point.Position, isPress: false);
            e.Handled = true;
        }
        else if (reporting)
        {
            SendMouse(session, MouseAction.Motion, _reportedButton, e, anyPressed: _reportedButton != null);
            e.Handled = true;
        }
    }

    private bool _arrowCursor;

    /// <summary>
    /// Like Windows Terminal: an arrow while the app consumes the mouse, an
    /// I-beam when the mouse selects text (including Shift-bypass).
    /// </summary>
    private void UpdatePointerCursor(bool arrow)
    {
        if (arrow == _arrowCursor)
        {
            return;
        }

        _arrowCursor = arrow;
        ProtectedCursor = InputSystemCursor.Create(arrow ? InputSystemCursorShape.Arrow : InputSystemCursorShape.IBeam);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        var session = _session;
        ReleasePointerCapture(e.Pointer);
        if (session == null)
        {
            return;
        }

        e.Handled = true;
        if (_selecting)
        {
            _selecting = false;
            RunGesture(session, _gestureRelease, e.GetCurrentPoint(_canvas).Position, isPress: false);
        }
        else if (_reportedButton is { } button)
        {
            _reportedButton = null;
            SendMouse(session, MouseAction.Release, button, e, anyPressed: false);
        }
    }

    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        var session = _session;
        if (session == null)
        {
            return;
        }

        e.Handled = true;
        var delta = e.GetCurrentPoint(_canvas).Properties.MouseWheelDelta;
        var notches = Math.Max(1, Math.Abs(delta) / 120);
        var up = delta > 0;

        if (ShouldReportMouse(session, e))
        {
            for (var i = 0; i < notches; i++)
            {
                SendMouse(session, MouseAction.Press, up ? MouseButton.Four : MouseButton.Five, e, anyPressed: false);
            }

            return;
        }

        bool alternateScreen;
        lock (session.Lock)
        {
            int screen;
            Native.TerminalGet(session.Handle, TerminalData.ActiveScreen, &screen);
            alternateScreen = screen == 1;
        }

        if (alternateScreen && session.GetMode(GhosttyMode.AltScroll))
        {
            // Full-screen apps without mouse support (less, man) get arrow keys.
            var key = up ? GhosttyKey.ArrowUp : GhosttyKey.ArrowDown;
            for (var i = 0; i < notches * 3; i++)
            {
                session.SendInput(session.EncodeKey(KeyAction.Press, key, GhosttyMods.None, GhosttyMods.None, null, 0));
            }

            return;
        }

        session.ScrollLines((up ? -3 : 3) * notches);
        _canvas.Invalidate();
    }

    private static bool ShouldReportMouse(TerminalSession session, PointerRoutedEventArgs e) =>
        // Holding Shift bypasses mouse reporting so text can still be selected.
        (e.KeyModifiers & VirtualKeyModifiers.Shift) == 0 && session.IsMouseTracking;

    private void SendMouse(TerminalSession session, MouseAction action, MouseButton? button, PointerRoutedEventArgs e,
        bool anyPressed)
    {
        var pos = e.GetCurrentPoint(_canvas).Position;
        var mods = GhosttyMods.None;
        if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0) mods |= GhosttyMods.Shift;
        if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0) mods |= GhosttyMods.Ctrl;
        if ((e.KeyModifiers & VirtualKeyModifiers.Menu) != 0) mods |= GhosttyMods.Alt;

        var widthPx = (uint)(_canvas.ActualWidth * _scale);
        var heightPx = (uint)(_canvas.ActualHeight * _scale);
        var padPx = (uint)(PaddingDip * _scale);
        var size = new GhosttyMouseEncoderSize
        {
            ScreenWidth = widthPx,
            ScreenHeight = heightPx,
            CellWidth = _cellWPx,
            CellHeight = _cellHPx,
            PaddingLeft = padPx,
            PaddingTop = padPx,
            PaddingRight = (uint)Math.Max(0, (int)widthPx - (int)padPx - _cols * (int)_cellWPx),
            PaddingBottom = (uint)Math.Max(0, (int)heightPx - (int)padPx - _rows * (int)_cellHPx),
        };

        var bytes = session.EncodeMouse(action, button, mods, (float)(pos.X * _scale), (float)(pos.Y * _scale), size,
            anyPressed);
        if (bytes.Length > 0)
        {
            session.SendInput(bytes);
        }
    }

    private void RunGesture(TerminalSession session, nint gestureEvent, Windows.Foundation.Point pos, bool isPress)
    {
        var col = (ushort)Math.Clamp((int)((pos.X - PaddingDip) / _cellW), 0, _cols - 1);
        var row = (uint)Math.Clamp((int)((pos.Y - PaddingDip) / _cellH), 0, _rows - 1);

        lock (session.Lock)
        {
            // Grid refs are untracked and only valid until the next terminal
            // mutation, so resolve, run the gesture and apply it under one lock.
            var gridRef = GhosttyGridRef.Init();
            var point = new GhosttyPoint { Tag = PointTag.Viewport, X = col, Y = row };
            if (Native.TerminalGridRef(session.Handle, point, &gridRef) != GhosttyResult.Success)
            {
                return;
            }

            Native.SelectionGestureEventSet(gestureEvent, SelectionGestureEventOption.Ref, &gridRef);

            var position = new GhosttySurfacePosition { X = pos.X * _scale, Y = pos.Y * _scale };
            Native.SelectionGestureEventSet(gestureEvent, SelectionGestureEventOption.Position, &position);

            var geometry = new GhosttySelectionGestureGeometry
            {
                Columns = _cols,
                CellWidth = _cellWPx,
                PaddingLeft = (uint)(PaddingDip * _scale),
                ScreenHeight = (uint)(_canvas.ActualHeight * _scale),
            };
            Native.SelectionGestureEventSet(gestureEvent, SelectionGestureEventOption.Geometry, &geometry);

            if (isPress)
            {
                // Timestamps enable double-click (word) and triple-click (line) selection.
                var nowNs = (ulong)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));
                Native.SelectionGestureEventSet(gestureEvent, SelectionGestureEventOption.TimeNs, &nowNs);
                ulong intervalNs = 500_000_000;
                Native.SelectionGestureEventSet(gestureEvent, SelectionGestureEventOption.RepeatIntervalNs, &intervalNs);
                var distance = 4.0 * _scale;
                Native.SelectionGestureEventSet(gestureEvent, SelectionGestureEventOption.RepeatDistance, &distance);
            }

            var selection = GhosttySelection.Init();
            var result = Native.SelectionGestureEvent(_gesture, session.Handle, gestureEvent, &selection);
            if (result == GhosttyResult.Success)
            {
                Native.TerminalSet(session.Handle, TerminalOption.Selection, &selection);
            }
            else if (isPress && result == GhosttyResult.NoValue)
            {
                // A plain click clears any existing selection.
                Native.TerminalSet(session.Handle, TerminalOption.Selection, null);
            }
        }

        _canvas.Invalidate();
    }

    // ---------------------------------------------------------------- focus

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        _focused = true;
        ResetBlink();
        SendFocusEvent(true);
        _canvas.Invalidate();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        _focused = false;
        SendFocusEvent(false);
        _canvas.Invalidate();
    }

    private void SendFocusEvent(bool focused)
    {
        if (_session is { HasExited: false } session && session.GetMode(GhosttyMode.FocusEvent))
        {
            session.SendInput(focused ? "\x1b[I"u8 : "\x1b[O"u8, scrollToBottom: false);
        }
    }

    // -------------------------------------------------------------- helpers

    private static Color ToColor(GhosttyColorRgb c) => Color.FromArgb(255, c.R, c.G, c.B);

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
