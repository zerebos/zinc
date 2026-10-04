using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Zinc.Input;

namespace Zinc;

// The quick terminal: a window summoned and dismissed with a global hotkey (quick-terminal-* settings), and the
// notice shown when that hotkey is taken. Like Ghostty's: one terminal (splits, no tabs or workspaces; no title bar,
// caption buttons or strips), centered by default, fading and growing into place (or out of its edge). Closing it
// hides it (its shells and scrollback live on until Zinc quits or its last tab closes), and dismissing it gives the
// foreground back to the window you came from.
public sealed partial class MainWindow
{
    private readonly bool _quick;
    private bool _quickShown;
    private bool _active;
    private bool _hotkeyNoticeDismissed;

    // The window that had the foreground when the quick terminal was summoned: it gets it back on dismissal.
    private nint _previousForeground;

    // Fading out: hidden when the timer fires (kept in a field: a timer only a local holds would be collected).
    private DispatcherQueueTimer? _fadeOutTimer;
    private Action? _fadeOutDone;

    // Fading in: Windows' shadow (which comes with the rounded corners) is off until the content is whole, then on.
    private DispatcherQueueTimer? _shadowTimer;

    public bool IsQuickShownAndActive => _quickShown && _active;
    public bool IsQuickShown => _quickShown;

    private void ConfigureQuickWindow()
    {
        // Out of the taskbar and Alt+Tab, above other windows while shown, with no frame at all: the content fades and
        // grows in a see-through window (ApplyBackdrop gives it a clear one), and a frame would sit there at full size
        // meanwhile. Windows still rounds its corners when asked.
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            // Not resizable by its frame either: a resizing frame without a title bar draws a light strip across the
            // top (Windows' quirk), and invisible margins around the content.
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        }

        // No border of any kind (a classic one survived the presenter's settings, and Windows 11 outlines windows):
        // just the rounded terminal.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        const int WS_BORDER = 0x00800000, WS_DLGFRAME = 0x00400000, WS_THICKFRAME = 0x00040000;
        SetWindowLongW(hwnd, -16 /* GWL_STYLE */, GetWindowLongW(hwnd, -16) & ~(WS_BORDER | WS_DLGFRAME | WS_THICKFRAME));
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0027 /* SWP_FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER */ | 0x0010 /* NOACTIVATE */);
        var corners = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corners, sizeof(int));
        var noOutline = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref noOutline, sizeof(int));

        // Just the terminal: the workspace strip (which is also the title bar elsewhere) stays collapsed.
        Tabs.IsAddTabButtonVisible = false;
        Tabs.Loaded += (_, _) =>
        {
            if (VisualDescendants(Tabs).OfType<Microsoft.UI.Xaml.Controls.Grid>().FirstOrDefault(g => g.Name == "TabContainerGrid") is { } strip)
            {
                strip.Visibility = Visibility.Collapsed;
            }
        };

        // Closing (Alt+F4, the system menu) hides it: what runs in it lives on. Only quitting Zinc, or its last tab
        // closing (nothing left to keep), closes it for real.
        AppWindow.Closing += (_, args) =>
        {
            if (!App.Instance.Quitting && AllTabs().Any())
            {
                args.Cancel = true;
                HideQuick(restoreFocus: true);
            }
        };
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _active = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_quick)
        {
            // Focus went elsewhere: it hides (focus stays where it went).
            if (!_active && _quickShown && _config.Global.QuickTerminal.Autohide && PaletteLayer.Visibility != Visibility.Visible)
            {
                HideQuick(restoreFocus: false);
            }

            return;
        }

        if (_active)
        {
            App.Instance.NoteActivated(this);
            if (SelectedTab is { } shown)
            {
                UpdateFocusBorders(shown); // back in front: what's focused has been seen
            }

            // What finished while you were away pulses again now that you can see it.
            foreach (var t in AllTabs())
            {
                t.Header.PulseAttention();
            }

            ShowHotkeyNoticeIfNeeded();
        }
    }

    /// <summary>Places the window on the configured monitor, brings it to the front and fades it in.</summary>
    public void ShowQuick()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var settings = _config.Global.QuickTerminal;
        // Summoned again while fading out: it turns around from where it is (still placed, still showing).
        var turningAround = _fadeOutTimer?.IsRunning == true;
        _fadeOutTimer?.Stop();
        if (!_quickShown && !turningAround)
        {
            var foreground = GetForegroundWindow();
            _previousForeground = foreground != hwnd ? foreground : 0;
            var area = settings.Screen == QuickTerminalScreen.Main || !GetCursorPos(out var cursor)
                ? DisplayArea.Primary
                : DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
            var work = area.WorkArea;
            var r = QuickTerminalLayout.Place(new QuickTerminalLayout.Rect(work.X, work.Y, work.Width, work.Height), settings,
                App.Instance.QuickTerminalSizes.For(MonitorKey(area), settings)); // the size its edges were dragged to there
            UpdateResizeEdges();
            var rect = new RectInt32(r.X, r.Y, r.Width, r.Height);
            AppWindow.MoveAndResize(rect);
            // Onto a monitor with another scale, Windows resizes it by the change of scale once it's there (too big on
            // a 125% screen, too small back on 100%): the size it should have, again.
            if (AppWindow.Size.Width != rect.Width || AppWindow.Size.Height != rect.Height || AppWindow.Position.X != rect.X)
            {
                AppWindow.MoveAndResize(rect);
            }

            if (settings.AnimationDuration > 0)
            {
                var scale = Scale();
                SetAppearance(QuickTerminalAppear.Start(settings.Position, r.Width / scale, r.Height / scale), opacity: 0, animate: false);
                SetShadow(false); // Windows draws it at full size from the first frame: on once the content is
            }
        }

        _quickShown = true;
        AppWindow.Show(true);
        Activate();
        // The hotkey press grants us the right to take the foreground.
        SetForegroundWindow(hwnd);
        if (SelectedTab is { } tab)
        {
            FocusActive(tab);
        }

        // Into place: the start values were set without a transition; the targets go with one, once the start has been
        // drawn, so the transition runs from it.
        if (settings.AnimationDuration > 0)
        {
            DispatcherQueue.SafeEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_quickShown)
                {
                    SetAppearance((1, 0, 0), opacity: 1, animate: true);
                }
            });
            if (_shadowTimer == null)
            {
                _shadowTimer = DispatcherQueue.CreateTimer();
                _shadowTimer.IsRepeating = false;
                _shadowTimer.OnTick(_ =>
                {
                    if (_quickShown)
                    {
                        SetShadow(true);
                    }
                });
            }

            _shadowTimer.Interval = TimeSpan.FromSeconds(settings.AnimationDuration);
            _shadowTimer.Start();
        }
        else
        {
            SetAppearance((1, 0, 0), opacity: 1, animate: false);
            SetShadow(true);
        }
    }

    /// <summary>
    /// Fades it out and hides it. <paramref name="restoreFocus"/>: dismissed here (the hotkey, closing), so the window
    /// you came from gets the foreground back; not when it hides because focus already went elsewhere.
    /// </summary>
    public void HideQuick(bool restoreFocus = true)
    {
        if (!_quickShown)
        {
            return;
        }

        _quickShown = false;
        var previous = restoreFocus ? _previousForeground : 0;
        void Hidden()
        {
            AppWindow.Hide();
            SetAppearance((1, 0, 0), opacity: 1, animate: false); // whole again for next time (hidden: nothing shows)
            if (previous != 0 && IsWindow(previous) && IsWindowVisible(previous))
            {
                SetForegroundWindow(previous);
            }
        }

        var duration = _config.Global.QuickTerminal.AnimationDuration;
        if (duration <= 0 || _windowClosed)
        {
            Hidden();
            return;
        }

        // Out: the shadow goes first (it would stay full size), then the content fades a little smaller (or back toward
        // its edge); hidden once the fade is done.
        _shadowTimer?.Stop();
        SetShadow(false);
        var start = QuickTerminalAppear.Start(_config.Global.QuickTerminal.Position, Root.ActualWidth, Root.ActualHeight);
        SetAppearance((1 - (1 - start.Scale) / 3, start.OffsetX / 3, start.OffsetY / 3), opacity: 0, animate: true);
        if (_fadeOutTimer == null)
        {
            _fadeOutTimer = DispatcherQueue.CreateTimer();
            _fadeOutTimer.IsRepeating = false;
            _fadeOutTimer.OnTick(_ => _fadeOutDone?.Invoke());
        }

        _fadeOutDone = Hidden;
        _fadeOutTimer.Interval = TimeSpan.FromSeconds(duration);
        _fadeOutTimer.Start();
    }

    private double Scale() => Root.XamlRoot?.RasterizationScale ?? 1;

    /// <summary>
    /// Windows' shadow around the window, off while the content isn't whole. A frameless window's shadow comes with its
    /// rounded corners (turning off non-client rendering left it there): square while fading, rounded once whole.
    /// </summary>
    private void SetShadow(bool on)
    {
        if (_windowClosed)
        {
            return;
        }

        var corners = on ? 2 : 1; // DWMWCP_ROUND : DWMWCP_DONOTROUND
        DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corners, sizeof(int));
    }

    /// <summary>
    /// The content's look: scale (around its middle), offset (DIPs) and opacity, at once or with a transition of the
    /// configured length (WinUI's implicit transitions, run by the compositor: smooth even when the UI thread is busy).
    /// </summary>
    private void SetAppearance((double Scale, double OffsetX, double OffsetY) look, double opacity, bool animate)
    {
        if (_windowClosed)
        {
            return;
        }

        var duration = TimeSpan.FromSeconds(Math.Max(0.01, _config.Global.QuickTerminal.AnimationDuration));
        Root.OpacityTransition = animate ? new ScalarTransition { Duration = duration } : null;
        Root.ScaleTransition = animate ? new Vector3Transition { Duration = duration } : null;
        Root.TranslationTransition = animate ? new Vector3Transition { Duration = duration } : null;
        var size = AppWindow.Size;
        var scale = Scale();
        Root.CenterPoint = new Vector3((float)(size.Width / scale / 2), (float)(size.Height / scale / 2), 0);
        Root.Opacity = opacity;
        Root.Scale = new Vector3((float)look.Scale, (float)look.Scale, 1);
        Root.Translation = new Vector3((float)look.OffsetX, (float)look.OffsetY, 0);
    }

    /// <summary>
    /// What the quick terminal doesn't do: it's one terminal (splits are fine), as Ghostty's: no tabs (Windows Terminal's
    /// quake window keeps them, but with no strip to see them by), no workspaces, no sidebar or title bar to toggle.
    /// </summary>
    internal static bool NotInQuickTerminal(TerminalAction action) => action is
        TerminalAction.NewTab or TerminalAction.NewTabFromProfile or TerminalAction.DuplicateTab or TerminalAction.OpenNewTabMenu or
        TerminalAction.NextTab or TerminalAction.PreviousTab or TerminalAction.SwitchToTab or TerminalAction.TabSwitcher or
        TerminalAction.NewWorkspace or TerminalAction.ReopenWorkspace or
        TerminalAction.ToggleSidebar or TerminalAction.ToggleTitleBar or TerminalAction.FocusMode;

    /// <summary>The palette's commands that are about tabs or workspaces ("Rename workspace…", "New tab: Fedora").</summary>
    private static bool AboutTabsOrWorkspaces(string title) =>
        System.Text.RegularExpressions.Regex.IsMatch(title, @"\b(tabs?|workspaces?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private void ShowHotkeyNoticeIfNeeded()
    {
        if (_hotkeyNoticeDismissed)
        {
            return;
        }

        if (App.Instance.UnavailableQuickTerminalHotkey() is not { } key)
        {
            HotkeyNotice.IsOpen = false;
            return;
        }

        HotkeyNotice.Message =
            $"{key} is in use by another app (Windows Terminal's quake mode uses Win+`). " +
            "Pick another in Settings > Quick terminal, or free the key: Zinc tries again whenever one of its windows is activated.";
        HotkeyNotice.IsOpen = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(nint hwnd, int index, int value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
