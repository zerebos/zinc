using System.Text.RegularExpressions;

namespace Phosphor.Terminal;

/// <summary>
/// A link under the pointer: an OSC 8 hyperlink (<see cref="Explicit"/>) or a URL found in
/// the text. It runs from (<see cref="Row"/>, <see cref="StartCol"/>) to (<see cref="EndRow"/>,
/// <see cref="EndCol"/>) inclusive, in viewport rows: more than one when it continues across
/// soft wraps, and a row above the viewport (negative) when it started in the scrollback.
/// </summary>
public readonly record struct LinkMatch(string Uri, int Row, ushort StartCol, int EndRow, ushort EndCol, bool Explicit)
{
    /// <summary>The columns the link covers on viewport row <paramref name="row"/> of a <paramref name="cols"/>-wide screen, if any.</summary>
    public (ushort First, ushort Last)? ColumnsOn(int row, int cols) =>
        row < Row || row > EndRow ? null : (row == Row ? StartCol : (ushort)0, row == EndRow ? EndCol : (ushort)(cols - 1));
}

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

/// <summary>
/// Where a pane's file links point. Shells name this machine as the host but use their own
/// paths: Git Bash/MSYS2 write <c>/c/Users/..</c> (and <c>/usr/..</c> under
/// <see cref="PosixRoot"/>); WSL writes Linux paths inside <see cref="WslDistro"/>, with Windows
/// drives under <see cref="WslMountRoot"/> (<see cref="WslConf.MountRoot"/>: <c>/mnt/</c> unless its
/// wsl.conf says otherwise; null when it doesn't mount them).
/// </summary>
public sealed record LinkContext(string MachineName, string? WslDistro = null, string? PosixRoot = null,
    string? WslMountRoot = WslConf.DefaultMountRoot)
{
    public static LinkContext Windows { get; } = new(Environment.MachineName);
}

public static partial class LinkTarget
{
    private static readonly string[] OpenSchemes = ["http", "https", "mailto", "ftp"];

    // "/c", "/c/rest" (MSYS, Git Bash) or "/cygdrive/c/rest". (WSL's drives: WslConf.WindowsPath.)
    [GeneratedRegex("^(?:/cygdrive)?/([a-zA-Z])(/.*)?$")]
    private static partial Regex MsysDrive();

    /// <summary>
    /// What Ctrl+click on <paramref name="uri"/> may do. Links come from whatever a program
    /// prints, so only web/mail schemes open directly; file links are revealed rather than
    /// opened, so a link can't launch an executable.
    /// </summary>
    public static LinkAction Classify(string uri, LinkContext context, out string target)
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

        var host = parsed.Host;
        if (!parsed.IsFile || !(host.Length == 0 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                host.Equals(context.MachineName, StringComparison.OrdinalIgnoreCase)))
        {
            return LinkAction.Reject;
        }

        // Not LocalPath: with a host that's the UNC path \\host\..., and /c/.. isn't a Windows path.
        // (Uri drops the leading slash of file:///C:/.., but keeps it after a host.)
        var path = Uri.UnescapeDataString(parsed.AbsolutePath);
        var windows = path.TrimStart('/');
        if (windows.Length >= 2 && char.IsAsciiLetter(windows[0]) && windows[1] == ':')
        {
            target = windows.Replace('/', '\\');
            return LinkAction.Reveal;
        }

        if (context.WslDistro is { } distro)
        {
            target = (context.WslMountRoot is { } mounts ? WslConf.WindowsPath(path, mounts) : null)
                     ?? $@"\\wsl.localhost\{distro}{path.Replace('/', '\\')}";
            return LinkAction.Reveal;
        }

        if (MsysDrive().Match(path) is { Success: true } drive)
        {
            target = DrivePath(drive);
            return LinkAction.Reveal;
        }

        if (context.PosixRoot is { } root && path.StartsWith('/'))
        {
            target = Path.Join(root, path.Replace('/', '\\'));
            return LinkAction.Reveal;
        }

        return LinkAction.Reject;
    }

    private static string DrivePath(Match m) =>
        char.ToUpperInvariant(m.Groups[1].Value[0]) + @":\" + m.Groups[2].Value.TrimStart('/').Replace('/', '\\');
}
