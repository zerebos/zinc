namespace Zinc;

/// <summary>What a launch with Zinc's own flags asks for (the jump list's items are such launches).</summary>
public enum LaunchAction
{
    /// <summary>No flag: the arguments, if any, are a command to run (Zinc's rule from the start).</summary>
    None,

    /// <summary><c>--profile &lt;id&gt;</c>: a new workspace with that profile, in the window used last.</summary>
    Profile,

    /// <summary>
    /// <c>--folder &lt;path&gt;</c> (with <c>--profile &lt;id&gt;</c>, or the default profile): a new workspace there, in the
    /// window used last. The path is as that profile's shell reported it (Windows, WSL or MSYS).
    /// </summary>
    Folder,

    /// <summary><c>--new-window</c>.</summary>
    NewWindow,

    /// <summary><c>--quick-terminal</c>: shows or hides it.</summary>
    QuickTerminal,

    /// <summary><c>--settings</c>.</summary>
    Settings,

    /// <summary><c>--reopen-workspace</c>: the workspace closed last.</summary>
    ReopenWorkspace,

    /// <summary><c>--reopen-window</c>: the window closed last.</summary>
    ReopenWindow,
}

/// <summary>
/// A launch's arguments, read: Zinc's flags (at the start of the line), or a command. Anything that doesn't start with
/// a flag stays a command, as before (<c>zinc wsl.exe</c>); <c>zinc -- &lt;command&gt;</c> runs a command that would
/// otherwise look like a flag.
/// </summary>
public sealed record LaunchRequest(LaunchAction Action, string? Profile, string? Folder, IReadOnlyList<string> Command)
{
    public static readonly LaunchRequest Nothing = new(LaunchAction.None, null, null, []);

    private static readonly Dictionary<string, LaunchAction> Switches = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--new-window"] = LaunchAction.NewWindow,
        ["--quick-terminal"] = LaunchAction.QuickTerminal,
        ["--settings"] = LaunchAction.Settings,
        ["--reopen-workspace"] = LaunchAction.ReopenWorkspace,
        ["--reopen-window"] = LaunchAction.ReopenWindow,
    };

    public const string ProfileFlag = "--profile", FolderFlag = "--folder";

    /// <summary>Whether <paramref name="arg"/> is one of Zinc's flags (a command line starting with one isn't a command).</summary>
    public static bool IsFlag(string arg) =>
        Switches.ContainsKey(arg) || arg.Equals(ProfileFlag, StringComparison.OrdinalIgnoreCase) ||
        arg.Equals(FolderFlag, StringComparison.OrdinalIgnoreCase);

    public static LaunchRequest Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return Nothing;
        }

        if (args[0] == "--")
        {
            return new LaunchRequest(LaunchAction.None, null, null, [.. args.Skip(1)]);
        }

        if (!IsFlag(args[0]))
        {
            return new LaunchRequest(LaunchAction.None, null, null, [.. args]);
        }

        string? profile = null, folder = null;
        var action = LaunchAction.None;
        for (var i = 0; i < args.Count; i++)
        {
            if (Switches.TryGetValue(args[i], out var named))
            {
                action = named;
            }
            else if (args[i].Equals(ProfileFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count && args[i + 1].Length > 0)
            {
                profile = args[++i];
            }
            else if (args[i].Equals(FolderFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count && args[i + 1].Length > 0)
            {
                folder = args[++i];
            }
        }

        // A switch wins; else a folder (with or without a profile); else a profile. A flag without its value asks for
        // nothing (rather than running "--profile" as a program).
        action = action != LaunchAction.None ? action
            : folder != null ? LaunchAction.Folder
            : profile != null ? LaunchAction.Profile
            : LaunchAction.None;
        return new LaunchRequest(action, profile, folder, []);
    }

    /// <summary>The arguments that ask for <paramref name="action"/>, as a jump list item passes them.</summary>
    public static string Arguments(LaunchAction action, string? profile = null, string? folder = null)
    {
        var parts = new List<string>();
        if (action is LaunchAction.Profile or LaunchAction.Folder && profile != null)
        {
            parts.Add(ProfileFlag);
            parts.Add(Quote(profile));
        }

        if (action == LaunchAction.Folder && folder != null)
        {
            parts.Add(FolderFlag);
            parts.Add(Quote(folder));
        }

        if (Switches.FirstOrDefault(s => s.Value == action).Key is { } flag)
        {
            parts.Add(flag);
        }

        return string.Join(' ', parts);
    }

    // Windows' command-line quoting (CommandLineToArgvW reads it back): quotes around, backslashes before a quote doubled.
    private static string Quote(string s)
    {
        if (s.Length > 0 && s.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return s;
        }

        var sb = new System.Text.StringBuilder("\"");
        var slashes = 0;
        foreach (var c in s)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            sb.Append(c);
        }

        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }
}
