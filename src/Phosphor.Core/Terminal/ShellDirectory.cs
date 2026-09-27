namespace Phosphor.Terminal;

/// <summary>
/// Decodes the working directory a shell reports: OSC 7 sends a <c>file://host/path</c> URI
/// (percent-encoded), OSC 9;9 and OSC 1337 CurrentDir a bare path. Windows paths come back
/// with backslashes (<c>file:///C:/x</c> → <c>C:\x</c>); POSIX paths (WSL, MSYS) stay as sent.
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
            // file://host/path: the path starts at the first slash after the host.
            var slash = s.IndexOf('/', "file://".Length);
            if (slash < 0)
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
    /// How to start <paramref name="command"/> in <paramref name="directory"/> (as <see cref="Parse"/>
    /// returns it): WSL gets a Linux path via <c>--cd</c> (replacing any it had), a Windows path
    /// becomes the working directory, and an MSYS path (<c>/c/Users</c>) is mapped to its drive.
    /// Null when the directory can't be used with this command (the caller falls back).
    /// </summary>
    public static (string Command, string? WorkingDirectory)? LaunchIn(string command, string directory)
    {
        if (IsWsl(command))
        {
            if (!directory.StartsWith('/'))
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

    private static bool IsWsl(string command) =>
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
