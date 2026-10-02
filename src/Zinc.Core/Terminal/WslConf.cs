namespace Zinc.Terminal;

/// <summary>
/// Where a WSL distro mounts Windows drives: its /etc/wsl.conf's <c>[automount] root</c>,
/// <c>/mnt/</c> by default. File drops and file links translate drive paths with it.
/// </summary>
public static class WslConf
{
    public const string DefaultMountRoot = "/mnt/";

    /// <summary>
    /// The <c>[automount] root</c> (with a trailing slash); <see cref="DefaultMountRoot"/> without
    /// one (or without the file); null with <c>enabled = false</c> (drives aren't mounted).
    /// </summary>
    public static string? MountRoot(string? conf)
    {
        string? root = null;
        var enabled = true;
        var section = "";
        foreach (var raw in (conf ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            var eq = line.IndexOf('=');
            if (section != "automount" || eq < 1)
            {
                continue;
            }

            var key = line[..eq].Trim().ToLowerInvariant();
            var value = StripComment(line[(eq + 1)..]).Trim().Trim('"', '\'');
            if (key == "root" && value.StartsWith('/'))
            {
                root = value.EndsWith('/') ? value : value + "/";
            }
            else if (key == "enabled")
            {
                enabled = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
            }
        }

        return enabled ? root ?? DefaultMountRoot : null;
    }

    /// <summary><c>C:\a</c> as the distro sees it under <paramref name="root"/> (<c>/mnt/c/a</c>); null if it isn't on a drive.</summary>
    public static string? DrivePath(string windowsPath, string root) =>
        windowsPath.Length >= 2 && char.IsAsciiLetter(windowsPath[0]) && windowsPath[1] == ':'
            ? root + char.ToLowerInvariant(windowsPath[0]) + windowsPath[2..].Replace('\\', '/')
            : null;

    /// <summary>A Linux path in a drive folder under <paramref name="root"/> as a Windows path; null otherwise.</summary>
    public static string? WindowsPath(string linuxPath, string root)
    {
        var at = root.Length;
        if (!linuxPath.StartsWith(root, StringComparison.Ordinal) || linuxPath.Length <= at ||
            !char.IsAsciiLetter(linuxPath[at]) || (linuxPath.Length > at + 1 && linuxPath[at + 1] != '/'))
        {
            return null;
        }

        return char.ToUpperInvariant(linuxPath[at]) + @":\" + linuxPath[(at + 1)..].TrimStart('/').Replace('/', '\\');
    }

    /// <summary>A value's trailing <c># comment</c>, if any (a # inside quotes stays).</summary>
    private static string StripComment(string value)
    {
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"')
            {
                quoted = !quoted;
            }
            else if (value[i] == '#' && !quoted)
            {
                return value[..i];
            }
        }

        return value;
    }
}
