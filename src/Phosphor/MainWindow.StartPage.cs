using Microsoft.UI.Xaml.Controls;
using Phosphor.Panes;
using Phosphor.Session;
using Phosphor.Terminal;

namespace Phosphor;

// The new-workspace page (StartPage): a tab item of its own (not a WorkspaceTab: AllTabs skips it)
// until it starts, when a workspace takes its place. A workspace started in a folder keeps it
// (WorkspaceTab.Folder): its new tabs and splits start there (StartIn).
public sealed partial class MainWindow
{
    /// <summary>A new workspace: the page, unless it's turned off (or this is the quick terminal).</summary>
    private void OpenNewWorkspace()
    {
        if (_quick || !_config.Global.NewWorkspacePage)
        {
            OpenTab(_config.DefaultProfile);
            return;
        }

        // One page per window: asking again goes back to it.
        if (Tabs.TabItems.OfType<TabViewItem>().FirstOrDefault(i => i.Tag is StartPage) is { Tag: StartPage open } existing)
        {
            Tabs.SelectedItem = existing;
            open.FocusFilter();
            return;
        }

        var page = new StartPage(_config.VisibleProfiles.ToList(), _config.DefaultProfile, App.Instance.RecentFolders, BrowseFolder,
            App.Instance.ClosedWorkspaces.All);
        var item = new TabViewItem
        {
            Header = "New workspace",
            IconSource = new FontIconSource { Glyph = ((char)0xE710).ToString() },
            Tag = page,
            Content = page,
        };
        page.Started += choice => Ui.Guard("starting a workspace", () => StartWorkspace(item, choice));
        page.Cancelled += () => Ui.Guard("leaving the new workspace page", () => LeaveStartPage(item));
        page.ShowPageChanged += SetNewWorkspacePage;
        page.Reopen += closed => Ui.Guard("reopening a workspace", () => ReopenWorkspace(closed, item));
        Tabs.TabItems.Add(item);
        Tabs.SelectedItem = item;
        RefreshWorkspaceList();
        page.FocusFilter();
        Log.Write("start page: opened");
    }

    /// <summary>The page's choice becomes a workspace in its place.</summary>
    private void StartWorkspace(TabViewItem item, StartChoice choice)
    {
        var (profile, folder, name, layout) = (choice.Profile, choice.Folder, choice.Name, choice.Layout);
        var (command, directory) = StartIn(folder, profile);
        var index = Tabs.TabItems.IndexOf(item);
        // Every pane of the layout runs the profile in the folder. Named as asked, else after its
        // folder; without either, its shell names it (as before).
        var workspace = StartLayouts.Build(layout, () => CreatePane(profile, command, directory));
        // Its color: the one chosen (even "none"), else the automatic one (workspace-colors).
        var color = choice.ColorChosen ? choice.Color : AutoColor(folder);
        var tab = AddWorkspace(workspace, name ?? folder?.Name, color, index < 0 ? null : index, folder, choice.Icon);
        Tabs.TabItems.Remove(item); // no longer selected: AddWorkspace selected the new one
        RefreshWorkspaceList();
        FocusActive(tab);
        Log.Write($"start page: {profile.Name} in {folder?.Path ?? "its usual folder"}, {layout}{(name != null ? $" as {name}" : "")}");
    }

    /// <summary>
    /// A workspace you closed comes back as it was saved (tabs, splits, folders, name, color, its own
    /// folder): in the page's place, or as a new workspace.
    /// </summary>
    private void ReopenWorkspace(ClosedWorkspace closed, TabViewItem? page = null)
    {
        if (!App.Instance.ClosedWorkspaces.Take(closed))
        {
            return; // reopened already (in another window)
        }

        if (SessionLayout.Restore(closed.State.Layout, OpenSaved) is not { } workspace)
        {
            Log.Write($"reopen: nothing of {closed.Name} could start");
            return;
        }

        var index = page != null ? Tabs.TabItems.IndexOf(page) : -1;
        var tab = AddWorkspace(workspace, closed.State.Title, ParseColor(closed.State.Color), index < 0 ? null : index,
            closed.State.Folder, closed.State.Icon);
        if (page != null)
        {
            Tabs.TabItems.Remove(page);
        }

        RefreshWorkspaceList();
        FocusActive(tab);
        Log.Write($"reopen: {closed.Name} ({closed.Summary})");
    }

    /// <summary>A workspace you closed goes on the Reopen list, as a session would save it.</summary>
    private void RememberClosed(WorkspaceTab tab)
    {
        try
        {
            var state = new WorkspaceState(tab.CustomTitle, FormatColor(tab.Color), SessionLayout.Capture(tab.Workspace, DescribeTab),
                tab.Folder, tab.Icon);
            App.Instance.ClosedWorkspaces.Note(new ClosedWorkspace(tab.Header.Title, state, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            Log.Write($"reopen: couldn't keep a closed workspace: {ex.Message}");
        }
    }

    /// <summary>Esc: the page goes (a window's only tab starts the default instead: a window is never empty).</summary>
    private void LeaveStartPage(TabViewItem item)
    {
        if (Tabs.TabItems.Count == 1)
        {
            StartWorkspace(item, new StartChoice(_config.DefaultProfile, null, null, StartLayout.One, null, null, false));
            return;
        }

        RemoveStartPage(item);
    }

    /// <summary>The page's tab closes (its ×, or Esc): the one before it is shown.</summary>
    private void RemoveStartPage(TabViewItem item)
    {
        var index = Tabs.TabItems.IndexOf(item);
        var wasSelected = ReferenceEquals(Tabs.SelectedItem, item);
        Tabs.TabItems.Remove(item);
        if (Tabs.TabItems.Count == 0)
        {
            Close();
            return;
        }

        if (wasSelected)
        {
            Tabs.SelectedItem = Tabs.TabItems[Math.Clamp(index - 1, 0, Tabs.TabItems.Count - 1)];
        }

        RefreshWorkspaceList();
        if (SelectedTab is { } shown)
        {
            FocusActive(shown);
        }
    }

    /// <summary>
    /// How <paramref name="profile"/> starts in a workspace with <paramref name="folder"/>: there, in
    /// the shell's own terms, when it can reach it; else in its usual folder.
    /// </summary>
    private static (string Command, string Directory) StartIn(WorkspaceFolder? folder, Profile profile)
    {
        var command = ShellResolver.ForProfile(profile.Config);
        var usual = profile.Config.ResolveWorkingDirectory();
        if (folder == null)
        {
            return (command, usual);
        }

        if (folder.LaunchFor(command, ShellEnvironment.WslDistroOf(command)) is { } launch &&
            (launch.WorkingDirectory is not { } windows || Directory.Exists(windows)))
        {
            return (launch.Command, launch.WorkingDirectory ?? usual);
        }

        Log.Write($"workspace folder {folder.Path} isn't reachable from {profile.Name}: its usual folder");
        return (command, usual);
    }

    /// <summary>
    /// The automatic color for a new workspace (<c>workspace-colors</c>): its folder's, or the next
    /// in turn; null when they're off (or "by folder" without a folder).
    /// </summary>
    private Windows.UI.Color? AutoColor(WorkspaceFolder? folder) => _config.Global.WorkspaceColors switch
    {
        WorkspaceColorMode.Folder when folder != null => WorkspaceLookPickers.ToColor(WorkspaceColors.All[WorkspaceColors.ForFolder(folder.Path)].Rgb),
        WorkspaceColorMode.Cycle => WorkspaceLookPickers.ToColor(WorkspaceColors.All[App.Instance.NextWorkspaceColor() % WorkspaceColors.All.Count].Rgb),
        _ => null,
    };

    /// <summary>The page's "Show this page for new workspaces" box, written to config.toml.</summary>
    private void SetNewWorkspacePage(bool show)
    {
        try
        {
            ConfigEditor.SetInFile(_config.Path, ["window", "new-workspace-page"], show);
            Log.Write($"config: window.new-workspace-page = {show}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"config: couldn't set new-workspace-page: {ex.Message}");
        }
    }

    /// <summary>The page's Browse: Windows' folder dialog, which (unlike FolderPicker) can open in a WSL distro's share.</summary>
    private string? BrowseFolder(string? startIn) =>
        FolderDialog.Pick(WinRT.Interop.WindowNative.GetWindowHandle(this), startIn, "Folder for the new workspace");

    /// <summary>A shell reported a folder: it goes on the recent list the page offers.</summary>
    private static void NoteFolder(TerminalTab pane)
    {
        if (ShellDirectory.Parse(pane.Control.ReportedDirectory) is { } path)
        {
            App.Instance.RecentFolders.Note(new RecentFolder(path, pane.Profile.Id, pane.Control.WslDistro, DateTimeOffset.UtcNow));
        }
    }
}
