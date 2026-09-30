using System.Text.RegularExpressions;

namespace Phosphor.Terminal;

/// <summary>How the shell in a pane wants a path quoted.</summary>
public enum PathQuoting
{
    /// <summary>bash, zsh (WSL, Git Bash, MSYS2): <c>'…'</c>, a quote inside as <c>'\''</c>.</summary>
    Posix,

    /// <summary>PowerShell: <c>'…'</c>, a quote inside doubled (no <c>$</c> or backtick expansion).</summary>
    PowerShell,

    /// <summary>cmd and anything else: <c>"…"</c> (Windows paths can't contain a double quote).</summary>
    Windows,
}

/// <summary>
/// Files dropped on a pane become text typed at its prompt: each path as the pane's shell writes
/// it (WSL: <c>/mnt/c/…</c>, or <c>/home/…</c> for its own files; Git Bash and MSYS2: <c>/c/…</c>, or
/// <c>/usr/…</c> under their root), quoted where needed, separated by spaces.
/// </summary>
public static partial class DroppedPaths
{
    public static string Format(IEnumerable<string> windowsPaths, LinkContext context, PathQuoting quoting) =>
        string.Join(' ', windowsPaths.Where(p => p.Length > 0).Select(p => Quote(Translate(p, context), quoting)));

    /// <summary><paramref name="path"/> (a Windows path) as the pane's shell names it.</summary>
    public static string Translate(string path, LinkContext context)
    {
        if (context.WslDistro is { } distro)
        {
            if (WslShare().Match(path) is { Success: true } share)
            {
                // The distro's own files; another distro's stay a Windows path (not mounted here).
                return share.Groups[1].Value.Equals(distro, StringComparison.OrdinalIgnoreCase)
                    ? "/" + share.Groups[2].Value.Replace('\\', '/')
                    : path;
            }

            return DrivePath(path, "/mnt/") ?? path;
        }

        if (context.PosixRoot is { } root)
        {
            var under = Path.GetRelativePath(root, path);
            if (!under.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(under))
            {
                return under == "." ? "/" : "/" + under.Replace('\\', '/');
            }

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return path.Replace('\\', '/'); // //server/share/…
            }

            return DrivePath(path, "/") ?? path;
        }

        return path;
    }

    public static string Quote(string path, PathQuoting quoting) => quoting switch
    {
        PathQuoting.Posix => PosixSafe().IsMatch(path) ? path : "'" + path.Replace("'", @"'\''") + "'",
        PathQuoting.PowerShell => WindowsSafe().IsMatch(path) ? path : "'" + path.Replace("'", "''") + "'",
        _ => WindowsSafe().IsMatch(path) ? path : "\"" + path + "\"",
    };

    /// <summary><c>C:\a\b</c> → <c>{prefix}c/a/b</c>; null if it isn't on a drive.</summary>
    private static string? DrivePath(string path, string prefix) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
            ? prefix + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/')
            : null;

    // \\wsl.localhost\Distro\rest or \\wsl$\Distro\rest
    [GeneratedRegex(@"^\\\\wsl(?:\.localhost|\$)\\([^\\]+)\\?(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex WslShare();

    // Nothing a POSIX shell would split, expand or glob (and no ~ at the start).
    [GeneratedRegex(@"^[A-Za-z0-9_./+,:@%=-][A-Za-z0-9_./+,:@%=~-]*$")]
    private static partial Regex PosixSafe();

    // Nothing cmd or PowerShell would split or treat specially (both split at , and cmd at =).
    [GeneratedRegex(@"^[A-Za-z0-9_.\\/:+~-]+$")]
    private static partial Regex WindowsSafe();
}
