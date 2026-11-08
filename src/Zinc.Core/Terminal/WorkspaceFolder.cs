namespace Zinc.Terminal;

/// <summary>
/// A workspace's own folder (chosen on the new-workspace page): new tabs and splits in the
/// workspace start there. <see cref="Path"/> is as the shell it was chosen for writes it: a Windows
/// path, an MSYS one (<c>/c/src</c>), <c>~</c>, or, with <see cref="WslDistro"/>, a Linux path in
/// that distro. Other shells get it in their own terms, or their usual folder when they can't
/// reach it.
/// </summary>
public sealed record WorkspaceFolder(string Path, string? WslDistro = null)
{
    /// <summary>The folder's own name, for the workspace's title (<c>~/src/ghostty</c> → "ghostty").</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Name
    {
        get
        {
            var trimmed = Path.TrimEnd('/', '\\');
            if (trimmed.Length == 0)
            {
                return Path;
            }

            var cut = trimmed.LastIndexOfAny(['/', '\\']);
            var name = cut < 0 ? trimmed : trimmed[(cut + 1)..];
            return name.Length > 0 && !name.EndsWith(':') ? name : trimmed; // "C:" stays "C:\"-ish
        }
    }

    /// <summary>
    /// How <paramref name="command"/> starts in this folder (see <see cref="ShellDirectory.LaunchIn"/>);
    /// <paramref name="commandDistro"/> is the distro a WSL command runs (null: its default, or not WSL).
    /// Null when this shell can't reach the folder: the caller uses the profile's own.
    /// </summary>
    public (string Command, string? WorkingDirectory)? LaunchFor(string command, string? commandDistro)
    {
        var windows = IsWindowsPath(Path);
        if (ShellDirectory.IsWsl(command) && WslDistro != null && !windows &&
            (commandDistro == null || commandDistro.Equals(WslDistro, StringComparison.OrdinalIgnoreCase)))
        {
            return ShellDirectory.LaunchIn(command, Path);
        }

        // A distro's view of a Windows drive (/mnt/c/dev) is that Windows folder to every other shell.
        if (WslDistro != null && OnWindowsDrive(Path) is { } drive)
        {
            return new WorkspaceFolder(drive).LaunchFor(command, commandDistro);
        }

        if (ShellDirectory.IsWsl(command))
        {
            // A Windows folder: the distro sees its drives under /mnt.
            return windows && ShellDirectory.ResolveTyped(Path, command, "") is { } mounted
                ? ShellDirectory.LaunchIn(command, mounted)
                : null;
        }

        if (windows)
        {
            return ShellDirectory.LaunchIn(command, Path);
        }

        if (WslDistro != null)
        {
            // A distro's folder from Windows: through its share (not "~": its home isn't known here), which cmd can't
            // start in (it says so and starts in C:\Windows).
            return Path.StartsWith('/') && !ShellDirectory.IsCmd(command)
                ? (command, $@"\\wsl.localhost\{WslDistro}{Path.Replace('/', '\\')}")
                : null;
        }

        // "~" (home) or an MSYS path.
        return ShellDirectory.ResolveTyped(Path, command, "") is { } resolved ? ShellDirectory.LaunchIn(command, resolved) : null;
    }

    /// <summary>The Windows folder a distro path under the default mount root is (<c>/mnt/c/dev</c> → <c>C:\dev</c>), else null.</summary>
    private static string? OnWindowsDrive(string path)
    {
        var drive = System.Text.RegularExpressions.Regex.Match(path, $"^{WslConf.DefaultMountRoot}([a-zA-Z])(/.*)?$");
        return drive.Success
            ? $"{char.ToUpperInvariant(drive.Groups[1].Value[0])}:{(drive.Groups[2].Value is { Length: > 0 } rest ? rest : "/")}".Replace('/', '\\')
            : null;
    }

    /// <summary>
    /// A folder chosen in Windows (a folder dialog) for a shell: on <paramref name="distro"/>'s share
    /// (<c>\\wsl.localhost\Fedora\home\jdoe</c>) it's that distro's own path (<c>/home/jdoe</c>);
    /// otherwise the Windows path as is.
    /// </summary>
    public static WorkspaceFolder FromWindows(string path, string? distro)
    {
        if (distro != null)
        {
            foreach (var share in new[] { $@"\\wsl.localhost\{distro}", $@"\\wsl$\{distro}" })
            {
                if (path.StartsWith(share, StringComparison.OrdinalIgnoreCase) &&
                    (path.Length == share.Length || path[share.Length] == '\\'))
                {
                    var rest = path[share.Length..].Replace('\\', '/');
                    return new WorkspaceFolder(rest.Length == 0 ? "/" : rest, distro);
                }
            }
        }

        return new WorkspaceFolder(path);
    }

    private static bool IsWindowsPath(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' || path.StartsWith(@"\\", StringComparison.Ordinal);
}

/// <summary>A folder a shell reported, for the new-workspace page: which profile was there, and when.</summary>
public sealed record RecentFolder(string Path, string Profile, string? WslDistro, DateTimeOffset When);

/// <summary>
/// Folders shells reported lately, newest first, one entry per folder and profile; shared by every
/// window and saved with the session.
/// </summary>
public sealed class RecentFolders
{
    public const int Capacity = 40;

    private readonly List<RecentFolder> _folders = [];

    public RecentFolders(IEnumerable<RecentFolder>? saved = null) =>
        _folders.AddRange((saved ?? []).Take(Capacity));

    public IReadOnlyList<RecentFolder> All => _folders;

    public void Note(RecentFolder folder)
    {
        // The same folder again soon (every prompt reports it): nothing new to keep.
        if (_folders.Count > 0 && _folders[0].Path == folder.Path && _folders[0].Profile == folder.Profile &&
            folder.When - _folders[0].When < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _folders.RemoveAll(f => f.Path == folder.Path && f.Profile == folder.Profile);
        _folders.Insert(0, folder);
        if (_folders.Count > Capacity)
        {
            _folders.RemoveAt(_folders.Count - 1);
        }
    }

    /// <summary>
    /// The folders a profile can start in, newest first: its own, and Windows ones (any shell reaches
    /// those; WSL through /mnt). One entry per path.
    /// </summary>
    public IEnumerable<RecentFolder> For(string profile, string? wslDistro) =>
        _folders.Where(f => f.Profile == profile || (wslDistro != null && f.WslDistro == wslDistro) ||
                            f.Path.Length >= 2 && f.Path[1] == ':')
            .DistinctBy(f => f.Path);
}
