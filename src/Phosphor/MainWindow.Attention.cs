using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Phosphor.Terminal;

namespace Phosphor;

// Shell integration, the attention part: a long command that finishes where you weren't looking
// (another workspace, tab or pane, or another app) puts a dot on its tab, red if it failed, and
// flashes the taskbar button while the window is in the background (or, with "toast", shows a
// Windows notification); the dot clears once you look. Programs' own notifications (OSC 9 / 777)
// get a blue dot the same way, and their progress (OSC 9;4) a ring and the taskbar's progress bar.
// Closing something while the shell says a command is running asks first.
public sealed partial class MainWindow
{
    private bool _confirmingClose;
    private bool _closeConfirmed;

    private void OnCommandFinished(WorkspaceTab tab, TerminalTab pane, ShellCommand command)
    {
        var global = _config.Global;
        if (global.NotifyCommandFinished == NotifyMode.Never ||
            command.Duration is not { } took || took < global.NotifyCommandFinishedAfter || IsLookingAt(tab, pane))
        {
            return;
        }

        var attention = command.Failed ? Attention.Failed : Attention.Finished;
        if (attention > pane.Attention)
        {
            pane.Attention = attention;
        }

        Log.Write($"attention: '{command.Text}' {attention} after {took.TotalSeconds:0}s");
        UpdateFocusBorders(tab);
        var result = command.Failed ? $"failed (exit {command.ExitCode})" : "finished";
        Alert(global.NotifyCommandFinished, tab, pane,
            $"{command.Text ?? "A command"}", $"{char.ToUpperInvariant(result[0])}{result[1..]} after {Elapsed(took)} · {tab.Header.Title}");
    }

    /// <summary>The bell rang (per <c>bell</c>): a dot on the tab where you weren't looking, and a taskbar flash from the background.</summary>
    private void OnBell(WorkspaceTab tab, TerminalTab pane)
    {
        if (IsLookingAt(tab, pane))
        {
            return;
        }

        if (Attention.Bell > pane.Attention)
        {
            pane.Attention = Attention.Bell;
        }

        UpdateFocusBorders(tab);
        if (!_active)
        {
            FlashTaskbarButton();
        }
    }

    /// <summary>A program asked for a notification (OSC 9 / OSC 777).</summary>
    private void OnProgramNotified(WorkspaceTab tab, TerminalTab pane, string title, string body)
    {
        var mode = _config.Global.NotifyPrograms;
        if (mode == NotifyMode.Never || IsLookingAt(tab, pane))
        {
            return;
        }

        if (Attention.Notified > pane.Attention)
        {
            pane.Attention = Attention.Notified;
        }

        UpdateFocusBorders(tab);
        Alert(mode, tab, pane, title.Length > 0 ? title : tab.Header.Title, body);
    }

    /// <summary>
    /// Past the dot: a Windows notification ("toast": while the window is in the background;
    /// "toast-always": whenever you weren't looking), else a taskbar flash while in the background.
    /// Clicking the notification brings you to the pane.
    /// </summary>
    private void Alert(NotifyMode mode, WorkspaceTab tab, TerminalTab pane, string title, string body)
    {
        if (mode == NotifyMode.ToastAlways || (mode == NotifyMode.Toast && !_active))
        {
            App.Instance.Notify(title, body, () => BringToPane(tab, pane));
        }
        else if (!_active)
        {
            FlashTaskbarButton();
        }
    }

    /// <summary>Brings this window forward with <paramref name="pane"/> shown and focused (if it's still here).</summary>
    private void BringToPane(WorkspaceTab tab, TerminalTab pane)
    {
        if (_windowClosed)
        {
            return; // e.g. a notification clicked after its window closed
        }

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        ForceForeground();
        if (!AllTabs().Contains(tab) || tab.Workspace.GroupOf(pane) == null)
        {
            return; // moved or closed since
        }

        Tabs.SelectedItem = tab.Item;
        ShowGroupTab(tab, pane);
    }

    /// <summary>
    /// Brings the window to the front although another app has it. A notification's click doesn't
    /// hand Phosphor the right to take the foreground (Activate alone left the window behind), so
    /// its input is briefly attached to the foreground app's thread, which may.
    /// </summary>
    private void ForceForeground()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        var theirs = GetWindowThreadProcessId(foreground, out _);
        var ours = GetCurrentThreadId();
        var attached = theirs != 0 && theirs != ours && AttachThreadInput(theirs, ours, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(theirs, ours, false);
            }
        }

        Log.Write($"foreground: {(GetForegroundWindow() == hwnd ? "taken" : "refused")} (attached {attached})");
    }

    /// <summary>A program's progress changed in a pane: its tab's ring, and the taskbar's bar.</summary>
    private void OnProgress(WorkspaceTab tab) => UpdateFocusBorders(tab); // which updates the taskbar too

    /// <summary>
    /// The window's taskbar button shows one progress: an error if any pane has one, else the
    /// shown workspace's, else any.
    /// </summary>
    private void UpdateTaskbarProgress()
    {
        if (_windowClosed)
        {
            return; // its AppWindow is gone
        }

        var shown = SelectedTab?.Workspace.Tabs ?? [];
        var all = AllTabs().SelectMany(t => t.Workspace.Tabs).Select(p => p.Control.Progress).Where(p => p.Active).ToList();
        var progress = all.FirstOrDefault(p => p.State == Ghostty.TerminalProgressState.Error) is { Active: true } error ? error
            : shown.Select(p => p.Control.Progress).FirstOrDefault(p => p.Active) is { Active: true } here ? here
            : all.FirstOrDefault();
        TaskbarProgress.Show(WinRT.Interop.WindowNative.GetWindowHandle(this), progress);
    }

    /// <summary>
    /// Whether you're looking at a terminal: its window active, its workspace shown, and it has
    /// focus. A visible pane beside the focused one doesn't count; your attention is elsewhere.
    /// </summary>
    private bool IsLookingAt(WorkspaceTab tab, TerminalTab pane) =>
        _active && SelectedTab == tab && tab.Workspace.Active == pane;

    /// <summary>Clears the dot of what you're now looking at. Called from <see cref="UpdateFocusBorders"/>.</summary>
    private void ClearSeenAttention(WorkspaceTab tab)
    {
        if (_active && SelectedTab == tab)
        {
            tab.Active.Attention = Attention.None;
        }
    }

    /// <summary>A workspace's dot: the most urgent of its terminals'; else a ring for a program's progress (an error first).</summary>
    private static void ShowWorkspaceAttention(WorkspaceTab tab)
    {
        tab.Header.SetAttention(tab.Workspace.Tabs.Select(t => t.Attention).DefaultIfEmpty().Max(),
            tab.Workspace.Tabs.Count(t => t.Attention != Attention.None));
        var progress = tab.Workspace.Tabs.Select(t => t.Control.Progress).Where(p => p.Active)
            .OrderByDescending(p => p.State == Ghostty.TerminalProgressState.Error).FirstOrDefault();
        tab.Header.SetProgress(progress.Active ? progress : TerminalProgress.None);
    }

    private void FlashTaskbarButton()
    {
        if (_windowClosed)
        {
            return; // its AppWindow is gone
        }

        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this),
            Flags = FlashTray | FlashTimerNoForeground, // until the window comes to the front
        };
        FlashWindowEx(ref info);
    }

    // ------------------------------------------------------------ closing

    /// <summary>
    /// Asks before closing terminals the shell says are running a command. True to go ahead
    /// (nothing running, or the user agreed).
    /// </summary>
    private async Task<bool> ConfirmClose(IEnumerable<TerminalTab> panes)
    {
        var running = panes.Select(p => p.Control.RunningCommand).OfType<ShellCommand>().ToList();
        if (running.Count == 0)
        {
            return true;
        }

        if (_confirmingClose || Content?.XamlRoot is not { } root)
        {
            return false; // one question at a time
        }

        _confirmingClose = true;
        try
        {
            var list = string.Join("\n", running.Select(c => c.Text ?? "(a command)"));
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = running.Count == 1 ? "A command is still running" : $"{running.Count} commands are still running",
                Content = $"{list}\n\nClosing stops {(running.Count == 1 ? "it" : "them")}.",
                PrimaryButtonText = "Close",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            // Code after an await resumes as queued work, which bypasses Application.UnhandledException:
            // record it, and don't close anything.
            Log.Crash("asking before a close", ex);
            return false;
        }
        finally
        {
            _confirmingClose = false;
        }
    }

    /// <summary>Closes a terminal tab when you ask to (not when its shell has exited).</summary>
    private async void AskClosePane(WorkspaceTab tab, TerminalTab pane)
    {
        if (await ConfirmClose([pane]))
        {
            Ui.Guard("closing a tab", () => ClosePane(tab, pane));
        }
        else
        {
            FocusActive(tab);
        }
    }

    private async void AskClosePanes(WorkspaceTab tab, IReadOnlyList<TerminalTab> panes)
    {
        if (await ConfirmClose(panes))
        {
            Ui.Guard("closing tabs", () =>
            {
                foreach (var pane in panes)
                {
                    ClosePane(tab, pane);
                }
            });
        }
        else
        {
            FocusActive(tab);
        }
    }

    /// <summary>The window's close button (and Alt+F4): asks if something is running.</summary>
    private void ConfirmWindowClose()
    {
        AppWindow.Closing += async (_, args) =>
        {
            if (_closeConfirmed || _quick)
            {
                return;
            }

            var panes = AllTabs().SelectMany(t => t.Workspace.Tabs).ToList();
            if (panes.All(p => p.Control.RunningCommand == null))
            {
                return;
            }

            args.Cancel = true;
            if (await ConfirmClose(panes))
            {
                _closeConfirmed = true;
                Ui.Guard("closing a window", Close);
            }
            else if (SelectedTab is { } shown)
            {
                FocusActive(shown);
            }
        };
    }

    /// <summary>Closes without the running-command question (quitting has asked already).</summary>
    internal void CloseWithoutAsking()
    {
        _closeConfirmed = true;
        Close();
    }

    /// <summary>Terminals in this window running a command (for quitting).</summary>
    internal IEnumerable<ShellCommand> RunningCommands() =>
        AllTabs().SelectMany(t => t.Workspace.Tabs).Select(p => p.Control.RunningCommand).OfType<ShellCommand>();

    /// <summary>Asks this window's question for the whole app (quitting).</summary>
    internal Task<bool> ConfirmQuit(IEnumerable<MainWindow> windows) =>
        ConfirmClose(windows.SelectMany(w => w.AllTabs()).SelectMany(t => t.Workspace.Tabs));

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    private const uint FlashTray = 0x2, FlashTimerNoForeground = 0xC;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint hwnd);
}
