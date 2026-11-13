using System.Text.RegularExpressions;

namespace Zinc.Terminal;

/// <summary>
/// A search hit from (Row, StartCol) through (EndRow, EndCol) inclusive, in screen rows (0 = the
/// oldest scrollback row). EndRow > Row when the match continues across a soft wrap.
/// </summary>
public readonly record struct SearchMatch(ulong Row, ushort StartCol, ulong EndRow, ushort EndCol)
{
    public SearchMatch(ulong row, ushort startCol, ushort endCol) : this(row, startCol, row, endCol)
    {
    }
}

public static class SearchPattern
{
    /// <summary>A pathological regex gives up rather than hanging the UI.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The pattern for find-box input: literal text unless <paramref name="regex"/>, ignoring
    /// case unless <paramref name="caseSensitive"/>. Null for empty input or an invalid regex
    /// (with <paramref name="error"/> set).
    /// </summary>
    public static Regex? Build(string text, bool caseSensitive, bool regex, out string? error)
    {
        error = null;
        if (text.Length == 0)
        {
            return null;
        }

        var options = RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            return new Regex(regex ? text : Regex.Escape(text), options, Timeout);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Which match to start at: the newest one at or above <paramref name="viewportBottom"/>
    /// (searching upward from what's on screen, like a shell's history search), else the newest.
    /// </summary>
    public static int Initial(IReadOnlyList<SearchMatch> matches, ulong viewportBottom)
    {
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i].Row <= viewportBottom)
            {
                return i;
            }
        }

        return matches.Count - 1;
    }
}
