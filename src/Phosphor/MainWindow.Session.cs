using Microsoft.UI.Windowing;
using Phosphor.Input;
using Phosphor.Session;
using Phosphor.Terminal;
using Windows.Graphics;

namespace Phosphor;

// Remembering windows between launches: a window's saved form (SessionState) and reopening one
// from it. App decides when to save and restore.
public sealed partial class MainWindow
{
    /// <summary>A window reopened from a saved session (check <see cref="HasWorkspaces"/>: tabs may fail to reopen).</summary>
    internal MainWindow(ConfigFile config, Keybindings keybindings, WindowState saved)
        : this(config, keybindings, quick: false)
    {
        foreach (var ws in saved.Workspaces)
        {
            if (SessionLayout.Restore(ws.Layout, OpenSaved) is { } workspace)
            {
                AddWorkspace(workspace, ws.Title, ParseColor(ws.Color));
            }
        }

        if (!HasWorkspaces)
        {
            return;
        }

        Tabs.SelectedIndex = Math.Clamp(saved.Selected, 0, Tabs.TabItems.Count - 1);
        if (saved.Width > 0 && saved.Height > 0)
        {
            AppWindow.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
        }

        if (saved.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        ReplayTestKeys();
    }

    internal bool HasWorkspaces => Tabs.TabItems.Count > 0;

    /// <summary>This window's saved form; null if it has nothing to reopen.</summary>
    internal WindowState? CaptureState()
    {
        var workspaces = AllTabs().Select(tab => new WorkspaceState(tab.CustomTitle, FormatColor(tab.Color),
            SessionLayout.Capture(tab.Workspace, DescribeTab))).ToList();
        if (workspaces.Count == 0)
        {
            return null;
        }

        var (pos, size) = (AppWindow.Position, AppWindow.Size);
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        return new WindowState(pos.X, pos.Y, size.Width, size.Height, maximized, Math.Max(0, Tabs.SelectedIndex), workspaces);
    }

    private static TabState DescribeTab(TerminalTab pane) =>
        new(pane.Profile.Id, pane.Command, pane.WorkingDirectory, ShellDirectory.Parse(pane.Control.ReportedDirectory));

    /// <summary>
    /// Reopens a saved tab: its profile (the default one if that's gone), its command, in the folder
    /// its shell last reported when usable, else where it first started.
    /// </summary>
    private TerminalTab? OpenSaved(TabState saved)
    {
        var profile = _config.FindProfile(saved.Profile) ?? _config.DefaultProfile;
        var command = string.IsNullOrWhiteSpace(saved.Command) ? ShellResolver.ForProfile(profile.Config) : saved.Command;
        string? directory = saved.StartDirectory;
        if (saved.Directory is { } reported && ShellDirectory.LaunchIn(command, reported) is { } launch)
        {
            command = launch.Command;
            directory = launch.WorkingDirectory ?? directory;
        }

        if (directory == null || !Directory.Exists(directory))
        {
            directory = profile.Config.ResolveWorkingDirectory();
        }

        Log.Write($"session: reopening {profile.Name}: {command} in {directory}");
        return CreatePane(profile, command, directory);
    }

    private static string? FormatColor(Windows.UI.Color? color) => color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;

    private static Windows.UI.Color? ParseColor(string? s) =>
        s != null && Config.TryColor(s, out var c) ? Windows.UI.Color.FromArgb(0xFF, c.R, c.G, c.B) : null;
}
