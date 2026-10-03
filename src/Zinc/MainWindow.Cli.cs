using Zinc.Cli;
using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

// zn's commands for this window (App finds the target first: App.Cli.cs). They go through what keys, menus and the
// palette use, so a command behaves like its key: close confirmation included.
public sealed partial class MainWindow
{
    /// <summary>A window zn asked for: one workspace running a profile (or a program), maybe in a folder.</summary>
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

    /// <summary>The workspace shown; null while the new-workspace page is.</summary>
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
                if (!request.Force && !await ConfirmClose(AllTabs().SelectMany(t => t.Workspace.Tabs).ToList()))
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
                if (!request.Force && !await ConfirmClose(tab.Workspace.Tabs.ToList()))
                {
                    return NotClosed();
                }

                CloseTabNow(tab);
                return CliReply.Done();
            case "pane close":
                if (!request.Force && !await ConfirmClose(group.Tabs.ToList()))
                {
                    return NotClosed();
                }

                foreach (var closing in group.Tabs.ToList())
                {
                    ClosePane(tab, closing);
                }

                return CliReply.Done();
            case "terminal close":
                if (!request.Force && !await ConfirmClose([pane]))
                {
                    return NotClosed();
                }

                ClosePane(tab, pane);
                return CliReply.Done();
            case "pane split":
                return CliSplit(request, tab, group, pane);
            case "terminal new":
                return CliNewTerminal(request, tab, group, pane);
            case "action":
                if (Actions.Parse(request.Action!, out var error) is not { } invocation)
                {
                    return CliReply.Fail(CliCodes.Usage, $"No action \"{request.Action}\" ({error}). " +
                        "The README's key table lists them, and Settings > Keyboard.");
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
    /// A new tab in <paramref name="group"/>, with any profile (else the terminal's, as New tab does). With tab groups
    /// off, panes hold one terminal: it opens as a workspace instead, as New tab does then.
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
        if (!_config.Global.TabGroups)
        {
            FocusActive(AddWorkspace(new Workspace<TerminalTab>(pane), color: AutoColor(folder)));
            return CliReply.Done(pane.Id);
        }

        Wire(pane, tab);
        group.Add(pane);
        ShowGroupTab(tab, pane);
        return CliReply.Done(pane.Id);
    }

    /// <summary>A folder zn sent (a full Windows path; from WSL, the distro's share) in the profile's terms; null if none.</summary>
    private static WorkspaceFolder? FolderFor(string? cwd, Profile profile) =>
        cwd == null ? null : WorkspaceFolder.FromWindows(cwd, ShellEnvironment.WslDistroOf(ShellResolver.ForProfile(profile.Config)));

    /// <summary>How a profile, or a program under it, starts in <paramref name="folder"/> (null: the profile's usual one).</summary>
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
