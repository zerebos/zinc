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
        RestoreChrome(saved.Chrome);
        if (Plausible(saved))
        {
            AppWindow.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
        }
        else
        {
            Log.Write($"session: ignoring saved bounds {saved.X},{saved.Y} {saved.Width}x{saved.Height} (off-screen or too small)");
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

        // The normal bounds, not the current ones: minimized, Windows parks a window at
        // (-32000, -32000) and 160 x 39, and a window restored there is invisible and too small
        // for its terminals to start (2026-09-27). Minimized windows come back restored.
        var b = _normalBounds;
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        return new WindowState(b.X, b.Y, b.Width, b.Height, maximized, Math.Max(0, Tabs.SelectedIndex), workspaces, CaptureChrome());
    }

    /// <summary>Where the window is when neither minimized nor maximized (what a session saves).</summary>
    private RectInt32 _normalBounds;

    private void TrackNormalBounds()
    {
        void Note()
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized or OverlappedPresenterState.Maximized })
            {
                return; // keep the bounds from before
            }

            _normalBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        }

        Note();
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
            {
                Note();
            }
        };
    }

    /// <summary>
    /// Whether saved bounds are usable: a sensible size, on a screen that exists now. Anything
    /// else (a monitor since unplugged, a bad save) is ignored and Windows places the window.
    /// </summary>
    private static bool Plausible(WindowState saved)
    {
        if (saved.Width < 200 || saved.Height < 150)
        {
            return false;
        }

        var rect = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
        return DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is { } area &&
               saved.X < area.OuterBounds.X + area.OuterBounds.Width - 40 &&
               saved.Y < area.OuterBounds.Y + area.OuterBounds.Height - 40 &&
               saved.X + saved.Width > area.OuterBounds.X + 40 &&
               saved.Y >= area.OuterBounds.Y - 8;
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
