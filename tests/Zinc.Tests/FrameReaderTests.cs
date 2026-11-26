using Zinc.Ghostty;
using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>What a renderer gets to draw: each cell's text, flags, and the colors it ends up with.</summary>
public class FrameReaderTests
{
    private static (TerminalSession Session, FrameReader Reader) Frame(string output, Config? config = null, ushort cols = 20, ushort rows = 4)
    {
        var session = new TerminalSession(cols, rows, TerminalTheme.Default);
        session.WriteToScreen(output);
        var reader = new FrameReader { Config = config ?? new Config() };
        Assert.True(reader.Update(session));
        reader.Read(rows, cols);
        return (session, reader);
    }

    private static GhosttyColorRgb Palette(FrameReader reader, int index) =>
        new(reader.Palette[index * 3], reader.Palette[index * 3 + 1], reader.Palette[index * 3 + 2]);

    private static void Same(GhosttyColorRgb expected, GhosttyColorRgb actual) =>
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));

    [Fact]
    public void ReadsTextStylesAndResolvedColors()
    {
        var (session, reader) = Frame("a\e[1;3;31mb\e[0;4:3;9;53mc\e[0;48;2;10;20;30md");
        using (session)
        using (reader)
        {
            Assert.Equal(4, reader.Rows);
            var row = reader.Row(0);
            Assert.Equal(20, row.Length);

            Assert.Equal(['a'], reader.Text(row[0]).ToArray());
            Assert.Equal(FrameCellFlags.None, row[0].Flags);
            Same(reader.Foreground, row[0].Fg);
            Assert.False(row[0].HasBg);

            Assert.Equal(FrameCellFlags.Bold | FrameCellFlags.Italic, row[1].Flags);
            Assert.Equal(3, row[1].FontVariant);
            Same(Palette(reader, 1), row[1].Fg);

            Assert.Equal(FrameCellFlags.Strikethrough | FrameCellFlags.Overline, row[2].Flags);
            Assert.Equal(3, row[2].Underline); // curly

            Assert.True(row[3].HasBg);
            Same(new GhosttyColorRgb(10, 20, 30), row[3].Bg);

            Assert.Equal(0, reader.Text(row[4]).Length); // nothing written there
        }
    }

    [Fact]
    public void AWideCharacterTakesTwoCellsAndAClusterStaysInOne()
    {
        var (session, reader) = Frame("日éx");
        using (session)
        using (reader)
        {
            var row = reader.Row(0);
            Assert.Equal(FrameCellFlags.Wide, row[0].Flags);
            Assert.Equal([0x65E5u], reader.Text(row[0]).ToArray());
            Assert.Equal(FrameCellFlags.SpacerTail, row[1].Flags);
            Assert.Equal(['e', 0x301u], reader.Text(row[2]).ToArray());
            Assert.Equal(['x'], reader.Text(row[3]).ToArray());
        }
    }

    [Fact]
    public void InverseSwapsTheColorsAndFillsInTheDefaultBackground()
    {
        var (session, reader) = Frame("\e[7ma\e[0;7;32;44mb");
        using (session)
        using (reader)
        {
            var row = reader.Row(0);
            Same(reader.Background, row[0].Fg);
            Same(reader.Foreground, row[0].Bg);
            Assert.True(row[0].HasBg);
            Same(Palette(reader, 4), row[1].Fg);
            Same(Palette(reader, 2), row[1].Bg);
        }
    }

    [Fact]
    public void BoldIsBrightAndBoldColorOnlyChangeTheColorsTheyAreFor()
    {
        const string Output = "\e[1;31ma\e[0;1mb\e[0;1;38;2;1;2;3mc\e[0;31md";
        var bold = new GhosttyColorRgb(200, 100, 50);
        var (session, reader) = Frame(Output, new Config { BoldIsBright = true, BoldColor = bold });
        using (session)
        using (reader)
        {
            var row = reader.Row(0);
            Same(Palette(reader, 9), row[0].Fg); // a palette color from the first eight
            Same(bold, row[1].Fg);               // the default color
            Same(new GhosttyColorRgb(1, 2, 3), row[2].Fg); // an RGB color is left alone
            Same(Palette(reader, 1), row[3].Fg); // and so is text that isn't bold
        }

        var (plainSession, plain) = Frame(Output);
        using (plainSession)
        using (plain)
        {
            Same(Palette(plain, 1), plain.Row(0)[0].Fg);
            Same(plain.Foreground, plain.Row(0)[1].Fg);
        }
    }

    [Fact]
    public void FaintBlendsTowardTheBackgroundByFaintOpacity()
    {
        var (session, reader) = Frame("\e[2;38;2;200;100;0;48;2;0;0;100ma", new Config { FaintOpacity = 0.25 });
        using (session)
        using (reader)
        {
            // A quarter of the way from the background to the foreground.
            Same(new GhosttyColorRgb(50, 25, 75), reader.Row(0)[0].Fg);
        }
    }

    [Fact]
    public void MinimumContrastMovesAForegroundThatIsTooCloseToItsBackground()
    {
        const string Output = "\e[38;2;20;20;20;48;2;0;0;0ma";
        var (session, reader) = Frame(Output, new Config { MinimumContrast = 4.5 });
        using (session)
        using (reader)
        {
            var cell = reader.Row(0)[0];
            Assert.True(MinimumContrast.Ratio((cell.Fg.R, cell.Fg.G, cell.Fg.B), (0, 0, 0)) >= 4.5);
        }

        var (plainSession, plain) = Frame(Output);
        using (plainSession)
        using (plain)
        {
            Same(new GhosttyColorRgb(20, 20, 20), plain.Row(0)[0].Fg);
        }
    }

    [Fact]
    public void ASelectionUsesTheThemesColorsOrElseInverseVideo()
    {
        const uint CellW = 8, CellH = 16;
        var geometry = new GhosttySelectionGestureGeometry { Columns = 20, CellWidth = CellW, PaddingLeft = 0, ScreenHeight = 4 * CellH };

        static void Select(TerminalSession session, GhosttySelectionGestureGeometry geometry)
        {
            using var gesture = new SelectionGesture(session);
            gesture.Handle(GesturePhase.Press, 1, 0, 1 * CellW + 2, 8, geometry, 1_000_000_000, repeatDistancePx: 8);
            gesture.Handle(GesturePhase.Drag, 2, 0, 2 * CellW + 6, 8, geometry, 1_100_000_000, repeatDistancePx: 8);
            gesture.Handle(GesturePhase.Release, 2, 0, 2 * CellW + 6, 8, geometry, 1_200_000_000, repeatDistancePx: 8);
        }

        using var session = new TerminalSession(20, 4, TerminalTheme.Default);
        session.WriteToScreen("abcd");
        Select(session, geometry);
        Assert.Equal("bc", session.GetSelectionText());

        using var themed = new FrameReader();
        Assert.True(themed.Update(session));
        themed.Read(4, 20);
        var row = themed.Row(0);
        Assert.Equal(FrameCellFlags.None, row[0].Flags);
        Assert.Equal(FrameCellFlags.Selected, row[1].Flags);
        Assert.Equal(FrameCellFlags.Selected, row[2].Flags);
        Assert.Equal(FrameCellFlags.None, row[3].Flags);
        Same(TerminalTheme.Default.SelectionBackground!.Value, row[1].Bg);
        Same(TerminalTheme.Default.SelectionForeground!.Value, row[1].Fg);

        // A scheme with no selection colors shows the selection as inverse video.
        using var bare = new FrameReader { Config = new Config { Theme = TerminalTheme.SchemeBase } };
        Assert.True(bare.Update(session));
        bare.Read(4, 20);
        Same(bare.Background, bare.Row(0)[1].Fg);
        Same(bare.Foreground, bare.Row(0)[1].Bg);
    }

    [Fact]
    public void ReportsTheCursorAndStopsAtTheRowsAskedFor()
    {
        var (session, reader) = Frame("one\r\ntwo\r\nthr");
        using (session)
        using (reader)
        {
            Assert.Equal(2, reader.Cursor.ViewportY);
            Assert.Equal(3, reader.Cursor.ViewportX);
            Assert.NotEqual(0, reader.Cursor.Visible);

            reader.Read(2, 20);
            Assert.Equal(2, reader.Rows);
            reader.Clean();
        }
    }

    [Fact]
    public void AClosedSessionHasNothingToRead()
    {
        var session = new TerminalSession(20, 4, TerminalTheme.Default);
        using var reader = new FrameReader();
        session.Dispose();
        Assert.False(reader.Update(session));
    }
}
