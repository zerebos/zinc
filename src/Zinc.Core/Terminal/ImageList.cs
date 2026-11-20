using Zinc.Ghostty;

namespace Zinc.Terminal;

/// <summary>
/// One image to draw, from a render state's image list. Its top-left corner is at viewport cell (<see cref="Col"/>,
/// <see cref="Row"/>) plus a pixel offset, and it's drawn at <see cref="PixelWidth"/> × <see cref="PixelHeight"/>
/// from a source rectangle in image pixels. The column and row are negative when the image starts left of or above
/// the viewport, and it can reach past the right or bottom edge, so clip it to the viewport when drawing.
/// </summary>
public readonly record struct ImageEntry(
    uint ImageId, ulong Generation, ImageLayer Layer, int Z,
    int Col, int Row, uint OffsetX, uint OffsetY,
    uint PixelWidth, uint PixelHeight,
    uint SourceX, uint SourceY, uint SourceWidth, uint SourceHeight);

/// <summary>An image's pixels as premultiplied RGBA, and the image generation they belong to.</summary>
public sealed record ImagePixels(ulong Generation, int Width, int Height, byte[] Rgba);

/// <summary>
/// The Kitty images a render state says to draw, sorted into drawing order, which is by layer and then by z.
/// libghostty has already resolved placeholder cells into entries, one for each row of cells. The list's owner calls
/// <see cref="Refresh"/> after each update of the render state, from whichever thread updated it.
/// </summary>
public sealed unsafe class ImageList : IDisposable
{
    private const int KeyCount = 15;

    private static readonly RenderStateImageData[] Keys =
    [
        RenderStateImageData.Protocol,
        RenderStateImageData.ImageId,
        RenderStateImageData.ImageGeneration,
        RenderStateImageData.Layer,
        RenderStateImageData.Z,
        RenderStateImageData.ViewportCol,
        RenderStateImageData.ViewportRow,
        RenderStateImageData.CellOffsetX,
        RenderStateImageData.CellOffsetY,
        RenderStateImageData.PixelWidth,
        RenderStateImageData.PixelHeight,
        RenderStateImageData.SourceX,
        RenderStateImageData.SourceY,
        RenderStateImageData.SourceWidth,
        RenderStateImageData.SourceHeight,
    ];

    private readonly List<ImageEntry> _entries = [];
    private nint _iterator;
    private ulong _generation;
    private bool _hasRead;

    public ImageList() => Check(Native.ImageIteratorNew(0, out _iterator));

    /// <summary>The images to draw, as of the last <see cref="Refresh"/>.</summary>
    public IReadOnlyList<ImageEntry> Entries => _entries;

    /// <summary>
    /// Reads the list from <paramref name="renderState"/> again if it changed since the last call, which most frames it
    /// hasn't. Returns whether it read the list.
    /// </summary>
    public bool Refresh(nint renderState)
    {
        ulong generation = 0;
        Native.RenderStateGet(renderState, RenderStateData.ImagePlacementsGeneration, &generation);
        if (_hasRead && generation == _generation)
        {
            return false;
        }

        _generation = generation;
        _hasRead = true;
        _entries.Clear();
        var iterator = _iterator;
        if (Native.RenderStateGet(renderState, RenderStateData.ImageIterator, &iterator) != GhosttyResult.Success)
        {
            return true;
        }

        ImageProtocol protocol;
        ImageLayer layer;
        uint imageId, offsetX, offsetY, width, height, sourceX, sourceY, sourceWidth, sourceHeight;
        ulong imageGeneration;
        int z, col, row;
        var values = stackalloc void*[KeyCount]
        {
            &protocol, &imageId, &imageGeneration, &layer, &z, &col, &row, &offsetX, &offsetY, &width, &height,
            &sourceX, &sourceY, &sourceWidth, &sourceHeight,
        };

        fixed (RenderStateImageData* keys = Keys)
        {
            while (Native.ImageIteratorNext(_iterator) != 0)
            {
                // Skip an entry from a protocol that a later libghostty adds.
                if (Native.ImageGetMulti(_iterator, KeyCount, keys, values, null) != GhosttyResult.Success ||
                    protocol != ImageProtocol.Kitty)
                {
                    continue;
                }

                _entries.Add(new ImageEntry(imageId, imageGeneration, layer, z, col, row, offsetX, offsetY, width, height,
                    sourceX, sourceY, sourceWidth, sourceHeight));
            }
        }

        _entries.Sort(static (a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Z.CompareTo(b.Z));
        return true;
    }

    public void Dispose()
    {
        Native.ImageIteratorFree(_iterator);
        _iterator = 0;
    }

    private static void Check(GhosttyResult result)
    {
        if (result != GhosttyResult.Success)
        {
            throw new InvalidOperationException($"libghostty-vt call failed: {result}");
        }
    }
}
