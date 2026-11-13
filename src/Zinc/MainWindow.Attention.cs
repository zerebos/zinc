using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Zinc.Terminal;

namespace Zinc;

// The attention part of shell integration. A long command that finishes where you weren't looking
// (another workspace, tab or pane, or another app) puts a dot on its tab, red if it failed, and
// flashes the taskbar button while the window is in the background (or, with "toast", shows a
// Windows notification). The dot clears once you look. Programs' own notifications (OSC 9 / 777)
// get a blue dot the same way, and their progress (OSC 9;4) gets a ring and the taskbar's progress bar.
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
        if (attention > pane.Attention || attention == Attention.Failed)
        {
            pane.Attention = attention;
            pane.FailedExit = command.Failed ? command.ExitCode : null;
        }

        Log.Write($"attention: '{command.Text}' {attention} after {took.TotalSeconds:0}s");
        UpdateFocusBorders(tab);
        // The command, what happened, then where (docs/ux-writing.md): "npm test failed (exit 1)", "In "api", after 3 min".
        var toast = Alert(global.NotifyCommandFinished, tab, pane,
            $"{command.Text ?? "A command"} {AttentionWords.For(attention, command.ExitCode)}", $"In \"{tab.Header.Title}\", after {Elapsed(took)}");
        if (!toast)
        {
            PlayFinishedSound(global.CommandFinishedSound);
        }
    }

    /// <summary>The sound for a finished command (<c>command-finished-sound</c>), played without waiting.</summary>
    private static void PlayFinishedSound(FinishedSound sound)
    {
        try
        {
            switch (sound)
            {
                case FinishedSound.Notification:
                    PlaySound("Notification.Default", 0, SndAlias | SndAsync | SndNoDefault);
                    break;
                case FinishedSound.Beep:
                    MessageBeep(0); // MB_OK: the "Default Beep" sound
                    break;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Write($"attention: no sound: {ex.Message}");
        }
    }

    private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndAlias = 0x00010000;

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string sound, nint module, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint type);

    /// <summary>
    /// The bell rang (per <c>bell</c>), so this puts a dot on the tab where you weren't looking, and flashes the taskbar
    /// when the window is in the background.
    /// </summary>
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

    /// <summary>
    /// A tab's own program ended and its pane stays. After an error, this puts a red mark where you weren't looking
    /// (it outranks every other mark) and flashes the taskbar from the background, as the bell does. The pane's status
    /// slot shows the exit either way.
    /// </summary>
    private void OnExited(WorkspaceTab tab, TerminalTab pane, uint code)
    {
        if (code != 0 && !IsLookingAt(tab, pane))
        {
            pane.Attention = Attention.Exited;
            pane.FailedExit = unchecked((int)code);
            Log.Write($"attention: '{pane.Title}' exited ({ExitCodes.Format(code)})");
            if (!_active)
            {
                FlashTaskbarButton();
            }
        }

        UpdateFocusBorders(tab);
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
    /// What happens besides the dot. That's a Windows notification ("toast" shows one while the window is in the
    /// background, and "toast-always" whenever you weren't looking), or else a taskbar flash while in the background.
    /// Clicking the notification brings you to the pane.
    /// </summary>
    /// <returns>Whether it was a Windows notification.</returns>
    private bool Alert(NotifyMode mode, WorkspaceTab tab, TerminalTab pane, string title, string body)
    {
        if (mode == NotifyMode.ToastAlways || (mode == NotifyMode.Toast && !_active))
        {
            App.Instance.Notify(title, body, () => BringToPane(tab, pane));
            return true;
        }

        if (!_active)
        {
            FlashTaskbarButton();
        }

        return false;
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
    /// hand Zinc the right to take the foreground (Activate alone leaves the window behind), so
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
    /// focus. A visible pane beside the focused one doesn't count, because your attention is elsewhere.
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

    /// <summary>
    /// Shows a workspace's dot, which is the most urgent of its terminals' dots, and a ring for a program's progress
    /// (an error first).
    /// </summary>
    private static void ShowWorkspaceAttention(WorkspaceTab tab)
    {
        tab.Header.SetAttention(tab.Workspace.Tabs.Select(t => t.Attention).DefaultIfEmpty().Max(),
            tab.Workspace.Tabs.Count(t => t.Attention != Attention.None),
            AttentionWords.Summary([.. tab.Workspace.Tabs.Select(t => (t.Attention, t.FailedExit))]));
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
    /// Asks before closing terminals the shell says are running a command, or (<paramref name="severalTabs"/>) two or
    /// more tabs at once even with nothing running, because a close-pane key takes every tab of a pane and shows no
    /// count. True to go ahead (nothing to ask, or the user agreed).
    /// </summary>
    /// <param name="what">What closes, for the button: "tab", "pane", "workspace", "window".</param>
    /// <param name="severalTabs">False for zn: a script gets no question unless a command is running.</param>
    private Task<bool> ConfirmClose(IEnumerable<TerminalTab> panes, string what, bool severalTabs = true) =>
        _config.Global.ConfirmClose ? ConfirmClose(panes, "Closing", $"Close {what}", severalTabs) : Task.FromResult(true);

    /// <param name="doing">What the answer does, as the reason ("Closing", "Quitting").</param>
    /// <param name="button">The primary button's label, a verb and its object ("Close tab", "Quit Zinc").</param>
    private async Task<bool> ConfirmClose(IEnumerable<TerminalTab> panes, string doing, string button, bool severalTabs = false)
    {
        var all = panes.ToList();
        var running = all.Select(p => p.Control.RunningCommand).OfType<ShellCommand>().ToList();
        if (running.Count == 0 && !(severalTabs && all.Count > 1))
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
                PrimaryButtonText = button,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (running.Count > 0)
            {
                dialog.Title = running.Count == 1 ? "A command is still running" : $"{running.Count} commands are still running";
                dialog.Content = $"{list}\n\n{doing} stops {(running.Count == 1 ? "it" : "them")}.";
            }
            else
            {
                dialog.Title = $"Close {all.Count} tabs?";
            }

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            // Code after an await resumes as queued work, which bypasses Application.UnhandledException,
            // so this records the exception and closes nothing.
            Log.Crash("asking before a close", ex);
            return false;
        }
        finally
        {
            _confirmingClose = false;
        }
    }

    /// <summary>Closes a terminal tab when you ask to (not when its shell has exited).</summary>
    private void AskClosePaneTab(WorkspaceTab tab, TerminalTab pane) => AskClosePaneTabs(tab, [pane]);

    /// <summary>
    /// Closes several terminal tabs, asking once for all of them. <paramref name="what"/> names them for the button
    /// ("pane").
    /// </summary>
    private async void AskClosePaneTabs(WorkspaceTab tab, IReadOnlyList<TerminalTab> panes, string? what = null)
    {
        // FIXME: this is gonna be fun with i18n later...
        if (await ConfirmClose(panes, what ?? (panes.Count == 1 ? "tab" : "tabs")))
        {
            Ui.Guard(panes.Count == 1 ? "closing a tab" : "closing tabs", () => CloseAsked(tab, panes));
        }
        else
        {
            FocusActive(tab);
        }
    }

    /// <summary>
    /// Closes tabs you asked to close. When they're all of the workspace's tabs, it closes as "Close workspace" does, so
    /// Reopen closed workspace brings it back.
    /// </summary>
    private void CloseAsked(WorkspaceTab tab, IReadOnlyList<TerminalTab> panes)
    {
        if (!_quick && tab.Workspace.Tabs.All(panes.Contains)) // (the quick terminal's isn't a workspace you'd reopen)
        {
            CloseWorkspaceNow(tab);
            return;
        }

        foreach (var pane in panes)
        {
            ClosePaneTab(tab, pane);
        }
    }

    /// <summary>Makes the window's close button (and Alt+F4) ask first if something is running.</summary>
    private void ConfirmWindowClose()
    {
        AppWindow.Closing += async (_, args) =>
        {
            if (_closeConfirmed || _quick)
            {
                return;
            }

            var panes = AllTabs().SelectMany(t => t.Workspace.Tabs).ToList();
            if (!_config.Global.ConfirmClose || (panes.Count < 2 && panes.All(p => p.Control.RunningCommand == null)))
            {
                return;
            }

            args.Cancel = true;
            if (await ConfirmClose(panes, "window"))
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

    /// <summary>Asks this window's question for the whole app (quitting, or restarting for an update).</summary>
    internal Task<bool> ConfirmQuit(IEnumerable<MainWindow> windows, bool restart = false) =>
        !_config.Global.ConfirmQuit ? Task.FromResult(true)
            : ConfirmClose(windows.SelectMany(w => w.AllTabs()).SelectMany(t => t.Workspace.Tabs),
                restart ? "Restarting" : "Quitting", restart ? "Restart Zinc" : "Quit Zinc");

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
