using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Phosphor.Terminal;

namespace Phosphor;

// Shell integration, the attention part: a long command that finishes where you weren't looking
// (another workspace or tab, or another app) puts a dot on its tab, red if it failed, and flashes
// the taskbar button while the window is in the background; the dot clears once you look.
// Closing something while the shell says a command is running asks first.
public sealed partial class MainWindow
{
    private bool _confirmingClose;
    private bool _closeConfirmed;

    private void OnCommandFinished(WorkspaceTab tab, TerminalTab pane, ShellCommand command)
    {
        var global = _config.Global;
        if (global.NotifyCommandFinished == NotifyCommandFinished.Never ||
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
        if (!_active)
        {
            FlashTaskbarButton();
        }
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

    /// <summary>A workspace's dot: the most urgent of its terminals'.</summary>
    private static void ShowWorkspaceAttention(WorkspaceTab tab) =>
        tab.Header.SetAttention(tab.Workspace.Tabs.Select(t => t.Attention).DefaultIfEmpty().Max());

    private void FlashTaskbarButton()
    {
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
                RequestedTheme = ElementTheme.Dark,
                Title = running.Count == 1 ? "A command is still running" : $"{running.Count} commands are still running",
                Content = $"{list}\n\nClosing stops {(running.Count == 1 ? "it" : "them")}.",
                PrimaryButtonText = "Close",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
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
            ClosePane(tab, pane);
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
            foreach (var pane in panes)
            {
                ClosePane(tab, pane);
            }
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
                Close();
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
}
