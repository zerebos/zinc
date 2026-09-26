using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Phantom.Ghostty;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Phantom.Terminal;

/// <summary>
/// One kitty graphics placement to draw this frame, in viewport terms: the image's top-left
/// is at cell (<see cref="Col"/>, <see cref="Row"/>) (the row may be negative when scrolled
/// partly off the top) plus a pixel offset, scaled to <see cref="PixelWidth"/> ×
/// <see cref="PixelHeight"/> from the given source rectangle. A <see cref="Virtual"/>
/// placement has no position: placeholder cells in the text show it, fitted to its
/// <see cref="Columns"/> × <see cref="Rows"/> grid (see <see cref="KittyPlaceholders"/>).
/// </summary>
public readonly record struct ImagePlacement(
    uint ImageId, ulong Generation, int Z,
    int Col, int Row, uint OffsetX, uint OffsetY,
    uint PixelWidth, uint PixelHeight,
    uint SourceX, uint SourceY, uint SourceWidth, uint SourceHeight,
    int ImageWidth, int ImageHeight,
    byte[]? Pixels,
    bool Virtual = false, uint PlacementId = 0, uint Columns = 0, uint Rows = 0);

/// <summary>Kitty graphics support: the PNG decoder libghostty calls, and pixel conversion.</summary>
public static unsafe class KittyImages
{
    /// <summary>Ghostty's default <c>image-storage-limit</c> (per screen).</summary>
    public const ulong StorageLimit = 320_000_000;

    private static int _installed;

    /// <summary>Installs the process-wide PNG decoder (libghostty has none of its own).</summary>
    public static void InstallPngDecoder()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 0)
        {
            Native.SysSet(SysOption.DecodePng,
                (delegate* unmanaged[Cdecl]<nint, nint, byte*, nuint, GhosttySysImage*, byte>)&DecodePng);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte DecodePng(nint userdata, nint allocator, byte* data, nuint len, GhosttySysImage* result)
    {
        try
        {
            var (width, height, rgba) = DecodePng(new ReadOnlySpan<byte>(data, (int)len).ToArray());
            var buffer = Native.Alloc(allocator, (nuint)rgba.Length);
            if (buffer == null)
            {
                return 0;
            }

            rgba.CopyTo(new Span<byte>(buffer, rgba.Length));
            *result = new GhosttySysImage { Width = width, Height = height, Data = (nint)buffer, DataLen = (nuint)rgba.Length };
            return 1;
        }
        catch (Exception ex)
        {
            Log.Write($"kitty: PNG decode failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Decodes a PNG to straight-alpha RGBA with Windows Imaging Component.</summary>
    public static (uint Width, uint Height, byte[] Rgba) DecodePng(byte[] png)
    {
        // Called on the PTY reader thread (MTA), where blocking on WinRT async is fine.
        using var stream = new InMemoryRandomAccessStream();
        stream.WriteAsync(png.AsBuffer()).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        var decoder = BitmapDecoder.CreateAsync(BitmapDecoder.PngDecoderId, stream).AsTask().GetAwaiter().GetResult();
        var pixels = decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
        return (decoder.PixelWidth, decoder.PixelHeight, pixels.DetachPixelData());
    }

    /// <summary>
    /// Converts stored image pixels (libghostty keeps RGB, RGBA, gray or gray+alpha) to
    /// premultiplied RGBA, the format GPU bitmaps take.
    /// </summary>
    public static byte[] ToPremultipliedRgba(KittyImageFormat format, ReadOnlySpan<byte> src, int pixelCount)
    {
        var dst = new byte[pixelCount * 4];
        for (var i = 0; i < pixelCount; i++)
        {
            byte r, g, b, a;
            switch (format)
            {
                case KittyImageFormat.Rgb:
                    (r, g, b, a) = (src[i * 3], src[i * 3 + 1], src[i * 3 + 2], 255);
                    break;
                case KittyImageFormat.GrayAlpha:
                    (r, a) = (src[i * 2], src[i * 2 + 1]);
                    (g, b) = (r, r);
                    break;
                case KittyImageFormat.Gray:
                    (r, g, b, a) = (src[i], src[i], src[i], 255);
                    break;
                default:
                    (r, g, b, a) = (src[i * 4], src[i * 4 + 1], src[i * 4 + 2], src[i * 4 + 3]);
                    break;
            }

            dst[i * 4] = (byte)((r * a + 127) / 255);
            dst[i * 4 + 1] = (byte)((g * a + 127) / 255);
            dst[i * 4 + 2] = (byte)((b * a + 127) / 255);
            dst[i * 4 + 3] = a;
        }

        return dst;
    }

    internal static int BytesPerPixel(KittyImageFormat format) => format switch
    {
        KittyImageFormat.Rgb => 3,
        KittyImageFormat.GrayAlpha => 2,
        KittyImageFormat.Gray => 1,
        _ => 4,
    };
}
