using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Zinc.Commands;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

// A workspace's own icon and folder, set from its menu or the palette (the new-workspace page sets
// them when it starts one).
public sealed partial class MainWindow
{
    private void SetWorkspaceIcon(WorkspaceTab tab, string? name)
    {
        tab.Icon = name;
        tab.Header.SetIcon(WorkspaceIcons.Glyph(name));
        RefreshWorkspaceList();
    }

    /// <summary>The icon grid, under the workspace's tab (or its sidebar entry).</summary>
    private void ShowIconPicker(WorkspaceTab tab)
    {
        var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };
        flyout.Content = WorkspaceLookPickers.Icons(tab.Icon, name =>
        {
            SetWorkspaceIcon(tab, name);
            flyout.Hide();
        });
        flyout.ShowAt(AnchorOf(tab));
    }

    /// <summary>The palette's icon question: each one previews on the tab; Esc puts the old one back.</summary>
    private void AskWorkspaceIcon(WorkspaceTab tab)
    {
        var before = tab.Icon;
        var choices = new List<PaletteCommand> { new("No icon (the profile's)", null, () => SetWorkspaceIcon(tab, null), Remember: false) };
        choices.AddRange(WorkspaceIcons.All.Select(i => new PaletteCommand(i.Name, null, () => SetWorkspaceIcon(tab, i.Name), Remember: false)));
        AskPaletteArgument(new PaletteArgument("Icon for this workspace", choices,
            Highlight: choice => tab.Header.SetIcon(WorkspaceIcons.Glyph(choice.Title)),
            Cancel: () => SetWorkspaceIcon(tab, before)));
    }

    /// <summary>"Set folder…": typed like "New tab in folder…" (relative to where the focused shell is; ~ is home).</summary>
    private void AskWorkspaceFolder(WorkspaceTab tab, string? prompt = null) =>
        AskPaletteArgument(new PaletteArgument(prompt ?? "Folder for this workspace: its new tabs and splits start there",
            Submit: typed => SetWorkspaceFolder(tab, typed)));

    private void SetWorkspaceFolder(WorkspaceTab tab, string typed)
    {
        var pane = tab.Active;
        var current = ShellDirectory.Parse(pane.Control.ReportedDirectory) ?? pane.Profile.Config.ResolveWorkingDirectory();
        var resolved = ShellDirectory.ResolveTyped(typed, pane.Command, current);
        var windows = resolved is { Length: >= 2 } r && (r[1] == ':' || r.StartsWith(@"\\", StringComparison.Ordinal));
        var folder = resolved == null ? null : new WorkspaceFolder(resolved, windows ? null : pane.Control.WslDistro);
        if (folder?.LaunchFor(pane.Command, pane.Control.WslDistro) is not { } launch ||
            launch.WorkingDirectory is { } directory && !Directory.Exists(directory))
        {
            AskWorkspaceFolder(tab, $"No folder {typed} for {pane.Profile.Name}: try another");
            return;
        }

        // Named after its folder, unless you named it yourself; with colors by folder, colored after
        // it too, unless you colored it yourself (a color that's none or the old folder's is automatic).
        if (tab.CustomTitle == null || tab.CustomTitle == tab.Folder?.Name)
        {
            tab.CustomTitle = folder.Name;
        }

        if (_config.Global.WorkspaceColors == WorkspaceColorMode.Folder && (tab.Color == null || tab.Color == FolderColor(tab.Folder)))
        {
            SetTabColor(tab, FolderColor(folder));
        }

        tab.Folder = folder;
        UpdateTitle(tab);
        RefreshWorkspaceList();
        Log.Write($"workspace folder: {folder.Path}");
    }

    /// <summary>A folder's color (<see cref="WorkspaceColors.ForFolder"/>); null without a folder.</summary>
    private static Windows.UI.Color? FolderColor(WorkspaceFolder? folder) =>
        folder == null ? null : WorkspaceLookPickers.ToColor(WorkspaceColors.All[WorkspaceColors.ForFolder(folder.Path)].Rgb);

    private void ClearWorkspaceFolder(WorkspaceTab tab)
    {
        if (tab.Folder is not { } folder)
        {
            return;
        }

        if (tab.CustomTitle == folder.Name)
        {
            tab.CustomTitle = null; // its shell names it again
        }

        if (_config.Global.WorkspaceColors == WorkspaceColorMode.Folder && tab.Color == FolderColor(folder))
        {
            SetTabColor(tab, null); // the folder's color goes with it (one you chose stays)
        }

        tab.Folder = null;
        UpdateTitle(tab);
        RefreshWorkspaceList();
        Log.Write("workspace folder: cleared");
    }
}
