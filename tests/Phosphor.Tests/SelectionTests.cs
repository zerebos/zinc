using Phosphor.Ghostty;
using Phosphor.Terminal;

namespace Phosphor.Tests;

/// <summary>SelectionGesture against a real terminal; geometry mimics an 8x16 px cell grid.</summary>
public class SelectionTests
{
    private const int CellW = 8, CellH = 16;

    private static readonly GhosttySelectionGestureGeometry Geometry = new()
    {
        Columns = 40, CellWidth = CellW, PaddingLeft = 0, ScreenHeight = 5 * CellH,
    };

    private static (TerminalSession Session, SelectionGesture Gesture) Create()
    {
        var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("hello world foo\r\nsecond line here\r\nthird");
        return (session, new SelectionGesture(session));
    }

    private static void At(SelectionGesture g, GesturePhase phase, ushort col, uint row, ulong timeNs) =>
        g.Handle(phase, col, row, col * CellW + 2, row * CellH + 8, Geometry, timeNs, repeatDistancePx: 8);

    private static void Click(SelectionGesture g, ushort col, uint row, ulong timeNs)
    {
        At(g, GesturePhase.Press, col, row, timeNs);
        At(g, GesturePhase.Release, col, row, timeNs);
    }

    [Fact]
    public void WordBoundariesDecideWhereADoubleClickedWordEnds()
    {
        var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("src/app-main.cs next");
        using (session)
        using (var g = new SelectionGesture(session))
        {
            const ulong t = 9_000_000_000;

            // Ghostty's set: '/' and '-' are part of the word.
            Click(g, 5, 0, t);
            Click(g, 5, 0, t + 100_000_000);
            Assert.Equal("src/app-main.cs", session.GetSelectionText());

            // With "/-" as boundaries, only "app".
            g.SetWordBoundaries(SelectionGesture.Boundaries("/-"));
            Click(g, 5, 0, t + 2_000_000_000);
            Click(g, 5, 0, t + 2_100_000_000);
            Assert.Equal("app", session.GetSelectionText());

            // And back.
            g.SetWordBoundaries(SelectionGesture.Boundaries(null));
            Click(g, 5, 0, t + 4_000_000_000);
            Click(g, 5, 0, t + 4_100_000_000);
            Assert.Equal("src/app-main.cs", session.GetSelectionText());
        }
    }

    [Fact]
    public void DragSelectsAcrossLines()
    {
        var (session, g) = Create();
        using (session)
        using (g)
        {
            At(g, GesturePhase.Press, 6, 0, 1_000_000_000);
            At(g, GesturePhase.Drag, 4, 1, 1_000_000_000);
            At(g, GesturePhase.Release, 4, 1, 1_000_000_000);
            Assert.Equal("world foo\nseco", session.GetSelectionText());
        }
    }

    [Fact]
    public void DoubleClickSelectsWordTripleClickSelectsLine()
    {
        var (session, g) = Create();
        using (session)
        using (g)
        {
            const ulong t = 5_000_000_000;
            Click(g, 13, 1, t);
            Assert.Null(session.GetSelectionText());

            Click(g, 13, 1, t + 100_000_000);
            Assert.Equal("here", session.GetSelectionText());

            Click(g, 13, 1, t + 200_000_000);
            Assert.Equal("second line here", session.GetSelectionText());
        }
    }

    [Fact]
    public void SlowSecondClickDoesNotSelectWord()
    {
        var (session, g) = Create();
        using (session)
        using (g)
        {
            const ulong t = 5_000_000_000;
            Click(g, 13, 1, t);
            Click(g, 13, 1, t + (ulong)(SelectionGesture.RepeatInterval.Ticks * 100) + 1);
            Assert.Null(session.GetSelectionText());
        }
    }

    [Fact]
    public void PlainClickClearsSelection()
    {
        var (session, g) = Create();
        using (session)
        using (g)
        {
            // A cell joins a drag selection once the pointer passes its midpoint; At()
            // points near each cell's left edge, so ending in column 5 selects columns 0-4.
            At(g, GesturePhase.Press, 0, 0, 1_000_000_000);
            At(g, GesturePhase.Drag, 5, 0, 1_000_000_000);
            At(g, GesturePhase.Release, 5, 0, 1_000_000_000);
            Assert.Equal("hello", session.GetSelectionText());

            Click(g, 2, 2, 9_000_000_000);
            Assert.Null(session.GetSelectionText());
        }
    }
}
