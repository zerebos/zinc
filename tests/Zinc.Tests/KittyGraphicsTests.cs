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
        $"\e_G{control};{Convert.ToBase64String(payload)}\e\\";

    /// <summary>The images a render state updated from <paramref name="session"/> says to draw now.</summary>
    private static List<ImageEntry> Images(TerminalSession session)
    {
        Native.RenderStateNew(0, out var state);
        try
        {
            using var list = new ImageList();
            lock (session.Lock)
            {
                Assert.True(session.UpdateRenderState(state));
                list.Refresh(state);
            }

            return [.. list.Entries];
        }
        finally
        {
            Native.RenderStateFree(state);
        }
    }

    [Fact]
    public void QueryIsAnswered()
    {
        using var session = NewSession();
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;

        // How programs (yazi, chafa, timg) detect support.
        session.WriteToScreen("\e_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\e\\");
        Assert.Contains("\e_Gi=31;OK\e\\", string.Concat(replies.Select(Encoding.UTF8.GetString)));
    }

    [Fact]
    public void RawRgbImageIsPlacedAtTheCursor()
    {
        using var session = NewSession();
        session.WriteToScreen("ab");
        session.WriteToScreen(Apc("a=T,f=24,s=2,v=1,i=7,q=2", [255, 0, 0, 0, 255, 0]));

        var p = Assert.Single(Images(session));
        Assert.Equal((7u, 2, 0), (p.ImageId, p.Col, p.Row));
        Assert.Equal((2u, 1u), (p.PixelWidth, p.PixelHeight));
        Assert.Equal((0u, 0u, 2u, 1u), (p.SourceX, p.SourceY, p.SourceWidth, p.SourceHeight));
        Assert.Equal((ImageLayer.AboveText, 0), (p.Layer, p.Z));

        var pixels = session.CopyImagePixels(7)!;
        Assert.Equal((2, 1, p.Generation), (pixels.Width, pixels.Height, pixels.Generation));
        Assert.Equal([255, 0, 0, 255, 0, 255, 0, 255], pixels.Rgba);
        Assert.Null(session.CopyImagePixels(8));
    }

    [Fact]
    public async Task PngImagesAreDecoded()
    {
        using var session = NewSession();
        var png = await EncodePng(2, 1, [10, 20, 30, 255, 200, 100, 0, 128]);
        session.WriteToScreen(Apc("a=T,f=100,i=9,q=2", png));

        Assert.Single(Images(session));
        var pixels = session.CopyImagePixels(9)!;
        Assert.Equal((2, 1), (pixels.Width, pixels.Height));
        // The second pixel is half transparent, so it comes back premultiplied.
        Assert.Equal([10, 20, 30, 255, 100, 50, 0, 128], pixels.Rgba);
    }

    [Fact]
    public void DeletedImagesDisappearAndScrollingMovesThem()
    {
        using var session = NewSession();
        session.WriteToScreen(Apc("a=T,f=24,s=1,v=1,i=3,q=2", [1, 2, 3]));
        Assert.Single(Images(session));

        session.WriteToScreen("\r\n\r\n");
        session.ScrollLines(-1); // not scrolled: no scrollback yet
        session.WriteToScreen(string.Concat(Enumerable.Repeat("\r\n", 30)));
        Assert.Empty(Images(session)); // scrolled out of the viewport

        session.ScrollViewport(new GhosttyTerminalScrollViewport { Tag = ScrollViewportTag.Top });
        Assert.Equal(0, Assert.Single(Images(session)).Row); // back in view from scrollback

        session.WriteToScreen("\e_Ga=d,d=I,i=3,q=2\e\\"); // (d=A spares it: only deletes what's on screen)
        Assert.Empty(Images(session));
    }

    [Fact]
    public void PlaceholderCellsBecomeOneEntryForEachRow()
    {
        // This is the form yazi uses. It transmits an image of 30x40 pixels with a virtual placement (U=1) of 3x2 cells,
        // which the image fills exactly at 10x20 pixels a cell. Then it prints the placeholder cells. Their foreground
        // color names the image (87576 is 0x015618), their underline color names the placement, and their diacritics
        // give the row and the column. A cell without diacritics continues the cell to its left.
        using var session = NewSession();
        session.WriteToScreen(Apc("a=T,U=1,f=24,s=30,v=40,i=87576,p=4,c=3,r=2,q=2", new byte[30 * 40 * 3]));
        Assert.Empty(Images(session)); // a virtual placement shows only where its cells are

        const string cell = "\U0010EEEE";
        const string row0 = "\u0305", row1 = "\u030D", col0 = "\u0305";
        session.WriteToScreen("\e[38;2;1;86;24m\e[58;5;4m");
        session.WriteToScreen($"{cell}{row0}{col0}{cell}{cell}\r\n{cell}{row1}{col0}{cell}{cell}\e[m");

        var entries = Images(session);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal((87576u, ImageLayer.BelowText, -1), (e.ImageId, e.Layer, e.Z)));
        var (top, bottom) = (entries.Single(e => e.Row == 0), entries.Single(e => e.Row == 1));
        Assert.Equal((0, 30u, 20u), (top.Col, top.PixelWidth, top.PixelHeight));
        Assert.Equal((0u, 0u, 30u, 20u), (top.SourceX, top.SourceY, top.SourceWidth, top.SourceHeight));
        Assert.Equal((0u, 20u, 30u, 20u), (bottom.SourceX, bottom.SourceY, bottom.SourceWidth, bottom.SourceHeight));
    }

    [Fact]
    public void LayersFollowTheZIndexAndOrderTheList()
    {
        using var session = NewSession();
        session.WriteToScreen(Apc("a=T,f=24,s=1,v=1,i=1,z=5,q=2", [1, 1, 1]));
        session.WriteToScreen(Apc("a=T,f=24,s=1,v=1,i=2,z=-1,q=2", [2, 2, 2]));
        session.WriteToScreen(Apc("a=T,f=24,s=1,v=1,i=3,z=-1073741825,q=2", [3, 3, 3]));
        session.WriteToScreen(Apc("a=T,f=24,s=1,v=1,i=4,z=0,q=2", [4, 4, 4]));

        var entries = Images(session);
        Assert.Equal([3u, 2u, 4u, 1u], entries.Select(e => e.ImageId));
        Assert.Equal([ImageLayer.BelowBackground, ImageLayer.BelowText, ImageLayer.AboveText, ImageLayer.AboveText],
            entries.Select(e => e.Layer));
    }

    [Fact]
    public void TheListIsReadAgainOnlyWhenItChanged()
    {
        using var session = NewSession();
        Native.RenderStateNew(0, out var state);
        try
        {
            using var list = new ImageList();
            bool Refresh()
            {
                lock (session.Lock)
                {
                    session.UpdateRenderState(state);
                    return list.Refresh(state);
                }
            }

            Assert.True(Refresh()); // the first read always reads
            session.WriteToScreen(Apc("a=T,f=24,s=1,v=1,i=1,q=2", [1, 1, 1]));
            Assert.True(Refresh());
            Assert.Single(list.Entries);

            session.WriteToScreen("plain text");
            Assert.False(Refresh());
            Assert.Single(list.Entries);
        }
        finally
        {
            Native.RenderStateFree(state);
        }
    }

    [Fact]
    public void AnImageSentAgainKeepsTheOldOneUntilTheNewOneHasArrived()
    {
        // terminal-browser sends its whole page as image 1 again on every frame, in chunks. libghostty keeps showing
        // the old image until the last chunk of the new one arrives, so the page never flashes empty.
        using var session = NewSession();
        var pixels = new byte[2 * 3 * 4];
        session.WriteToScreen(Apc("a=T,f=32,s=2,v=3,i=1,p=1,q=2", pixels));
        var first = Assert.Single(Images(session)).Generation;

        session.WriteToScreen(Apc("a=T,f=32,s=2,v=3,i=1,p=1,q=2,m=1", pixels[..12]));
        Assert.Equal(first, Assert.Single(Images(session)).Generation);
        Assert.Equal(first, session.CopyImagePixels(1)!.Generation);

        session.WriteToScreen(Apc("m=0", pixels[12..]));
        Assert.NotEqual(first, Assert.Single(Images(session)).Generation);
    }

    [Fact]
    public void AnimationsMoveToTheirNextFrame()
    {
        // A red root frame and a green second frame, 50 ms each, looping.
        using var session = NewSession();
        Assert.Null(session.TickAnimations(0)); // nothing stored yet
        session.WriteToScreen(Apc("a=T,f=24,s=1,v=1,i=5,q=2", [255, 0, 0]));
        session.WriteToScreen(Apc("a=f,f=24,s=1,v=1,i=5,z=50,q=2", [0, 255, 0]));
        Assert.Null(session.TickAnimations(1000)); // stored, but not playing

        session.WriteToScreen("\e_Ga=a,i=5,r=1,z=50,q=2\e\\"); // the root frame's gap
        session.WriteToScreen("\e_Ga=a,i=5,s=3,v=1,q=2\e\\"); // play, looping forever
        var first = session.CopyImagePixels(5)!;
        var now = session.TickAnimations(1000) ?? throw new Xunit.Sdk.XunitException("the animation didn't start");
        Assert.InRange(now, 1001ul, 1050ul);

        // A tick at the time the previous tick returned shows the next frame, which has a new generation.
        var generations = new HashSet<ulong> { first.Generation };
        var reds = new List<byte> { first.Rgba[0] };
        for (var i = 0; i < 3; i++)
        {
            now = session.TickAnimations(now) ?? throw new Xunit.Sdk.XunitException("the animation stopped");
            var frame = session.CopyImagePixels(5)!;
            Assert.True(generations.Add(frame.Generation));
            reds.Add(frame.Rgba[0]);
        }

        Assert.Equal([255, 0, 255, 0], reds);
    }

    [Fact]
    public void AFileQueryForAPathZincCantOpenFails()
    {
        // presenterm in WSL asks whether the terminal can load an image from a file, with a path in WSL's own /tmp, and
        // it falls back to sending the data only when the terminal says no.
        using var session = NewSession();
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;
        session.WriteToScreen(Apc("f=32,a=q,t=f,i=513988419,s=1,v=1", Encoding.UTF8.GetBytes("/tmp/.tmp4Pt8bU")));
        var reply = string.Concat(replies.Select(Encoding.UTF8.GetString));
        Assert.StartsWith("\e_Gi=513988419;", reply);
        Assert.DoesNotContain(";OK", reply);
    }

    [Fact]
    public async Task ImagesSentAsAFilePathAreLoaded()
    {
        // fastfetch's kitty-direct and presenterm's kitty-local send a path to a PNG (t=f) instead of its data.
        using var session = NewSession();
        var path = Path.Combine(Path.GetTempPath(), $"zinc-kitty-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, await EncodePng(2, 1, [10, 20, 30, 255, 40, 50, 60, 255]));
        try
        {
            session.WriteToScreen(Apc("a=T,f=100,t=f,i=11,q=2", Encoding.UTF8.GetBytes(path)));
            var entry = Assert.Single(Images(session));
            Assert.Equal(11u, entry.ImageId);
            Assert.Equal([10, 20, 30, 255, 40, 50, 60, 255], session.CopyImagePixels(11)!.Rgba);
        }
        finally
        {
            File.Delete(path);
        }
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
