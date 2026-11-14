namespace Zinc.Settings;

/// <summary>
/// Code spans in a settings description, written in backticks as in Markdown (`` `none` ``). Settings draws them in a
/// monospace font, and plain text (search, the config schema's plain description) drops the backticks. A backtick with
/// no partner, like the one in "Win+`", stays as it is.
/// </summary>
public static class CodeText
{
    /// <summary>The text in pieces, each marked as code or not, with the backticks of each code span removed.</summary>
    public static IEnumerable<(string Text, bool IsCode)> Split(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var open = text.IndexOf('`', start);
            var close = open < 0 ? -1 : text.IndexOf('`', open + 1);
            if (close < 0)
            {
                yield return (text[start..], false);
                yield break;
            }

            if (open > start)
            {
                yield return (text[start..open], false);
            }

            if (close > open + 1)
            {
                yield return (text[(open + 1)..close], true);
            }

            start = close + 1;
        }
    }

    /// <summary>The text without its code spans' backticks.</summary>
    public static string Plain(string text) =>
        text.Contains('`') ? string.Concat(Split(text).Select(p => p.Text)) : text;
}
