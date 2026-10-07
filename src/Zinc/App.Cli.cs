using Microsoft.UI.Dispatching;
using Zinc.Cli;
using Zinc.Panes;
using Zinc.Terminal;
using TerminalTab = Zinc.MainWindow.TerminalTab;
using WorkspaceTab = Zinc.MainWindow.WorkspaceTab;

namespace Zinc;

/// <summary>What a zn command acts on: a window, and (unless it shows the new-workspace page) a workspace, pane and terminal.</summary>
internal sealed record CliTarget(MainWindow Window, WorkspaceTab? Workspace, TabGroup<TerminalTab>? Pane, TerminalTab? Terminal)
{
    /// <summary>Found by nothing the command said (run outside Zinc, no flags): zn tells the user what it picked.</summary>
    public bool Defaulted { get; init; }
}

// zn's requests (CliServer), handled on the UI thread: finding what a command means, then the window does it.
public partial class App
{
    private void StartCliServer()
    {
        var name = _sessionPath != null ? CliPipe.ForSession(_sessionPath) : CliPipe.ForProcess(Environment.ProcessId);
        var dq = DispatcherQueue.GetForCurrentThread();
        CliServer.Start(name, request =>
        {
            var done = new TaskCompletionSource<CliReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dq.TryEnqueue(() => RunCli(request, done)))
            {
                done.TrySetResult(CliReply.Fail(CliCodes.Failed, "Zinc is closing."));
            }

            return done.Task;
        });
    }

    /// <summary>
    /// <c>[cli] path</c>: zn's folder on the user's PATH (this copy's: installs are versioned folders), or off it. At every
    /// start and reload; never in test runs (ZINC_CONFIG), as they never touch the sign-in entry either.
    /// </summary>
    private static void ApplyZnPath(bool on)
    {
        if (Environment.GetEnvironmentVariable("ZINC_CONFIG") is { Length: > 0 })
        {
            return;
        }

        try
        {
            var path = Zinc.Pty.UserPath.ForUser();
            var folder = on ? CliServer.ZnFolder : null;
            if (on && folder == null)
            {
                Log.Write("cli: no zn next to this zinc.exe to put on PATH");
                return;
            }

            if (path.Apply(folder))
            {
                Log.Write(folder != null ? $"cli: zn on PATH: {folder}" : "cli: zn taken off PATH");
                _ = Task.Run(Zinc.Pty.UserPath.Announce); // a broadcast: kept off the UI thread
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Write($"cli: couldn't change PATH: {ex.Message}");
        }
    }

    /// <summary>Every exception is caught here: this runs as queued work, where one would end the process.</summary>
    private async void RunCli(CliRequest request, TaskCompletionSource<CliReply> done)
    {
        try
        {
            done.TrySetResult(await HandleCli(request));
        }
        catch (Exception ex)
        {
            Log.Crash($"zn {request.Cmd}", ex);
            done.TrySetResult(CliReply.Fail(CliCodes.Failed, $"Zinc failed: {ex.Message}"));
        }
    }

    private async Task<CliReply> HandleCli(CliRequest request)
    {
        // What and where, never what's typed (session 2's terminal send leaves its text out).
        Log.Write($"cli: {request.Cmd}{(request.Direction is { } d ? " " + d : "")} from " +
                  (request.Caller is { } caller ? $"terminal {caller}" : "outside Zinc"));
        if (request.Cmd == "version")
        {
            return new CliReply { Ok = true, Version = TerminalSession.Version.Replace("zinc ", "") };
        }

        if (!_config.Global.RemoteControl)
        {
            return CliReply.Fail(CliCodes.Off,
                "Remote control is off. Turn it on in Settings > Terminal > Command line, or set [cli] remote-control = true.");
        }

        if (_quitting)
        {
            return CliReply.Fail(CliCodes.Failed, "Zinc is quitting.");
        }

        switch (request.Cmd)
        {
            case "list" or "window list" or "workspace list" or "pane list" or "terminal list":
                return new CliReply { Ok = true, Tree = CliTree() };
            case "window new":
                if (CliProfile(request.Profile, _config.DefaultProfile) is not { } profile)
                {
                    return NoProfile(request.Profile!);
                }

                var window = Track(new MainWindow(_config, _keybindings, new MainWindow.CliStart(profile, request.Cwd, request.CommandLine)));
                return CliReply.Done(window.Workspaces.FirstOrDefault()?.Active.Id);
        }

        if (FindCliTarget(request) is not { } target)
        {
            return CliReply.Fail(CliCodes.Failed, NotFound(request));
        }

        var reply = await target.Window.HandleCli(request, target);
        if (reply.Ok && target.Defaulted)
        {
            reply.Target = Describe(request, target);
        }

        return reply;
    }

    /// <summary>Windows zn can see: the normal ones, then the quick terminal's.</summary>
    private IEnumerable<MainWindow> CliWindows => _quick != null ? _windows.Append(_quick) : _windows;

    /// <summary>A profile by id or name (any case); <paramref name="fallback"/> when none was named; null if there's no such profile.</summary>
    internal Profile? CliProfile(string? name, Profile fallback) =>
        name == null ? fallback
        : _config.FindProfile(name) ?? _config.Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal static CliReply NoProfile(string name) =>
        CliReply.Fail(CliCodes.Usage, $"No profile \"{name}\". Settings > Profiles lists them; use a profile's id (wsl:Fedora) or name.");

    /// <summary>
    /// What a command means: the flag's thing (the biggest picks its focused parts), else zn's own terminal, else the
    /// window used last. Null if a flag names something that isn't open.
    /// </summary>
    private CliTarget? FindCliTarget(CliRequest request)
    {
        foreach (var window in CliWindows)
        {
            foreach (var workspace in window.Workspaces)
            {
                foreach (var group in workspace.Workspace.Groups)
                {
                    foreach (var terminal in group.Tabs)
                    {
                        if (request.Terminal is { } id ? terminal.Id == id
                            : request.Pane is { } pane ? group.Id == pane && terminal == group.Active
                            : request.Workspace is { } ws ? workspace.Id == ws && terminal == workspace.Active
                            : request.Window is { } w ? window.Id == w && workspace == window.ShownWorkspace && terminal == workspace.Active
                            : request.Caller is { } caller && terminal.Id == caller)
                        {
                            return new CliTarget(window, workspace, group, terminal);
                        }
                    }
                }
            }
        }

        if (request.Terminal != null || request.Pane != null || request.Workspace != null)
        {
            return null;
        }

        if (request.Window is { } windowId)
        {
            // A window showing the new-workspace page: the window alone.
            return CliWindows.FirstOrDefault(w => w.Id == windowId) is { } only ? new CliTarget(only, null, null, null) : null;
        }

        if (request.Caller != null)
        {
            return null; // zn's terminal isn't in this Zinc (closed, or a ZINC_PIPE from elsewhere)
        }

        if (_windows.LastOrDefault() is not { } front)
        {
            return null;
        }

        var shown = front.ShownWorkspace;
        return new CliTarget(front, shown, shown?.Workspace.ActiveGroup, shown?.Active) { Defaulted = true };
    }

    /// <summary>Pane <paramref name="id"/>, wherever it is (terminal move's destination).</summary>
    internal CliTarget? FindCliPane(int id) => FindCliTarget(new CliRequest { Pane = id });

    internal MainWindow? FindCliWindow(int id) => CliWindows.FirstOrDefault(w => w.Id == id);

    private string NotFound(CliRequest request) =>
        request.Terminal is { } t ? $"No terminal {t}. zn list shows the open ones."
        : request.Pane is { } p ? $"No pane {p}. zn list shows the open ones."
        : request.Workspace is { } s ? $"No workspace {s}. zn list shows the open ones."
        : request.Window is { } w ? $"No window {w}. zn list shows the open ones."
        : request.Caller is { } c ? $"zn's terminal ({c}) isn't in this Zinc. Name one with --terminal; zn list shows them."
        : "No window is open. zn window new opens one.";

    /// <summary>"pane 3 in window 1": what a command from outside Zinc acted on.</summary>
    private static string Describe(CliRequest request, CliTarget target)
    {
        var window = $"window {target.Window.Id}";
        return request.Cmd.Split(' ')[0] switch
        {
            "window" => window,
            _ when request.Cmd == "workspace new" => window,
            "workspace" when target.Workspace is { } ws => $"workspace {ws.Id} in {window}",
            "pane" when target.Pane is { } pane => $"pane {pane.Id} in {window}",
            _ when request.Cmd == "terminal new" && target.Pane is { } pane => $"pane {pane.Id} in {window}",
            _ when target.Terminal is { } terminal => $"terminal {terminal.Id} in {window}",
            _ => window,
        };
    }

    /// <summary>Everything open, for list; "focused" marks the way to the terminal keys go to in the window used last.</summary>
    private CliTree CliTree()
    {
        var front = _windows.LastOrDefault();
        var tree = new CliTree();
        foreach (var window in CliWindows.OrderBy(w => w.Id))
        {
            var shown = window.ShownWorkspace;
            tree.Windows.Add(new CliWindow
            {
                Id = window.Id,
                Focused = window == front,
                Quick = window.IsQuick,
                Workspaces = window.Workspaces.Select(workspace =>
                {
                    var active = workspace == shown;
                    var focused = active && window == front;
                    return new CliWorkspace
                    {
                        Id = workspace.Id,
                        Name = workspace.Header.Title,
                        Active = active,
                        Focused = focused,
                        Panes = workspace.Workspace.Groups.Select(group =>
                        {
                            var activePane = group == workspace.Workspace.ActiveGroup;
                            return new CliPane
                            {
                                Id = group.Id,
                                Active = activePane,
                                Focused = focused && activePane,
                                Terminals = group.Tabs.Select(terminal => new CliTerminal
                                {
                                    Id = terminal.Id,
                                    Title = GroupView.TitleOf(terminal),
                                    Folder = terminal.Control.CurrentFolder,
                                    Profile = terminal.Profile.Id,
                                    Active = terminal == group.Active,
                                    Focused = focused && activePane && terminal == group.Active,
                                }).ToList(),
                            };
                        }).ToList(),
                    };
                }).ToList(),
            });
        }

        return tree;
    }
}
