using System.Text.RegularExpressions;
using Zinc.Terminal;

namespace Zinc.Tests;

public class SearchTests
{
    private static List<SearchMatch> Find(TerminalSession session, string text, bool caseSensitive = false, bool regex = false)
    {
        var matches = new List<SearchMatch>();
        session.Search(SearchPattern.Build(text, caseSensitive, regex, out _)!, matches);
        return matches;
    }

    [Fact]
    public void RowsAreScreenRowsIncludingScrollback()
    {
        using var session = new TerminalSession(20, 5, TerminalTheme.Default);
        session.WriteToScreen("needle one\r\n\r\nplain\r\n");
        session.WriteToScreen(string.Concat(Enumerable.Range(0, 20).Select(i => $"line {i}\r\n")));
        session.WriteToScreen("last needle");

        // Row 0 scrolled into history, and row 23 is the cursor line (3 + 20 lines before it).
        Assert.Equal([new SearchMatch(0, 0, 5), new SearchMatch(23, 5, 10)], Find(session, "needle"));
        var sb = session.GetScrollbar();
        Assert.Equal(24ul, sb.Total);
    }

    [Fact]
    public void LeadingBlankRowsKeepTheirPlace()
    {
        // Full-screen apps position text with the cursor, so blank rows above must still count.
        using var session = new TerminalSession(20, 6, TerminalTheme.Default);
        session.WriteToScreen("\x1b[4;3Hneedle\x1b[6;1Hneedle");
        Assert.Equal([new SearchMatch(3, 2, 7), new SearchMatch(5, 0, 5)], Find(session, "needle"));
    }

    [Fact]
    public void WideCharactersMapToBothCells()
    {
        using var session = new TerminalSession(20, 5, TerminalTheme.Default);
        session.WriteToScreen("日本 go 語x");
        // 日=0-1 本=2-3 ' '=4 g=5 o=6 ' '=7 語=8-9 x=10
        Assert.Equal([new SearchMatch(0, 5, 6)], Find(session, "go"));
        Assert.Equal([new SearchMatch(0, 8, 10)], Find(session, "語x"));
        Assert.Equal([new SearchMatch(0, 0, 3)], Find(session, "日本"));
    }

    [Fact]
    public void MatchesContinueAcrossSoftWraps()
    {
        using var session = new TerminalSession(10, 5, TerminalTheme.Default);
        session.WriteToScreen("xxxxxxxneedle needle\r\nnext line"); // wraps after 10 columns
        // "nee|dle" spans the wrap (row 0 col 7 to row 1 col 2), and the second fits on row 1.
        Assert.Equal([new SearchMatch(0, 7, 1, 2), new SearchMatch(1, 4, 9)], Find(session, "needle"));
        // The real line break still separates lines, and "next" is on screen row 2.
        Assert.Equal([new SearchMatch(2, 0, 3)], Find(session, "next"));
        Assert.Empty(Find(session, @"needle\s*next", regex: true));
    }

    [Fact]
    public void WideCharactersWrappedToTheNextRowDontSplitWords()
    {
        // 5 columns: "abcd" + 語 doesn't fit, so 語 moves to row 1 leaving a gap at row 0 col 4.
        using var session = new TerminalSession(5, 4, TerminalTheme.Default);
        session.WriteToScreen("abcd語z");
        Assert.Equal([new SearchMatch(0, 3, 1, 2)], Find(session, "d語z"));
    }

    [Fact]
    public void CaseAndRegexOptions()
    {
        using var session = new TerminalSession(40, 5, TerminalTheme.Default);
        session.WriteToScreen("Error: disk 42 error 7");

        Assert.Equal(2, Find(session, "error").Count);
        Assert.Single(Find(session, "error", caseSensitive: true));
        Assert.Equal([new SearchMatch(0, 12, 13), new SearchMatch(0, 21, 21)], Find(session, @"\d+", regex: true));
        Assert.Empty(Find(session, @"\d+")); // literal unless regex
        Assert.Equal([new SearchMatch(0, 21, 21)], Find(session, @"\d$", regex: true)); // $ = end of text, not of the row
        Assert.Equal([new SearchMatch(0, 0, 4)], Find(session, @"^\w+", regex: true));
    }

    [Fact]
    public void InvalidRegexReportsAnError()
    {
        Assert.Null(SearchPattern.Build("(", false, true, out var error));
        Assert.NotNull(error);
        Assert.Null(SearchPattern.Build("", false, false, out error));
        Assert.Null(error);
        Assert.NotNull(SearchPattern.Build("(", false, false, out _)); // literal "(" is fine
    }

    [Fact]
    public void InitialMatchIsTheNewestOnOrAboveTheViewport()
    {
        var matches = new[] { new SearchMatch(2, 0, 0), new SearchMatch(10, 0, 0), new SearchMatch(50, 0, 0) };
        Assert.Equal(1, SearchPattern.Initial(matches, viewportBottom: 20));
        Assert.Equal(2, SearchPattern.Initial(matches, viewportBottom: 60));
        Assert.Equal(2, SearchPattern.Initial(matches, viewportBottom: 1)); // none above: newest
    }
}
