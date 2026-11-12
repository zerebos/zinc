namespace Zinc.Terminal;

/// <summary>Where new tabs and splits start when the workspace has no folder of its own. Config: <c>[window] start-in</c>.</summary>
public enum NewTerminalFolder
{
    /// <summary>The focused terminal's current folder (as its shell last reported it), else the profile's.</summary>
    Current,

    /// <summary>The profile's own folder.</summary>
    Profile,
}

/// <summary>What a new terminal is: a tab in a pane, a split, or a copy of one (Duplicate).</summary>
public enum StartKind
{
    NewTab,
    Split,
    Duplicate,
}

/// <summary>A folder a new terminal may start in.</summary>
public enum StartSource
{
    /// <summary>Where the focused (or duplicated) terminal's shell is now.</summary>
    Current,

    /// <summary>The workspace's own folder.</summary>
    Workspace,

    /// <summary>Where the duplicated terminal started.</summary>
    Original,

    /// <summary>The profile's folder (the last resort, which always works).</summary>
    Usual,
}

/// <summary>
/// The folders a new terminal tries, in order (the first one its shell can reach wins). New tabs follow the
/// workspace's folder, splits sit beside a terminal and start where it is, and a duplicate starts where the original
/// is now. Without a workspace folder, new tabs also start where you are, unless the setting is
/// <see cref="NewTerminalFolder.Profile"/>.
/// </summary>
public static class NewTerminalStart
{
    public static IReadOnlyList<StartSource> Order(StartKind kind, bool workspaceFolder, NewTerminalFolder startIn)
    {
        var current = startIn == NewTerminalFolder.Current;
        return kind switch
        {
            StartKind.Duplicate => [StartSource.Current, StartSource.Original, StartSource.Usual],
            StartKind.Split when current => workspaceFolder
                ? [StartSource.Current, StartSource.Workspace, StartSource.Usual]
                : [StartSource.Current, StartSource.Usual],
            _ when workspaceFolder => [StartSource.Workspace, StartSource.Usual],
            StartKind.NewTab when current => [StartSource.Current, StartSource.Usual],
            _ => [StartSource.Usual],
        };
    }

    /// <summary>
    /// A terminal's current folder as a <see cref="WorkspaceFolder"/>, so any shell can be started there.
    /// <paramref name="reported"/> is what its shell last reported (OSC 7 / 9;9), and <paramref name="wslDistro"/> is
    /// the distro it runs in (null when it isn't WSL). Null when nothing usable was reported.
    /// </summary>
    public static WorkspaceFolder? Current(string? reported, string? wslDistro)
    {
        if (ShellDirectory.Parse(reported) is not { } path)
        {
            return null;
        }

        // A Linux path from a WSL shell belongs to its distro, while Windows paths (and MSYS ones) stand alone.
        return new WorkspaceFolder(path, path.StartsWith('/') ? wslDistro : null);
    }
}
