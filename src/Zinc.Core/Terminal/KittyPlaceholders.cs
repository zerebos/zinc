using Zinc.Ghostty;

namespace Zinc.Terminal;

/// <summary>
/// Kitty graphics Unicode placeholders (virtual placements, <c>U=1</c>, which yazi uses). The
/// program prints U+10EEEE cells whose foreground color names the image, underline color
/// the placement, and combining diacritics the image row/column (and the id's high byte).
/// Adjacent compatible cells form runs, and each run draws its slice of the image, scaled
/// to fit the virtual placement's grid. Ported from Ghostty's graphics_unicode.zig.
/// </summary>
public static partial class KittyPlaceholders
{
    public const uint Placeholder = 0x10EEEE;

    /// <summary>The value a row/column diacritic stands for, or null if it isn't one.</summary>
    public static int? DiacriticIndex(uint codepoint)
    {
        var i = Array.BinarySearch(Diacritics, codepoint);
        return i >= 0 ? i : null;
    }

    /// <summary>One placeholder cell, whose missing parts continue the run to its left.</summary>
    public readonly record struct Cell(uint IdLow, byte? IdHigh, uint PlacementId, int? Row, int? Col);

    /// <summary>
    /// A run of placeholder cells on one row, which draws image cells (Row, Col..Col+Width-1) at column X.
    /// </summary>
    public readonly record struct Run(int X, uint ImageId, uint PlacementId, int Row, int Col, int Width);

    /// <summary>
    /// Parses a placeholder cell from its combining marks (the codepoints after U+10EEEE) and
    /// the ids its colors encode (see <see cref="ColorId"/>). Invalid diacritics count as absent.
    /// </summary>
    public static Cell Parse(ReadOnlySpan<uint> diacritics, uint foregroundId, uint underlineId)
    {
        int? row = diacritics.Length > 0 ? DiacriticIndex(diacritics[0]) : null;
        int? col = diacritics.Length > 1 ? DiacriticIndex(diacritics[1]) : null;
        byte? high = diacritics.Length > 2 && DiacriticIndex(diacritics[2]) is { } h and <= 255 ? (byte)h : null;
        return new Cell(foregroundId, high, underlineId, row, col);
    }

    /// <summary>
    /// Turns a style color (a GhosttyStyle tag and its three bytes) into an id. The id is the palette index (the first
    /// byte) for a palette color, 24-bit RGB as 0xRRGGBB for an RGB color, and 0 when the color is unset.
    /// </summary>
    public static uint ColorId(int tag, GhosttyColorRgb color) => tag switch
    {
        1 => color.R, // palette
        2 => ((uint)color.R << 16) | ((uint)color.G << 8) | color.B, // rgb
        _ => 0,
    };

    /// <summary>Groups one row's cells (null = not a placeholder) into runs.</summary>
    public static void Runs(ReadOnlySpan<Cell?> row, List<Run> into)
    {
        Cell? start = null;
        var startX = 0;
        var width = 0;
        for (var x = 0; x <= row.Length; x++)
        {
            var cell = x < row.Length ? row[x] : null;
            if (start is { } s && cell is { } c &&
                c.IdLow == s.IdLow && c.PlacementId == s.PlacementId &&
                (c.Row == null || c.Row == s.Row) &&
                (c.Col == null || c.Col == s.Col + width) &&
                (c.IdHigh == null || c.IdHigh == s.IdHigh))
            {
                width++;
                continue;
            }

            if (start is { } done)
            {
                var id = done.IdLow | ((uint)(done.IdHigh ?? 0) << 24);
                into.Add(new Run(startX, id, done.PlacementId, done.Row ?? 0, done.Col ?? 0, width));
                start = null;
            }

            if (cell is { } next)
            {
                start = next with { Row = next.Row ?? 0, Col = next.Col ?? 0 };
                startX = x;
                width = 1;
            }
        }
    }

    /// <summary>Where a run draws, in pixels relative to its first cell, and from which part of the image.</summary>
    public readonly record struct Slice(int OffsetX, int OffsetY, int DestWidth, int DestHeight,
        int SourceX, int SourceY, int SourceWidth, int SourceHeight);

    /// <summary>
    /// The slice of an image a run shows. The image is fitted into the placement's grid
    /// (<paramref name="gridCols"/> × <paramref name="gridRows"/> cells, where 0 means sized from
    /// the image), keeping its aspect ratio, and centered. The run covers the image cells from
    /// (<see cref="Run.Row"/>, <see cref="Run.Col"/>) onward. Null when the run shows only the
    /// centering margin.
    /// </summary>
    public static Slice? Geometry(Run run, int imageWidth, int imageHeight, uint gridCols, uint gridRows,
        uint cellWidth, uint cellHeight)
    {
        var rows = gridRows != 0 ? gridRows : (uint)((imageHeight + cellHeight - 1) / cellHeight);
        var cols = gridCols != 0 ? gridCols : (uint)((imageWidth + cellWidth - 1) / cellWidth);
        if (rows == 0 || cols == 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return null;
        }

        double imgW = imageWidth, imgH = imageHeight;
        double gridW = cols * cellWidth, gridH = rows * cellHeight;

        // Fit the image in the grid, centering along the other axis.
        double scale, padX = 0, padY = 0;
        if (imgW * gridH > imgH * gridW)
        {
            scale = gridW / imgW;
            padY = (gridH - imgH * scale) / 2;
        }
        else
        {
            scale = gridH / imgH;
            padX = (gridW - imgW * scale) / 2;
        }

        // The image padded to the grid's shape, in image pixels.
        var marginX = padX / scale;
        var marginY = padY / scale;
        var paddedW = imgW + marginX * 2;
        var paddedH = imgH + marginY * 2;

        // The run's share of the padded image.
        var srcX = paddedW * run.Col / cols;
        var srcY = paddedH * run.Row / rows;
        var srcW = paddedW * run.Width / cols;
        var srcH = paddedH * 1 / rows;
        double destW = run.Width * cellWidth, destH = cellHeight;

        // The padding isn't part of the bitmap, so trim the source and shift the destination.
        (srcY, srcH, var offY, destH) = Trim(srcY, srcH, marginY, paddedH, imgH, scale, destH);
        (srcX, srcW, var offX, destW) = Trim(srcX, srcW, marginX, paddedW, imgW, scale, destW);
        if (srcW <= 0 || srcH <= 0)
        {
            return null;
        }

        return new Slice(
            (int)Math.Round(offX * scale), (int)Math.Round(offY * scale),
            (int)Math.Round(destW), (int)Math.Round(destH),
            (int)Math.Round(srcX), (int)Math.Round(srcY), (int)Math.Round(srcW), (int)Math.Round(srcH));
    }

    private static (double Src, double Len, double Offset, double Dest) Trim(double src, double len, double margin,
        double padded, double image, double scale, double dest)
    {
        if (src < margin)
        {
            // Starts in the leading margin.
            var offset = margin - src;
            len -= offset;
            dest -= offset * scale;
            if (len > image)
            {
                // Spans both margins.
                len = image;
                dest = image * scale;
            }

            return (0, len, offset, dest);
        }

        if (src + len > padded - margin)
        {
            // Ends in the trailing margin.
            src -= margin;
            len = padded - margin - src - margin;
            return (src, len, 0, len * scale);
        }

        return (src - margin, len, 0, dest);
    }
}
