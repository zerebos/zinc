namespace Zinc.Terminal;

/// <summary>
/// Decodes the working directory a shell reports: OSC 7 sends a <c>file://host/path</c> URI
/// (percent-encoded), OSC 9;9 and OSC 1337 CurrentDir a bare path. Windows paths come back
/// with backslashes (<c>file:///C:/x</c> → <c>C:\x</c>); POSIX paths (WSL, MSYS) stay as sent.
/// A URI naming another machine (not one of the <see cref="LocalHosts"/>) reports nothing.
/// </summary>
public static class ShellDirectory
{
    /// <summary>The path in <paramref name="reported"/>, or null if there's none worth using.</summary>
    public static string? Parse(string? reported)
    {
        var s = reported?.Trim().Trim('"');
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        if (s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            // file://host/path: the path starts at the first slash after the host. A shell on another
            // machine (ssh) names its own host, and its /c/x isn't C:\x here.
            var slash = s.IndexOf('/', "file://".Length);
            if (slash < 0 || RemoteHost(s) != null)
            {
                return null;
            }

            s = Uri.UnescapeDataString(s[slash..]);
            // "/C:/Users/x" is a Windows path.
            if (s.Length >= 3 && s[0] == '/' && char.IsAsciiLetter(s[1]) && s[2] == ':')
            {
                s = s[1..];
            }
        }

        if (s.Length >= 2 && char.IsAsciiLetter(s[0]) && s[1] == ':')
        {
            s = s.Replace('/', '\\');
            return s.Length == 2 ? s + "\\" : s;
        }

        return s.StartsWith('/') || s.StartsWith(@"\\") ? s : null;
    }

    /// <summary>
    /// The host a <c>file://host/path</c> report names when that isn't this PC (<see cref="LocalHosts"/>): a shell
    /// on another machine. Null for a local one and for reports without a host (OSC 9;9, OSC 1337 CurrentDir:
    /// those can't be told apart, so they're taken as local).
    /// </summary>
    public static string? RemoteHost(string? reported)
    {
        var s = reported?.Trim().Trim('"');
        if (s == null || !s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var slash = s.IndexOf('/', "file://".Length);
        var host = Uri.UnescapeDataString(slash < 0 ? s["file://".Length..] : s["file://".Length..slash]);
        return LocalHosts.Is(host) ? null : host;
    }

    /// <summary>
    /// How to start <paramref name="command"/> in <paramref name="directory"/> (as <see cref="Parse"/>
    /// returns it): WSL gets a Linux path via <c>--cd</c> (replacing any it had), a Windows path
    /// becomes the working directory, and an MSYS path (<c>/c/Users</c>) is mapped to its drive.
    /// Null when the directory can't be used with this command (the caller falls back).
    /// </summary>
    public static (string Command, string? WorkingDirectory)? LaunchIn(string command, string directory)
    {
        if (IsWsl(command))
        {
            // POSIX, or home-relative: wsl --cd understands "~" and "~/sub" (checked).
            if (!directory.StartsWith('/') && directory != "~" && !directory.StartsWith("~/", StringComparison.Ordinal))
            {
                return null; // a Windows path: the tab's usual start works
            }

            var exe = FirstToken(command);
            var rest = System.Text.RegularExpressions.Regex.Replace(command[exe.Length..], @"\s--cd\s+(""[^""]*""|\S+)", "");
            return ($"{exe} --cd \"{directory}\"{rest}", null);
        }

        if (directory.Length >= 2 && directory[1] == ':' || directory.StartsWith(@"\\"))
        {
            return (command, directory);
        }

        // MSYS / Git Bash: "/c/Users/x" is C:\Users\x.
        var msys = System.Text.RegularExpressions.Regex.Match(directory, "^/([a-zA-Z])(/.*)?$");
        return msys.Success
            ? (command, $"{char.ToUpperInvariant(msys.Groups[1].Value[0])}:{(msys.Groups[2].Value is { Length: > 0 } p ? p : "/")}".Replace('/', '\\'))
            : null;
    }

    /// <summary>
    /// A folder someone typed, in the form <see cref="LaunchIn"/> takes for <paramref name="command"/>:
    /// for WSL a Linux path (<c>~</c> kept for WSL to expand, <c>C:\x</c> becoming <c>/mnt/c/x</c>);
    /// otherwise a Windows path (<c>~</c> is the user's home, environment variables expand) or an
    /// MSYS one (<c>/c/src</c>). A relative path starts from <paramref name="current"/> (where the
    /// shell is, as reported). Null if it can't be one.
    /// </summary>
    public static string? ResolveTyped(string typed, string command, string current, string? home = null)
    {
        var s = typed.Trim().Trim('"');
        if (s.Length == 0)
        {
            return null;
        }

        if (IsWsl(command))
        {
            if (s.StartsWith('/') || s == "~" || s.StartsWith("~/", StringComparison.Ordinal))
            {
                return s;
            }

            var drive = System.Text.RegularExpressions.Regex.Match(s, @"^([a-zA-Z]):[\\/]?(.*)$");
            if (drive.Success)
            {
                return JoinPosix($"/mnt/{char.ToLowerInvariant(drive.Groups[1].Value[0])}", drive.Groups[2].Value.Replace('\\', '/'));
            }

            return current.StartsWith('/') ? JoinPosix(current, s) : null;
        }

        s = Environment.ExpandEnvironmentVariables(s);
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (s == "~")
        {
            return home;
        }

        if (s.StartsWith("~/", StringComparison.Ordinal) || s.StartsWith(@"~\", StringComparison.Ordinal))
        {
            return Path.GetFullPath(Path.Combine(home, s[2..]));
        }

        if (s.StartsWith('/'))
        {
            return s; // MSYS: LaunchIn maps /c/... to its drive
        }

        if (Path.IsPathFullyQualified(s))
        {
            return Path.GetFullPath(s);
        }

        return current.StartsWith('/') ? JoinPosix(current, s)
            : current.Length > 0 ? Path.GetFullPath(Path.Combine(current, s))
            : null;
    }

    /// <summary>A POSIX path joined and normalized ("." and ".." resolved).</summary>
    private static string JoinPosix(string start, string relative)
    {
        var parts = new List<string>();
        foreach (var part in $"{start}/{relative}".Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(part);
        }

        return "/" + string.Join('/', parts);
    }

    /// <summary>Whether <paramref name="command"/> starts a WSL distro.</summary>
    public static bool IsWsl(string command) =>
        Path.GetFileNameWithoutExtension(FirstToken(command).Trim('"')).Equals("wsl", StringComparison.OrdinalIgnoreCase);

    /// <summary>The executable part of a command line (quoted or not), as written.</summary>
    private static string FirstToken(string command)
    {
        var s = command.TrimStart();
        if (s.StartsWith('"'))
        {
            var close = s.IndexOf('"', 1);
            return close < 0 ? s : s[..(close + 1)];
        }

        var space = s.IndexOf(' ');
        return space < 0 ? s : s[..space];
    }
}
