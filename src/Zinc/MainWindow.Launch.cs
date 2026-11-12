using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

// What a launch with Zinc's flags asks of a window (the jump list's items, LaunchRequest): a new workspace for a
// profile, maybe in a folder, or the workspace closed last.
public sealed partial class MainWindow
{
    /// <summary>A window a launch asked for, with one workspace running a profile, maybe in a folder.</summary>
    internal sealed record LaunchStart(Profile Profile, string? Folder);

    internal MainWindow(ConfigFile config, Keybindings keybindings, LaunchStart start)
        : this(config, keybindings, quick: false)
    {
        FocusActive(AddLaunched(start));
    }

    /// <summary>A new workspace for <paramref name="start"/>, in front.</summary>
    internal void OpenLaunched(LaunchStart start)
    {
        AddLaunched(start);
        BringForward();
    }

    /// <summary>
    /// Brings back the workspace closed last and puts the window in front (only in front when there's nothing to reopen).
    /// </summary>
    internal void ReopenLastWorkspace()
    {
        if (App.Instance.ClosedWorkspaces.All.FirstOrDefault() is { } last)
        {
            ReopenWorkspace(last);
        }

        BringForward();
    }

    private WorkspaceTab AddLaunched(LaunchStart start)
    {
        // The folder as the profile's shell reported it (a Linux path is in that profile's distro).
        var folder = start.Folder is { } path
            ? new WorkspaceFolder(path, ShellEnvironment.WslDistroOf(ShellResolver.ForProfile(start.Profile.Config)))
            : null;
        var (command, directory) = StartIn(folder, start.Profile);
        return AddWorkspace(new Workspace<TerminalTab>(CreatePane(start.Profile, command, directory)), folder?.Name,
            AutoColor(folder), folder: folder);
    }
}
