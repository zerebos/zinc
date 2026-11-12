using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Zinc.Input;
using Zinc.Session;
using Zinc.Terminal;
using Windows.Graphics;

namespace Zinc;

// Remembering windows between launches: a window's saved form (SessionState) and reopening one
// from it. App decides when to save and restore.
public sealed partial class MainWindow
{
    /// <summary>
    /// A window reopened from a saved session. Check <see cref="HasWorkspaces"/>, because tabs may fail to reopen.
    /// </summary>
    internal MainWindow(ConfigFile config, Keybindings keybindings, WindowState saved)
        : this(config, keybindings, quick: false)
    {
        foreach (var ws in saved.Workspaces)
        {
            if (SessionLayout.Restore(ws.Layout, OpenSaved) is { } workspace)
            {
                AddWorkspace(workspace, ws.Title, ParseColor(ws.Color), folder: ws.Folder, icon: ws.Icon);
            }
        }

        if (!HasWorkspaces)
        {
            return;
        }

        // Selected again once the TabView has loaded, because a selection set before that is lost,
        // and the window would open on the last workspace added instead of the one you left.
        var selected = Tabs.TabItems[Math.Clamp(saved.Selected, 0, Tabs.TabItems.Count - 1)];
        Tabs.SelectedItem = selected;
        void Reselect(object sender, RoutedEventArgs e)
        {
            Tabs.Loaded -= Reselect;
            if (Tabs.TabItems.Contains(selected) && !ReferenceEquals(Tabs.SelectedItem, selected))
            {
                Log.Write($"session: reselecting workspace {Tabs.TabItems.IndexOf(selected) + 1} (the TabView picked {Tabs.SelectedIndex + 1})");
                Tabs.SelectedItem = selected;
            }
        }

        Tabs.Loaded += Reselect;
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

    /// <summary>This window's saved form, or null if it has nothing to reopen.</summary>
    internal WindowState? CaptureState()
    {
        // Workspaces of consoles Windows handed over can't be reopened.
        var tabs = AllTabs().Where(t => !t.Workspace.Tabs.All(p => p.HandedOver)).ToList();
        var workspaces = tabs.Select(tab => new WorkspaceState(tab.CustomTitle, FormatColor(tab.Color),
            SessionLayout.Capture(tab.Workspace, DescribeTab), tab.Folder, tab.Icon)).ToList();
        if (workspaces.Count == 0)
        {
            return null;
        }

        // The normal bounds, not the current ones, because Windows parks a minimized window at
        // (-32000, -32000) and 160 x 39, and a window restored there is invisible and too small
        // for its terminals to start. Minimized windows come back restored.
        var b = _normalBounds;
        var maximized = _maximized;
        // Which workspace was shown (a new-workspace page isn't saved, so it's the one before it).
        var shown = SelectedTab is { } selected ? tabs.IndexOf(selected) : tabs.Count - 1;
        return new WindowState(b.X, b.Y, b.Width, b.Height, maximized, Math.Max(0, shown), workspaces, CaptureChrome());
    }

    /// <summary>Where the window is when neither minimized nor maximized (what a session saves).</summary>
    private RectInt32 _normalBounds;

    /// <summary>
    /// Whether the window is maximized. It's kept here because a closing window's AppWindow is gone when it's saved as
    /// closed.
    /// </summary>
    private bool _maximized;

    /// <summary>The workspaces' titles, in order (what a closed window is called).</summary>
    internal IReadOnlyList<string> WorkspaceTitles => [.. AllTabs().Select(t => t.Header.Title)];

    private void TrackNormalBounds()
    {
        void Note()
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } presenter)
            {
                _maximized = presenter.State == OverlappedPresenterState.Maximized; // while minimized, it stays as it was before
            }

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
    /// Whether saved bounds are usable, meaning a sensible size on a screen that exists now. Anything
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
        new(pane.Profile.Id, pane.Command, pane.WorkingDirectory, ShellDirectory.Parse(pane.Control.ReportedDirectory), pane.ColorScheme,
            pane.CustomTitle);

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
        TerminalTab pane;
        if (saved.ColorScheme is { } scheme && _config.Themes.Apply(profile.Config, scheme) is { } themed)
        {
            pane = CreatePane(profile with { Config = themed }, command, directory, recent: false);
            pane.ColorScheme = scheme;
        }
        else
        {
            pane = CreatePane(profile, command, directory, recent: false);
        }

        pane.CustomTitle = saved.Title;
        return pane;
    }

    private static string? FormatColor(Windows.UI.Color? color) => color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;

    private static Windows.UI.Color? ParseColor(string? s) =>
        s != null && Config.TryColor(s, out var c) ? Windows.UI.Color.FromArgb(0xFF, c.R, c.G, c.B) : null;
}
