using Zinc.Ghostty;

namespace Zinc.Terminal;

[Flags]
public enum FrameCellFlags : ushort
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
    Placeholder = 256, // a Kitty graphics Unicode placeholder, which is part of an image, not text
    Selected = 512, // in the selection (ligatures break at its edges)
}

/// <summary>One cell of a frame, with its colors already worked out. Its text is <see cref="FrameReader.Text"/>.</summary>
public struct FrameCell
{
    public int CpStart;
    public int CpLen;
    public GhosttyColorRgb Fg;

    /// <summary>The background, which only means something when <see cref="HasBg"/> is set. Otherwise the cell shows the default background.</summary>
    public GhosttyColorRgb Bg;
    public bool HasBg;
    public FrameCellFlags Flags;

    /// <summary>The SGR underline style: 0 none, 1 single, 2 double, 3 curly, 4 dotted, 5 dashed.</summary>
    public int Underline;

    public readonly int FontVariant => (int)(Flags & (FrameCellFlags.Bold | FrameCellFlags.Italic));
}

/// <summary>
/// Reads a terminal's screen into cells a renderer can draw, whatever it draws with. It owns libghostty's render
/// state, and does the part of a frame that isn't drawing: which characters a cell holds, and the colors it ends up
/// with after the selection, inverse video, bold-is-bright, minimum contrast and faint are applied, in that order.
/// A frame is <see cref="Update"/>, then <see cref="Read"/>, then the drawing, then <see cref="Clean"/>.
///
/// The rules here are the ones the Windows renderer applies in its own ReadRow, which doesn't use this class yet.
/// Until it does, a change to either belongs in both.
/// </summary>
public sealed unsafe class FrameReader : IDisposable
{
    /// <summary>The character a Kitty image's Unicode placeholder cells hold.</summary>
    public const uint KittyPlaceholder = 0x10EEEE;

    private readonly nint _state;
    private readonly nint _rowIterator;
    private readonly nint _rowCells;
    private readonly GhosttyColorRgb[] _bright = new GhosttyColorRgb[8];
    private readonly byte[] _palette = new byte[768];
    private FrameCell[][] _cells = [];
    private int[] _counts = [];
    private int _columns;
    private uint[] _codepoints = new uint[4096];
    private bool _disposed;

    public FrameReader()
    {
        Check(Native.RenderStateNew(0, out _state));
        Check(Native.RowIteratorNew(0, out _rowIterator));
        Check(Native.RowCellsNew(0, out _rowCells));
    }

    /// <summary>The settings that decide a cell's colors: the theme's selection colors, bold-is-bright, bold-color, minimum-contrast and faint-opacity.</summary>
    public Config Config { get; set; } = new();

    /// <summary>The rows the last <see cref="Read"/> filled.</summary>
    public int Rows { get; private set; }

    public GhosttyColorRgb Foreground { get; private set; }

    public GhosttyColorRgb Background { get; private set; }

    /// <summary>The cursor's color when a program or the scheme set one. Null draws it in the foreground color.</summary>
    public GhosttyColorRgb? CursorColor { get; private set; }

    public GhosttyRenderStateCursor Cursor { get; private set; }

    /// <summary>The 256 palette colors as programs left them (OSC 4), three bytes each.</summary>
    public ReadOnlySpan<byte> Palette => _palette;

    /// <summary>
    /// Takes a snapshot of the session's screen, under its lock. False when the session has closed, and then there's
    /// nothing new to read.
    /// </summary>
    public bool Update(TerminalSession session)
    {
        lock (session.Lock)
        {
            return !_disposed && !session.IsDisposed && session.UpdateRenderState(_state);
        }
    }

    /// <summary>Reads the snapshot's colors, cursor, and up to <paramref name="rows"/> rows of <paramref name="cols"/> cells.</summary>
    public void Read(int rows, int cols)
    {
        var colors = new GhosttyRenderStateColors { Size = (nuint)sizeof(GhosttyRenderStateColors) };
        Native.RenderStateGet(_state, RenderStateData.Colors, &colors);
        var cursor = new GhosttyRenderStateCursor { Size = (nuint)sizeof(GhosttyRenderStateCursor) };
        Native.RenderStateGet(_state, RenderStateData.Cursor, &cursor);
        Cursor = cursor;
        Foreground = colors.Foreground;
        Background = colors.Background;
        CursorColor = colors.CursorHasValue != 0 ? colors.Cursor : null;
        new ReadOnlySpan<byte>(colors.Palette, 768).CopyTo(_palette);
        for (var i = 0; i < 8; i++)
        {
            _bright[i] = new GhosttyColorRgb(_palette[(i + 8) * 3], _palette[(i + 8) * 3 + 1], _palette[(i + 8) * 3 + 2]);
        }

        var iterator = _rowIterator;
        Native.RenderStateGet(_state, RenderStateData.RowIterator, &iterator);
        EnsureRoom(rows, cols);
        var used = 0;
        var read = 0;
        while (read < rows && Native.RowIteratorNext(_rowIterator) != 0)
        {
            _counts[read] = ReadRow(read, cols, ref used);
            read++;
        }

        Rows = read;
    }

    public ReadOnlySpan<FrameCell> Row(int y) => _cells[y].AsSpan(0, _counts[y]);

    /// <summary>A cell's characters: one code point, or a whole grapheme cluster. Empty for a blank cell.</summary>
    public ReadOnlySpan<uint> Text(in FrameCell cell) => _codepoints.AsSpan(cell.CpStart, cell.CpLen);

    /// <summary>Marks the snapshot as drawn, so the next <see cref="Update"/> knows what changed since.</summary>
    public void Clean() => Native.RenderStateClean(_state);

    private int ReadRow(int y, int cols, ref int cp)
    {
        var row = _cells[y];
        var cells = _rowCells;
        Native.RowGet(_rowIterator, RenderStateRowData.Cells, &cells);

        var sel = new GhosttyRenderStateRowSelection { Size = (nuint)sizeof(GhosttyRenderStateRowSelection) };
        var hasSelection = Native.RowGet(_rowIterator, RenderStateRowData.Selection, &sel) == GhosttyResult.Success;
        var theme = Config.Theme;
        var selectionBg = hasSelection ? theme.SelectionBackground : null;
        var selectionFg = hasSelection ? theme.SelectionForeground : null;
        var (defaultFg, defaultBg) = (Foreground, Background);

        var x = 0;
        while (x < cols && Native.RowCellsNext(_rowCells) != 0)
        {
            ref var cell = ref row[x];
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
                    var buffer = new GhosttyCodepointBuffer { Ptr = p, Cap = len };
                    Native.RowCellsGet(_rowCells, RenderStateRowCellsData.GraphemesBuf, &buffer);
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
                    if (wide == (int)CellWide.Wide) cell.Flags |= FrameCellFlags.Wide;
                    else if (wide == (int)CellWide.SpacerTail) cell.Flags |= FrameCellFlags.SpacerTail;
                }
            }

            var inverse = false;
            var (fgTag, fgPalette) = (0, 0); // the style's foreground as the program set it, as a tag and, for a palette color, its index
            byte hasStyling = 0;
            Native.RowCellsGet(_rowCells, RenderStateRowCellsData.HasStyling, &hasStyling);
            if (hasStyling != 0)
            {
                GhosttyStyle style;
                Native.RowCellsGet(_rowCells, RenderStateRowCellsData.Style, &style);
                if (style.Bold) cell.Flags |= FrameCellFlags.Bold;
                if (style.Italic) cell.Flags |= FrameCellFlags.Italic;
                if (style.Faint) cell.Flags |= FrameCellFlags.Faint;
                if (style.Invisible) cell.Flags |= FrameCellFlags.Invisible;
                if (style.Strikethrough) cell.Flags |= FrameCellFlags.Strikethrough;
                if (style.Overline) cell.Flags |= FrameCellFlags.Overline;
                cell.Underline = style.Underline;
                inverse = style.Inverse;
                (fgTag, fgPalette) = (style.FgColorTag, style.FgColor.R);
            }

            // The image list draws what a placeholder cell stands for, so the cell itself draws nothing.
            if (cell.CpLen > 0 && _codepoints[cell.CpStart] == KittyPlaceholder)
            {
                cell.Flags |= FrameCellFlags.Placeholder;
            }

            GhosttyColorRgb rgb;
            cell.Fg = Native.RowCellsGet(_rowCells, RenderStateRowCellsData.FgColor, &rgb) == GhosttyResult.Success ? rgb : defaultFg;
            if (Native.RowCellsGet(_rowCells, RenderStateRowCellsData.BgColor, &rgb) == GhosttyResult.Success)
            {
                cell.Bg = rgb;
                cell.HasBg = true;
            }

            // Bold draws palette colors 0-7 in their bright versions, and the default color in bold-color.
            if ((cell.Flags & FrameCellFlags.Bold) != 0)
            {
                if (fgTag == 1 && fgPalette < 8 && Config.BoldIsBright)
                {
                    cell.Fg = _bright[fgPalette];
                }
                else if (fgTag == 0 && Config.BoldColor is { } bold)
                {
                    cell.Fg = bold;
                }
            }

            // The selection uses the scheme's (or config's) selection colors, or else inverse video,
            // which works with any theme.
            var selected = hasSelection && x >= sel.StartX && x <= sel.EndX;
            if (selected)
            {
                cell.Flags |= FrameCellFlags.Selected;
            }

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
                var against = cell.HasBg ? cell.Bg : defaultBg;
                var (r, g, b) = MinimumContrast.Ensure((cell.Fg.R, cell.Fg.G, cell.Fg.B), (against.R, against.G, against.B), Config.MinimumContrast);
                cell.Fg = new GhosttyColorRgb(r, g, b);
            }

            if ((cell.Flags & FrameCellFlags.Faint) != 0)
            {
                cell.Fg = Blend(cell.Fg, cell.HasBg ? cell.Bg : defaultBg, 1 - (float)Config.FaintOpacity);
            }

            x++;
        }

        return x;
    }

    /// <summary>Room for <paramref name="rows"/> rows of <paramref name="cols"/> cells (grown, never shrunk).</summary>
    private void EnsureRoom(int rows, int cols)
    {
        if (_cells.Length >= rows && _columns >= cols)
        {
            return;
        }

        rows = Math.Max(rows, _cells.Length);
        cols = Math.Max(cols, _columns);
        _cells = new FrameCell[rows][];
        for (var y = 0; y < rows; y++)
        {
            _cells[y] = new FrameCell[cols];
        }

        _counts = new int[rows];
        _columns = cols;
        Rows = 0;
    }

    private static GhosttyColorRgb Blend(GhosttyColorRgb a, GhosttyColorRgb b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static void Check(GhosttyResult result)
    {
        if (result != GhosttyResult.Success)
        {
            throw new InvalidOperationException($"libghostty: {result}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Native.RowCellsFree(_rowCells);
        Native.RowIteratorFree(_rowIterator);
        Native.RenderStateFree(_state);
    }
}
