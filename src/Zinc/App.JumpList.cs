using Zinc.Session;
using Zinc.Shell;

namespace Zinc;

// The taskbar button's jump list (Core's JumpListPlan, written by JumpListWriter) and the launches its items make
// (LaunchRequest: zinc.exe with Zinc's flags, handed to the running Zinc like any later launch).
public partial class App
{
    /// <summary>The profiles opened lately (saved with the session): the jump list's "Recent profiles".</summary>
    internal RecentProfiles RecentProfiles { get; private set; } = new();

    // What the list was last written as (Core's key), and the profiles' icon files (finding them probes the disk).
    private string? _jumpListKey;
    private readonly Dictionary<string, JumpIcon> _jumpIcons = [];

    // Test runs (ZINC_CONFIG) leave the jump list alone, unless ZINC_JUMP_LIST=1 asks for it.
    private static bool JumpListWanted =>
        Environment.GetEnvironmentVariable("ZINC_CONFIG") is not { Length: > 0 } || Environment.GetEnvironmentVariable("ZINC_JUMP_LIST") == "1";

    internal void NoteProfileOpened(string id)
    {
        if (!id.StartsWith("handed-over:", StringComparison.Ordinal))
        {
            RecentProfiles.Note(id);
        }
    }

    /// <summary>Does what a launch with Zinc's flags asks (a jump list item, or <c>zinc --profile …</c> typed).</summary>
    internal void HandleLaunch(LaunchRequest request)
    {
        Log.Write($"launch: {request.Action} {request.Profile} {request.Folder}".TrimEnd());
        var last = _windows.LastOrDefault();
        switch (request.Action)
        {
            case LaunchAction.Profile or LaunchAction.Folder:
                var profile = (request.Profile is { } id ? _config.FindProfile(id) : null) ?? _config.DefaultProfile;
                var start = new MainWindow.LaunchStart(profile, request.Folder);
                if (last != null)
                {
                    last.OpenLaunched(start);
                }
                else
                {
                    Track(new MainWindow(_config, _keybindings, start));
                }

                break;
            case LaunchAction.NewWindow:
                OpenNewWindow();
                break;
            case LaunchAction.QuickTerminal:
                ToggleQuickTerminal();
                break;
            case LaunchAction.Settings:
                ShowSettings();
                break;
            case LaunchAction.ReopenWorkspace:
                if (last == null)
                {
                    OpenNewWindow();
                    last = _windows.LastOrDefault();
                }

                last?.ReopenLastWorkspace();
                break;
            case LaunchAction.ReopenWindow:
                if (ClosedWindows.All.Count > 0)
                {
                    ReopenWindow();
                }
                else if (_windows.Count == 0)
                {
                    OpenNewWindow();
                }

                break;
        }
    }

    /// <summary>
    /// Writes the jump list when what it holds changed (called with every session save tick, and at start). The
    /// installed exe's path is in every item, so a new install's first run writes it again.
    /// </summary>
    private void UpdateJumpList()
    {
        if (!JumpListWanted || Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            var profiles = _config.VisibleProfiles.Select(p => new JumpProfile(p.Id, p.Name, JumpIconFor(p))).ToList();
            var folderIcon = new JumpIcon(Path.Combine(Environment.SystemDirectory, "shell32.dll"), 3); // a folder
            JumpListContent Build(IReadOnlySet<string> removed) => JumpListPlan.Build(profiles, RecentProfiles.All, RecentFolders.All,
                ClosedWorkspaces.All.Count > 0, ClosedWindows.All.Count > 0, removed, folderIcon);
            var key = exe + "\n" + JumpListPlan.Key(Build(new HashSet<string>()));
            if (key == _jumpListKey)
            {
                return;
            }

            if (JumpListWriter.Write(exe, Build))
            {
                _jumpListKey = key;
                Log.Write("jump list: written");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"jump list: {ex.Message}");
        }
    }

    private JumpIcon JumpIconFor(Profile profile)
    {
        var key = $"{profile.Id}|{profile.Config.Icon}|{profile.Config.Command}";
        if (!_jumpIcons.TryGetValue(key, out var icon))
        {
            _jumpIcons[key] = icon = ProfileIcons.JumpIconFor(profile);
        }

        return icon;
    }
}
