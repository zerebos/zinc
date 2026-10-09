using Zinc.Ghostty;
using Zinc.Terminal;

namespace Zinc.Tests;

public class ClipboardHtmlTests
{
    private static readonly GhosttySelectionGestureGeometry Geometry = new() { Columns = 40, CellWidth = 8, PaddingLeft = 0, ScreenHeight = 5 * 16 };

    private static string? CopyAsHtml(string output, ushort toCol, uint toRow)
    {
        using var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen(output);
        using var g = new SelectionGesture(session);
        g.Handle(GesturePhase.Press, 0, 0, 2, 8, Geometry, 1_000_000_000, repeatDistancePx: 8);
        g.Handle(GesturePhase.Drag, toCol, toRow, toCol * 8 + 2, toRow * 16 + 8, Geometry, 1_000_000_000, repeatDistancePx: 8);
        return session.GetSelectionHtml("Cascadia Mono NF", 10.5);
    }

    [Fact]
    public void ColorsAreWrittenOutAndTheBlockHasTheTerminalsLook()
    {
        var html = CopyAsHtml("\u001b[31mred\u001b[0m \u001b[1;38;2;10;200;30mbold\u001b[0m \u001b[4;44mund\u001b[0m", 15, 0)!;

        Assert.DoesNotContain("<style>", html);
        Assert.DoesNotContain("var(", html);
        Assert.DoesNotContain("display: inline;", html);
        Assert.StartsWith("<div style=\"display: inline-block; white-space: pre; font-family: 'Cascadia Mono NF', Consolas, monospace; font-size: 10.5pt; " +
            "color: #cfddd3; background-color: #0c110e;", html); // Lit Cell's text and background
        Assert.Contains("<span style=\"color: #ff6b6b;\">red</span>", html); // palette 1 of the scheme in force
        Assert.Contains("<span style=\"color: rgb(10, 200, 30);font-weight: bold;\">bold</span>", html);
        Assert.Contains("background-color: #6fb3f2;text-decoration-line: underline;", html);
        Assert.EndsWith("</div>", html);
    }

    [Fact]
    public void ReverseVideoSwapsTheColorsAndLinesBreakWithBr()
    {
        var html = CopyAsHtml("one\r\ntwo \u001b[7mrev\u001b[0m \u001b[7;31;42mboth\u001b[0m", 15, 1)!;

        Assert.Contains("one<br>two", html);
        Assert.DoesNotContain("\n", html);
        Assert.DoesNotContain("invert", html);
        Assert.Contains("<span style=\"color: #0c110e;background-color: #cfddd3;\">rev</span>", html); // default colors swapped
        Assert.Contains("<span style=\"color: #3df2a0;background-color: #ff6b6b;\">both</span>", html); // red on green, swapped
    }

    [Fact]
    public void TextIsEscapedAndNothingSelectedIsNull()
    {
        Assert.Contains("&lt;a&amp;b&gt;", CopyAsHtml("<a&b> end", 9, 0));

        using var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("nothing selected");
        Assert.Null(session.GetSelectionHtml("x", 10));
    }

    [Fact]
    public void APageBoundaryIsALineBreak()
    {
        // As libghostty writes a selection over two pages of its memory (checked against the DLL: 3000 lines of scrollback).
        const string page = "<div style=\"font-family: monospace; white-space: pre;\">";
        var html = ClipboardHtml.Make(
            "<style>:root{--vt-palette-1: #ff0000;}</style>" + page + "<div style=\"display: inline;color: var(--vt-palette-1);\">line 1</div></div>" +
            page + "<div style=\"display: inline;color: var(--vt-palette-1);\">line 2</div></div>", default, default, "x", 10);

        Assert.Contains("line 1</span><br><span style=\"color: #ff0000;\">line 2", html);
        Assert.DoesNotContain("font-family: monospace", html);
        Assert.EndsWith("line 2</span></div>", html);
    }

    [Fact]
    public void PaletteNamesInTheTextStayText()
    {
        var html = CopyAsHtml("\u001b[31mvar(--vt-palette-1)\u001b[0m\r\n--vt-palette-1: #000000", 30, 1)!;

        Assert.Contains("<span style=\"color: #ff6b6b;\">var(--vt-palette-1)</span><br>--vt-palette-1: #000000", html);
    }

    [Fact]
    public void QuotesInTheFontNameCantBreakTheStyle() =>
        Assert.Contains("font-family: 'My Font', Consolas",
            ClipboardHtml.Make("<div style=\"font-family: monospace; white-space: pre;\">x</div>", default, default, "My 'Font\"", 10)); // quotes dropped
}
