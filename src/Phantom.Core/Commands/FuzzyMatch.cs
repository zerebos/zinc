namespace Phantom.Commands;

/// <summary>
/// Fuzzy matching for the command palette: every query character must appear in order
/// (case-insensitive); spaces in the query are ignored, so "spl gb" works like "splgb".
/// Matches are scored so that tighter, more "intentional" matches rank higher:
/// consecutive characters, characters at word starts, and matches near the beginning.
/// </summary>
public static class FuzzyMatch
{
    private const int Base = 1, Consecutive = 5, WordStart = 8, FirstCharacter = 10;

    /// <summary>
    /// Scores <paramref name="text"/> against <paramref name="query"/>, or returns null if it
    /// doesn't match. <paramref name="positions"/> receives the matched character indices
    /// (for highlighting). An empty query matches everything with score 0.
    /// </summary>
    public static int? Score(string query, string text, out int[] positions)
    {
        var chars = query.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray();
        positions = new int[chars.Length];
        if (chars.Length == 0)
        {
            return 0;
        }

        // Greedy left-to-right, but prefer a word-start occurrence when one is available
        // before the next ordinary one would be used up — cheap and good enough for short
        // command titles.
        var score = 0;
        var t = 0;
        var previous = -2;
        for (var q = 0; q < chars.Length; q++)
        {
            var c = chars[q];
            var next = IndexOf(text, c, t);
            if (next < 0)
            {
                return null;
            }

            // If this character also appears later at a word start, and taking that one
            // still leaves the rest of the query matchable, prefer it (unless we'd lose a
            // consecutive run).
            if (next != previous + 1 && !IsWordStart(text, next))
            {
                for (var i = next + 1; i < text.Length; i++)
                {
                    if (char.ToLowerInvariant(text[i]) == c && IsWordStart(text, i) &&
                        RestMatches(chars, q + 1, text, i + 1))
                    {
                        next = i;
                        break;
                    }
                }
            }

            score += Base;
            if (next == previous + 1)
            {
                score += Consecutive;
            }

            if (IsWordStart(text, next))
            {
                score += WordStart;
            }

            if (next == 0)
            {
                score += FirstCharacter;
            }

            positions[q] = next;
            previous = next;
            t = next + 1;
        }

        // Among equal matches, prefer shorter texts.
        return score * 100 - text.Length;
    }

    private static int IndexOf(string text, char lower, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == lower)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool RestMatches(char[] chars, int from, string text, int start)
    {
        for (var q = from; q < chars.Length; q++)
        {
            start = IndexOf(text, chars[q], start);
            if (start < 0)
            {
                return false;
            }

            start++;
        }

        return true;
    }

    private static bool IsWordStart(string text, int i) =>
        i == 0 || !char.IsLetterOrDigit(text[i - 1]) ||
        (char.IsUpper(text[i]) && char.IsLower(text[i - 1]));
}
