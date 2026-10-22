using System.Text.RegularExpressions;

namespace Zinc.Terminal;

/// <summary>
/// A pane header's title: what leads, and the quieter text after it (null: none). A part that's a folder
/// (<paramref name="LeadIsFolder"/>, <paramref name="DetailIsFolder"/>) loses its start when narrow, not its end
/// (<see cref="ShellTitle.FitFolder"/>).
/// </summary>
public sealed record HeaderTitle(string Lead, string? Detail, bool LeadIsFolder = false, bool DetailIsFolder = false);

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
    /// <param name="commandStarted">When the running command started, if one runs.</param>
    /// <param name="programTitle">The last title a program set ("" if none).</param>
    /// <param name="programTitleSinceCommand">That title was set after the running command started.</param>
    /// <param name="home">The user's Windows home folder (shown as ~).</param>
    /// <returns>The title; "" leaves it to the profile's name.</returns>
    /// <remarks>
    /// A command names the tab once it has run <see cref="CommandLeadsAfter"/>, and until then the folder stays, as in
    /// a pane header. A title the running program set (vim) names it at once.
    /// </remarks>
    public static string Choose(bool shellTitles, bool programTitles, bool? atPrompt, string? folder, string? command,
        DateTime? commandStarted, DateTime now, string programTitle, bool programTitleSinceCommand, string? home) =>
        Pick(shellTitles, programTitles, atPrompt, folder, command, commandStarted, now, programTitle, programTitleSinceCommand, home).Title;

    /// <summary>Whether <see cref="Choose"/> names the tab by its folder (which then shortens from its start, as a header's).</summary>
    public static bool ChoosesFolder(bool shellTitles, bool programTitles, bool? atPrompt, string? folder, string? command,
        DateTime? commandStarted, DateTime now, string programTitle, bool programTitleSinceCommand) =>
        Pick(shellTitles, programTitles, atPrompt, folder, command, commandStarted, now, programTitle, programTitleSinceCommand, null).IsFolder;

    private static (string Title, bool IsFolder) Pick(bool shellTitles, bool programTitles, bool? atPrompt, string? folder, string? command,
        DateTime? commandStarted, DateTime now, string programTitle, bool programTitleSinceCommand, string? home)
    {
        var program = programTitles ? programTitle : "";
        if (!shellTitles || atPrompt == null)
        {
            return (program, false);
        }

        if (atPrompt == false)
        {
            if (programTitleSinceCommand && program.Length > 0)
            {
                return (program, false);
            }

            if (UntilCommandLeads(commandStarted, now) == TimeSpan.Zero && FirstLine(command) is { Length: > 0 } text)
            {
                return (text, false);
            }
        }

        return folder != null ? (Folder(folder, home), true) : (program, false);
    }

    /// <summary>
    /// How long a command runs before it names the tab, leads a pane header's title and shows in the title bar, so quick
    /// ones (ls, git status) never flash there.
    /// </summary>
    public static readonly TimeSpan CommandLeadsAfter = TimeSpan.FromMilliseconds(300);

    /// <summary>How long until a command started at <paramref name="started"/> leads a header (zero: it does); null if none runs.</summary>
    public static TimeSpan? UntilCommandLeads(DateTime? started, DateTime now)
    {
        if (started is not { } at)
        {
            return null;
        }

        var left = CommandLeadsAfter - (now - at);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// A one-tab pane header's title (decisions 19, 38): what leads, and quieter text after it. At the prompt the folder
    /// leads and the profile follows; once a command has run <see cref="CommandLeadsAfter"/>, the command leads and the
    /// folder follows; a title the running program set (vim) leads at once. Without the marks: the program's title (or
    /// the profile's name) and the profile. <c>shell-titles</c> is about tab labels and isn't read here.
    /// </summary>
    /// <param name="commandStarted">When the running command started, if one runs.</param>
    /// <param name="profileName">The tab's profile ("PowerShell").</param>
    public static HeaderTitle Header(bool programTitles, bool? atPrompt, string? folder, string? command, DateTime? commandStarted,
        DateTime now, string programTitle, bool programTitleSinceCommand, string? home, string profileName)
    {
        var program = programTitles ? programTitle : "";
        var place = folder != null ? Folder(folder, home) : null;
        if (atPrompt == false)
        {
            if (programTitleSinceCommand && program.Length > 0)
            {
                return Make(program, place, detailIsFolder: true);
            }

            if (UntilCommandLeads(commandStarted, now) == TimeSpan.Zero && FirstLine(command) is { Length: > 0 } text)
            {
                return Make(text, place, detailIsFolder: true);
            }
        }

        // At the prompt (or a command that hasn't led yet): the folder, else the program's title, else the profile.
        return atPrompt != null && place != null
            ? Make(place, profileName, leadIsFolder: true)
            : Make(program.Length > 0 ? program : profileName, profileName);

        // No quieter text the title already says: a program's title often names the folder itself (as the command
        // center skips it), as written (~/dev/x) or in full (/home/zack/dev/x).
        HeaderTitle Make(string lead, string? detail, bool leadIsFolder = false, bool detailIsFolder = false)
        {
            var said = detail == null || Says(lead, detail) || (detailIsFolder && Says(lead, folder));
            return new(lead, said ? null : detail, leadIsFolder, !said && detailIsFolder);
        }
    }

    /// <summary>
    /// The folder to show after a title ("npm test  ·  ~\dev\zinc" in the command center and the tab switcher), or
    /// none when the title already names it, as written or in full.
    /// </summary>
    public static string? FolderAfter(string title, string folder, string? home)
    {
        var place = Folder(folder, home);
        return Says(title, place) || Says(title, folder) ? null : place;
    }

    private static bool Says(string lead, string? text) => text is { Length: > 0 } && lead.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A folder shortened from its start, longest first, as far as its last name: "…\dev\zinc", "…\zinc" for
    /// "~\dev\zinc". The end of a path says where you are, so that's what stays. None for a root ("~", "C:\", "/").
    /// </summary>
    public static IEnumerable<string> FolderShortenings(string folder)
    {
        var separator = folder.Contains('\\') ? '\\' : '/';
        var path = folder.TrimEnd(separator);
        var first = path.Length - path.TrimStart(separator).Length; // a leading / or \\server isn't a folder to drop
        for (var i = path.IndexOf(separator, first); i > 0; i = path.IndexOf(separator, i + 1))
        {
            if (i > 1) // "~" for "…" saves nothing
            {
                yield return "…" + path[i..];
            }
        }
    }

    /// <summary>
    /// A folder as it fits in <paramref name="room"/> (by <paramref name="measure"/>): whole, else the longest of its
    /// <see cref="FolderShortenings"/> that fits, else the shortest (it's then trimmed at the end like any text).
    /// </summary>
    public static string FitFolder(string folder, double room, Func<string, double> measure)
    {
        var fitted = folder;
        if (measure(folder) <= room)
        {
            return folder;
        }

        foreach (var shorter in FolderShortenings(folder))
        {
            fitted = shorter;
            if (measure(shorter) <= room)
            {
                break;
            }
        }

        return fitted;
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
