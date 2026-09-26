using System.Text;
using Phantom.Ghostty;

namespace Phantom.Terminal;

/// <summary>
/// One terminal row as a string, with the string index where each cell starts, so matches
/// found in the text (URLs, search hits) map back to columns. Blank cells read as spaces; the
/// second half of a wide character adds nothing (its column shares the next cell's start).
/// </summary>
internal sealed unsafe class RowText
{
    private readonly int[] _cellStart;
    private readonly bool[] _spacerTail;

    private RowText(string text, int[] cellStart, bool[] spacerTail)
    {
        Text = text;
        _cellStart = cellStart;
        _spacerTail = spacerTail;
    }

    public string Text { get; }

    /// <summary>The string index where column <paramref name="col"/> starts.</summary>
    public int IndexOf(int col) => _cellStart[col];

    /// <summary>Reads row <paramref name="row"/> (in <paramref name="tag"/> coordinates). Hold the session lock.</summary>
    public static RowText Read(nint terminal, PointTag tag, uint row, ushort cols)
    {
        var text = new StringBuilder(cols);
        var cellStart = new int[cols + 1];
        var spacerTail = new bool[cols];
        Span<uint> graphemes = stackalloc uint[16];
        for (ushort c = 0; c < cols; c++)
        {
            cellStart[c] = text.Length;
            var gridRef = GhosttyGridRef.Init();
            var point = new GhosttyPoint { Tag = tag, X = c, Y = row };
            nuint len = 0;
            if (Native.TerminalGridRef(terminal, point, &gridRef) == GhosttyResult.Success)
            {
                int wide;
                if (Native.GridRefCell(&gridRef, out var cell) == GhosttyResult.Success &&
                    Native.CellGet(cell, CellData.Wide, &wide) == GhosttyResult.Success && wide == (int)CellWide.SpacerTail)
                {
                    spacerTail[c] = true;
                    continue;
                }

                fixed (uint* p = graphemes)
                {
                    if (Native.GridRefGraphemes(&gridRef, p, (nuint)graphemes.Length, out len) != GhosttyResult.Success)
                    {
                        len = 0;
                    }
                }
            }

            if (len == 0)
            {
                text.Append(' ');
            }

            for (var i = 0; i < (int)len; i++)
            {
                text.Append(char.ConvertFromUtf32((int)graphemes[i]));
            }
        }

        cellStart[cols] = text.Length;
        return new RowText(text.ToString(), cellStart, spacerTail);
    }

    /// <summary>The columns covering text [<paramref name="start"/>, start + length), wide characters whole.</summary>
    public (ushort First, ushort Last) Columns(int start, int length)
    {
        var cols = _spacerTail.Length;
        var first = Array.FindLastIndex(_cellStart, cols - 1, cols, i => i <= start);
        var last = Array.FindLastIndex(_cellStart, cols - 1, cols, i => i < start + length);
        while (first > 0 && _spacerTail[first])
        {
            first--;
        }

        while (last + 1 < cols && _spacerTail[last + 1])
        {
            last++;
        }

        return ((ushort)first, (ushort)Math.Max(first, last));
    }
}
