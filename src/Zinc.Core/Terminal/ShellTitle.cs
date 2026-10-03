using System.Text.RegularExpressions;

namespace Zinc.Terminal;

/// <summary>
/// What a terminal's tab is called (<c>[terminal] shell-titles</c>, Ghostty's <c>title</c> feature, done from the marks
/// instead of in each shell's script, so every shell that marks its prompt gets it): the folder while the shell waits at
/// its prompt, the command while one runs. A title the running program sets itself (vim, htop) wins until it ends.
/// Shells that mark nothing keep today's behaviour: the program's title.
/// </summary>
public static partial class ShellTitle
{
    /// <param name="shellTitles"><c>shell-titles</c>.</param>
    /// <param name="programTitles"><c>program-titles</c> (off: programs can't name the tab).</param>
    /// <param name="atPrompt">From the marks: true at the prompt, false while a command runs, null if the shell marks nothing.</param>
    /// <param name="folder">The folder the shell last reported (decoded), if any.</param>
    /// <param name="command">The running command's text, if known.</param>
    /// <param name="programTitle">The last title a program set ("" if none).</param>
    /// <param name="programTitleSinceCommand">That title was set after the running command started.</param>
    /// <param name="home">The user's Windows home folder (shown as ~).</param>
    /// <returns>The title; "" leaves it to the profile's name.</returns>
    public static string Choose(bool shellTitles, bool programTitles, bool? atPrompt, string? folder, string? command, string programTitle,
        bool programTitleSinceCommand, string? home)
    {
        var program = programTitles ? programTitle : "";
        if (!shellTitles || atPrompt == null)
        {
            return program;
        }

        if (atPrompt == false)
        {
            return programTitleSinceCommand && program.Length > 0 ? program
                : FirstLine(command) is { Length: > 0 } text ? text
                : program;
        }

        return folder != null ? Folder(folder, home) : program;
    }

    /// <summary>A folder as a title: the home folder as ~ (Windows, MSYS's /c/Users/x, a Linux /home/x or /root).</summary>
    public static string Folder(string path, string? home)
    {
        if (home is { Length: > 0 })
        {
            var windows = home.TrimEnd('\\');
            var msys = MsysForm(windows);
            foreach (var (form, separator) in new[] { (windows, '\\'), (msys, '/') })
            {
                if (form != null && Under(path, form, separator) is { } rest)
                {
                    return rest.Length == 0 ? "~" : "~" + separator + rest;
                }
            }
        }

        var linux = LinuxHome().Match(path);
        if (linux.Success)
        {
            return "~" + path[linux.Length..];
        }

        return path;
    }

    /// <summary>What follows <paramref name="root"/> in <paramref name="path"/> ("" for the root itself); null if it's elsewhere.</summary>
    private static string? Under(string path, string root, char separator)
    {
        var trimmed = path.TrimEnd(separator);
        if (trimmed.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return trimmed.StartsWith(root + separator, StringComparison.OrdinalIgnoreCase) ? trimmed[(root.Length + 1)..] : null;
    }

    /// <summary>C:\Users\x as MSYS writes it (/c/Users/x); null if it isn't a drive path.</summary>
    private static string? MsysForm(string windows) =>
        windows.Length >= 3 && windows[1] == ':' && windows[2] == '\\'
            ? "/" + char.ToLowerInvariant(windows[0]) + windows[2..].Replace('\\', '/')
            : null;

    /// <summary>A command's first line, trimmed and cut at 80 characters.</summary>
    private static string? FirstLine(string? command)
    {
        if (command == null)
        {
            return null;
        }

        var line = command.Split('\n', 2)[0].Trim();
        return line.Length > 80 ? line[..79] + "…" : line;
    }

    // A home in WSL or another Linux: /home/<name> (whoever it belongs to: the distro's user, nearly always) or /root.
    [GeneratedRegex(@"^(/home/[^/]+|/root)(?=/|$)")]
    private static partial Regex LinuxHome();
}
