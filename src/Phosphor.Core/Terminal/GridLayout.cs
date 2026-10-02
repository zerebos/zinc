namespace Phosphor.Terminal;

/// <summary>
/// Where a pane's grid goes: as many whole cells as fit inside the padding, from the top-left corner. What's
/// left over (less than a cell each way) ends up on the right and bottom, or, with <c>padding-balance</c>,
/// is split between both sides (Ghostty's <c>window-padding-balance</c>).
/// </summary>
public static class GridLayout
{
    /// <summary>The grid's size in cells and its top-left corner in DIPs.</summary>
    public readonly record struct Placement(ushort Columns, ushort Rows, float Left, float Top);

    public static Placement Place(double width, double height, float padLeft, float padRight, float padTop, float padBottom,
        float cellWidth, float cellHeight, float scale, bool balance)
    {
        var columns = (ushort)Math.Clamp((int)((width - padLeft - padRight) / cellWidth), 2, 1000);
        var rows = (ushort)Math.Clamp((int)((height - padTop - padBottom) / cellHeight), 1, 1000);
        var left = padLeft;
        var top = padTop;
        if (balance)
        {
            left += Half(width - padLeft - padRight - columns * cellWidth, scale);
            top += Half(height - padTop - padBottom - rows * cellHeight, scale);
        }

        return new Placement(columns, rows, left, top);
    }

    /// <summary>
    /// Half the spare room, in whole device pixels: the grid's offset within a pixel stays what the padding makes it,
    /// whatever the window's size (the glyph atlas draws at that offset and starts over when it changes).
    /// </summary>
    private static float Half(double spare, float scale) => spare > 0 ? MathF.Floor((float)(spare * scale) / 2) / scale : 0;
}
