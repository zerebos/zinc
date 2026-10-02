using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Zinc.Ghostty;
using Zinc.Terminal;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Zinc.Tests;

public class KittyGraphicsTests
{
    private static TerminalSession NewSession()
    {
        var session = new TerminalSession(80, 24, TerminalTheme.Default);
        session.Resize(80, 24, 10, 20); // placements need a cell size in pixels
        return session;
    }

    private static string Apc(string control, byte[] payload) =>
        $"\x1b_G{control};{Convert.ToBase64String(payload)}\x1b\\";

    private static List<ImagePlacement> Collect(TerminalSession session, bool pixels = true)
    {
        var list = new List<ImagePlacement>();
        session.CollectImages(list, (_, _) => pixels);
        return list;
    }

    [Fact]
    public void QueryIsAnswered()
    {
        using var session = NewSession();
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;

        // How programs (yazi, chafa, timg) detect support.
        session.WriteToScreen("\x1b_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\x1b\\");
        Assert.Contains("\x1b_Gi=31;OK\x1b\\", string.Concat(replies.Select(Encoding.UTF8.GetString)));
    }

    [Fact]
    public void RawRgbImageIsPlacedAtTheCursor()
    {
        using var session = NewSession();
        session.WriteToScreen("ab");
        session.WriteToScreen(Apc("a=T,f=24,s=2,v=1,i=7,q=2", [255, 0, 0, 0, 255, 0]));

        var p = Assert.Single(Collect(session));
        Assert.Equal((7u, 2, 0), (p.ImageId, p.Col, p.Row));
        Assert.Equal((2, 1), (p.ImageWidth, p.ImageHeight));
        Assert.Equal((2u, 1u), (p.PixelWidth, p.PixelHeight));
        Assert.Equal([255, 0, 0, 255, 0, 255, 0, 255], p.Pixels);

        // Cached by the caller: no pixel copy.
        Assert.Null(Assert.Single(Collect(session, pixels: false)).Pixels);
    }

    [Fact]
    public async Task PngImagesAreDecoded()
    {
        using var session = NewSession();
        var png = await EncodePng(2, 1, [10, 20, 30, 255, 200, 100, 0, 128]);
        session.WriteToScreen(Apc("a=T,f=100,i=9,q=2", png));

        var p = Assert.Single(Collect(session));
        Assert.Equal((2, 1), (p.ImageWidth, p.ImageHeight));
        // Second pixel is half transparent: premultiplied.
        Assert.Equal([10, 20, 30, 255, 100, 50, 0, 128], p.Pixels);
    }

    [Fact]
    public void DeletedImagesDisappearAndScrollingMovesThem()
    {
        using var session = NewSession();
        session.WriteToScreen(Apc("a=T,f=24,s=1,v=1,i=3,q=2", [1, 2, 3]));
        Assert.Single(Collect(session));

        session.WriteToScreen("\r\n\r\n");
        session.ScrollLines(-1); // not scrolled: no scrollback yet
        session.WriteToScreen(string.Concat(Enumerable.Repeat("\r\n", 30)));
        Assert.Empty(Collect(session)); // scrolled out of the viewport

        session.ScrollViewport(new GhosttyTerminalScrollViewport { Tag = ScrollViewportTag.Top });
        Assert.Equal(0, Assert.Single(Collect(session)).Row); // back in view from scrollback

        session.WriteToScreen("\x1b_Ga=d,d=I,i=3,q=2\x1b\\"); // (d=A spares it: only deletes what's on screen)
        Assert.Empty(Collect(session));
    }

    [Fact]
    public void VirtualPlacementsAreReportedWithTheirGrid()
    {
        using var session = NewSession();
        // yazi's form: transmit and create a virtual placement (U=1) of 3x2 cells.
        session.WriteToScreen(Apc("a=T,U=1,f=24,s=1,v=1,i=87576,p=4,c=3,r=2,q=2", [9, 9, 9]));

        var p = Assert.Single(Collect(session));
        Assert.True(p.Virtual);
        Assert.Equal((87576u, 4u, 3u, 2u), (p.ImageId, p.PlacementId, p.Columns, p.Rows));
        Assert.NotNull(p.Pixels);
    }

    [Fact]
    public void PremultipliesEveryFormat()
    {
        Assert.Equal([1, 2, 3, 255], KittyImages.ToPremultipliedRgba(KittyImageFormat.Rgb, [1, 2, 3], 1));
        Assert.Equal([100, 100, 100, 128], KittyImages.ToPremultipliedRgba(KittyImageFormat.GrayAlpha, [200, 128], 1));
        Assert.Equal([7, 7, 7, 255], KittyImages.ToPremultipliedRgba(KittyImageFormat.Gray, [7], 1));
        Assert.Equal([0, 0, 0, 0], KittyImages.ToPremultipliedRgba(KittyImageFormat.Rgba, [255, 255, 255, 0], 1));
    }

    private static async Task<byte[]> EncodePng(uint width, uint height, byte[] rgba)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, width, height, 96, 96, rgba);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }
}
