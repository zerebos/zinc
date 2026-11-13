using Zinc.Cli;
using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

// zn's commands for this window (App.Cli.cs finds the target first). They go through what keys, menus and the
// palette use, so a command behaves like its key, close confirmation included.
public sealed partial class MainWindow
{
    /// <summary>A window zn asked for, with one workspace running a profile (or a program), maybe in a folder.</summary>
    internal sealed record CliStart(Profile Profile, string? Cwd, string? CommandLine);

    internal MainWindow(ConfigFile config, Keybindings keybindings, CliStart start)
        : this(config, keybindings, quick: false)
    {
        var folder = FolderFor(start.Cwd, start.Profile);
        var (command, directory) = CliLaunchFor(start.Profile, folder, start.CommandLine);
        var tab = AddWorkspace(new Workspace<TerminalTab>(CreatePane(start.Profile, command, directory)), folder?.Name,
            AutoColor(folder), folder: folder);
        FocusActive(tab);
    }

    /// <summary>The workspaces, in order (a new-workspace page among them isn't one).</summary>
    internal IEnumerable<WorkspaceTab> Workspaces => AllTabs();

    /// <summary>The workspace shown, or null while the new-workspace page is shown.</summary>
    internal WorkspaceTab? ShownWorkspace => SelectedTab;

    internal bool IsQuick => _quick;

    internal async Task<CliReply> HandleCli(CliRequest request, CliTarget target)
    {
        if (_windowClosed)
        {
            return CliReply.Fail(CliCodes.Failed, $"Window {Id} is closing.");
        }

        var (tab, group, pane) = (target.Workspace, target.Pane, target.Terminal);
        switch (request.Cmd)
        {
            case "window focus":
                BringForward();
                return CliReply.Done();
            case "window close":
                if (!request.Force && !await ConfirmClose(AllTabs().SelectMany(t => t.Workspace.Tabs).ToList(), "window", severalTabs: false))
                {
                    return NotClosed();
                }

                CloseWithoutAsking();
                return CliReply.Done();
            case "workspace new":
                if (App.Instance.CliProfile(request.Profile, _config.DefaultProfile) is not { } profile)
                {
                    return App.NoProfile(request.Profile!);
                }

                var folder = FolderFor(request.Cwd, profile);
                var (command, directory) = CliLaunchFor(profile, folder, request.CommandLine);
                var created = CreatePane(profile, command, directory);
                AddWorkspace(new Workspace<TerminalTab>(created), request.Name ?? folder?.Name, AutoColor(folder), folder: folder);
                BringForward();
                return CliReply.Done(created.Id);
        }

        if (tab == null || group == null || pane == null)
        {
            return CliReply.Fail(CliCodes.Failed, $"Window {Id} shows the new-workspace page: there's no terminal there.");
        }

        switch (request.Cmd)
        {
            case "workspace focus" or "pane focus" or "terminal focus":
                BringToPane(tab, pane);
                return CliReply.Done();
            case "workspace close":
                if (!request.Force && !await ConfirmClose(tab.Workspace.Tabs.ToList(), "workspace", severalTabs: false))
                {
                    return NotClosed();
                }

                CloseWorkspaceNow(tab);
                return CliReply.Done();
            case "pane close":
                if (!request.Force && !await ConfirmClose(group.Tabs.ToList(), "pane", severalTabs: false))
                {
                    return NotClosed();
                }

                foreach (var closing in group.Tabs.ToList())
                {
                    ClosePaneTab(tab, closing);
                }

                return CliReply.Done();
            case "terminal close":
                if (!request.Force && !await ConfirmClose([pane], "tab"))
                {
                    return NotClosed();
                }

                ClosePaneTab(tab, pane);
                return CliReply.Done();
            case "terminal send":
                return pane.Control.SendText(request.Text ?? "", request.Paste)
                    ? CliReply.Done()
                    : CliReply.Fail(CliCodes.Failed, $"Nothing runs in terminal {pane.Id} (its program ended).");
            case "terminal read":
                return pane.Control.ReadText(request.Lines, request.Ansi) is { } text
                    ? new CliReply { Ok = true, Text = text }
                    : CliReply.Fail(CliCodes.Failed, $"Terminal {pane.Id} hasn't started yet: it starts once it's shown.");
            case "terminal title":
                pane.CustomTitle = string.IsNullOrEmpty(request.Title) ? null : request.Title;
                UpdateTitle(tab);
                UpdateFocusBorders(tab); // its pane's strip shows it too
                return CliReply.Done();
            case "terminal move":
                return CliMoveTerminal(request, group, pane);
            case "workspace rename":
                tab.CustomTitle = string.IsNullOrEmpty(request.Name) ? null : request.Name;
                UpdateTitle(tab);
                RefreshWorkspaceList();
                return CliReply.Done();
            case "workspace color":
                if (!TryWorkspaceColor(request.Color!, out var color))
                {
                    return CliReply.Fail(CliCodes.Usage, $"No color \"{request.Color}\": " +
                        string.Join(", ", WorkspaceColors.All.Select(c => c.Name.ToLowerInvariant())) + ", #RRGGBB or none.");
                }

                SetTabColor(tab, color);
                return CliReply.Done();
            case "workspace icon":
                var icon = request.Icon!.Equals("none", StringComparison.OrdinalIgnoreCase) ? null
                    : WorkspaceIcons.All.FirstOrDefault(i => i.Name.Equals(request.Icon, StringComparison.OrdinalIgnoreCase)).Name;
                if (icon == null && !request.Icon.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return CliReply.Fail(CliCodes.Usage, $"No icon \"{request.Icon}\": " +
                        string.Join(", ", WorkspaceIcons.All.Select(i => i.Name)) + " or none.");
                }

                SetWorkspaceIcon(tab, icon);
                return CliReply.Done();
            case "workspace move":
                return CliMoveWorkspace(request, tab);
            case "pane split":
                return CliSplit(request, tab, group, pane);
            case "terminal new":
                return CliNewTerminal(request, tab, group, pane);
            case "action":
                if (Actions.Parse(request.Action!, out var error) is not { } invocation)
                {
                    return CliReply.Fail(CliCodes.Usage, $"No action \"{request.Action}\" ({error}). " +
                        "Settings > Keyboard lists them.");
                }

                return pane.Control.Invoke(invocation)
                    ? CliReply.Done()
                    : CliReply.Fail(CliCodes.Failed, $"\"{request.Action}\" had nothing to do in terminal {pane.Id}.");
            default:
                return CliReply.Fail(CliCodes.Usage, $"This Zinc doesn't know \"{request.Cmd}\": is zn newer than Zinc?");
        }
    }

    /// <summary>A new pane beside <paramref name="group"/>, with the given profile (else the terminal's, as Split does).</summary>
    private CliReply CliSplit(CliRequest request, WorkspaceTab tab, TabGroup<TerminalTab> group, TerminalTab origin)
    {
        if (App.Instance.CliProfile(request.Profile, origin.Profile) is not { } profile)
        {
            return App.NoProfile(request.Profile!);
        }

        var where = request.Direction switch
        {
            "left" => PaneDirection.Left,
            "up" => PaneDirection.Up,
            "down" => PaneDirection.Down,
            _ => PaneDirection.Right,
        };
        var (command, directory) = CliLaunchFor(profile, FolderFor(request.Cwd, profile) ?? tab.Folder, request.CommandLine);
        var pane = CreatePane(profile, command, directory);
        Wire(pane, tab);
        tab.Workspace.Split(group, where, pane, request.Size is { } size ? CliCommandLine.ParseSize(size) ?? 0.5 : 0.5);
        Rebuild(tab);
        return CliReply.Done(pane.Id);
    }

    /// <summary>
    /// A new tab in <paramref name="group"/>, with any profile (else the terminal's, as New tab does). With pane tabs
    /// off, a pane holds one terminal, so the new one opens as a workspace instead, as New tab does then.
    /// </summary>
    private CliReply CliNewTerminal(CliRequest request, WorkspaceTab tab, TabGroup<TerminalTab> group, TerminalTab origin)
    {
        if (App.Instance.CliProfile(request.Profile, origin.Profile) is not { } profile)
        {
            return App.NoProfile(request.Profile!);
        }

        var folder = FolderFor(request.Cwd, profile);
        var (command, directory) = CliLaunchFor(profile, folder ?? tab.Folder, request.CommandLine);
        var pane = CreatePane(profile, command, directory);
        if (!_config.Global.PaneTabs)
        {
            FocusActive(AddWorkspace(new Workspace<TerminalTab>(pane), color: AutoColor(folder)));
            return CliReply.Done(pane.Id);
        }

        Wire(pane, tab);
        group.Add(pane);
        ShowGroupTab(tab, pane);
        return CliReply.Done(pane.Id);
    }

    /// <summary>
    /// Moves a terminal into another pane (in any window) as its newest tab, shown, the way a tab dropped on a pane's strip
    /// moves.
    /// </summary>
    private CliReply CliMoveTerminal(CliRequest request, TabGroup<TerminalTab> group, TerminalTab pane)
    {
        if (!_config.Global.PaneTabs)
        {
            return CliReply.Fail(CliCodes.Failed, "Tab groups are off ([window] pane-tabs), so a pane holds one terminal.");
        }

        if (App.Instance.FindCliPane(request.ToPane!.Value) is not { Workspace: { } toTab, Pane: { } toGroup } to)
        {
            return CliReply.Fail(CliCodes.Failed, $"No pane {request.ToPane}. zn list shows the open ones.");
        }

        if (toGroup == group)
        {
            return CliReply.Fail(CliCodes.Failed, $"Terminal {pane.Id} is in pane {group.Id} already.");
        }

        to.Window.MoveTerminalTab(this, pane, toTab, toGroup, null);
        to.Window.FocusActive(toTab);
        return CliReply.Done();
    }

    /// <summary>Moves a workspace to another window (as its last workspace, shown) or a new one, as dragging its tab there does.</summary>
    private CliReply CliMoveWorkspace(CliRequest request, WorkspaceTab tab)
    {
        if (request.ToWindow == "new")
        {
            MoveToNewWindow(tab);
            return CliReply.Done();
        }

        if (!int.TryParse(request.ToWindow, out var id) || App.Instance.FindCliWindow(id) is not { } target)
        {
            return CliReply.Fail(CliCodes.Failed, $"No window {request.ToWindow}. zn list shows the open ones.");
        }

        if (target == this)
        {
            return CliReply.Fail(CliCodes.Failed, $"Workspace {tab.Id} is in window {Id} already.");
        }

        var transfer = DetachWorkspace(tab);
        target.FocusActive(target.AddWorkspace(transfer.Workspace, transfer.CustomTitle, transfer.Color, folder: transfer.Folder,
            icon: transfer.Icon, id: transfer.Id));
        CloseIfEmpty();
        target.BringForward();
        return CliReply.Done();
    }

    /// <summary>A workspace color by name (red…pink), as #RRGGBB, or none (null).</summary>
    private static bool TryWorkspaceColor(string text, out Windows.UI.Color? color)
    {
        color = null;
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (WorkspaceColors.All.FirstOrDefault(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase)) is { Name: not null } named)
        {
            color = WorkspaceLookPickers.ToColor(named.Rgb);
            return true;
        }

        if (text.StartsWith('#') && Config.TryColor(text, out var rgb))
        {
            color = Windows.UI.Color.FromArgb(0xFF, rgb.R, rgb.G, rgb.B);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A folder zn sent (a full Windows path, which from WSL is the distro's share) in the profile's terms, or null if none.
    /// </summary>
    private static WorkspaceFolder? FolderFor(string? cwd, Profile profile) =>
        cwd == null ? null : WorkspaceFolder.FromWindows(cwd, ShellEnvironment.WslDistroOf(ShellResolver.ForProfile(profile.Config)));

    /// <summary>How a profile, or a program under it, starts in <paramref name="folder"/> (null for the profile's usual one).</summary>
    private static (string Command, string Directory) CliLaunchFor(Profile profile, WorkspaceFolder? folder, string? program)
    {
        var (command, directory) = StartIn(folder, profile);
        return program == null ? (command, directory) : (CliLaunch.WithProgram(command, program), directory);
    }

    private void BringForward()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        ForceForeground();
        if (SelectedTab is { } shown)
        {
            FocusActive(shown);
        }
    }

    private static CliReply NotClosed() => CliReply.Fail(CliCodes.Failed, "Not closed: a command is still running there, and closing was cancelled.");
}
