using System.Text.RegularExpressions;

namespace Phantom.Terminal;

/// <summary>
/// A link under the pointer: an OSC 8 hyperlink (<see cref="Explicit"/>) or a URL found in
/// the text. Covers viewport row <see cref="Row"/>, columns <see cref="StartCol"/> through
/// <see cref="EndCol"/> inclusive.
/// </summary>
public readonly record struct LinkMatch(string Uri, uint Row, ushort StartCol, ushort EndCol, bool Explicit);

/// <summary>Finds plain-text URLs in a row of terminal text.</summary>
public static partial class UrlDetector
{
    // Stops at whitespace, quotes, angle brackets and backticks; trailing punctuation is
    // trimmed afterwards so "see https://x.org/a." doesn't take the period.
    [GeneratedRegex("""(?:https?|ftp|file)://[^\s<>"'`]+|mailto:[^\s<>"'`]+""", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    /// <summary>The URL covering <paramref name="index"/> in <paramref name="text"/>, if any.</summary>
    public static (int Start, int Length)? Find(string text, int index)
    {
        foreach (Match m in Url().Matches(text))
        {
            var length = TrimmedLength(text, m.Index, m.Length);
            if (index >= m.Index && index < m.Index + length)
            {
                return (m.Index, length);
            }
        }

        return null;
    }

    private static int TrimmedLength(string text, int start, int length)
    {
        while (length > 0)
        {
            var last = text[start + length - 1];
            if (".,;:!?".Contains(last))
            {
                length--;
            }
            else if (last is ')' or ']' or '}' && !Balanced(text.AsSpan(start, length), last))
            {
                // "(see https://x.org/a)" keeps the parenthesis out; Wikipedia-style
                // "https://x.org/A_(b)" keeps it.
                length--;
            }
            else
            {
                break;
            }
        }

        return length;
    }

    private static bool Balanced(ReadOnlySpan<char> url, char close)
    {
        var open = close switch { ')' => '(', ']' => '[', _ => '{' };
        var depth = 0;
        foreach (var ch in url)
        {
            if (ch == open) depth++;
            else if (ch == close) depth--;
        }

        return depth >= 0;
    }
}

public enum LinkAction
{
    /// <summary>Hand the URI to its default handler (browser, mail client).</summary>
    Open,

    /// <summary>Show the local path in Explorer (select a file, open a folder); never run it.</summary>
    Reveal,

    /// <summary>Do nothing: an unknown scheme or a file on another host.</summary>
    Reject,
}

public static class LinkTarget
{
    private static readonly string[] OpenSchemes = ["http", "https", "mailto", "ftp"];

    /// <summary>
    /// What Ctrl+click on <paramref name="uri"/> may do. Links come from whatever a program
    /// prints, so only web/mail schemes open directly; file links are revealed rather than
    /// opened, so a link can't launch an executable.
    /// </summary>
    public static LinkAction Classify(string uri, out string target)
    {
        target = uri;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return LinkAction.Reject;
        }

        if (OpenSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            target = parsed.AbsoluteUri;
            return LinkAction.Open;
        }

        if (parsed.IsFile && parsed.Host.Length == 0)
        {
            target = parsed.LocalPath;
            return LinkAction.Reveal;
        }

        if (parsed.IsFile && parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            // LocalPath would be the UNC path \\localhost\C:\...; the path is what's meant.
            target = Uri.UnescapeDataString(parsed.AbsolutePath).TrimStart('/').Replace('/', '\\');
            return LinkAction.Reveal;
        }

        return LinkAction.Reject;
    }
}
