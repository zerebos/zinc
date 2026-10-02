using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Phosphor;

// The quick terminal: a window summoned and dismissed with a global hotkey
// (quick-terminal-* settings), and the notice shown when that hotkey is taken.
public sealed partial class MainWindow
{
    private readonly bool _quick;
    private bool _quickShown;
    private bool _active;
    private bool _hotkeyNoticeDismissed;

    public bool IsQuickShownAndActive => _quickShown && _active;

    public bool IsQuickShown => _quickShown;

    private void ConfigureQuickWindow()
    {
        // Out of the taskbar and Alt+Tab, above other windows while shown.
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMinimizable = false;
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _active = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_quick)
        {
            if (!_active && _quickShown && _config.Global.QuickTerminal.Autohide && PaletteLayer.Visibility != Visibility.Visible)
            {
                HideQuick();
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

    /// <summary>Places the window on the configured monitor and brings it to the front.</summary>
    public void ShowQuick()
    {
        var settings = _config.Global.QuickTerminal;
        var area = settings.Screen == QuickTerminalScreen.Main || !GetCursorPos(out var cursor)
            ? DisplayArea.Primary
            : DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var work = area.WorkArea;
        var r = QuickTerminalLayout.Place(new QuickTerminalLayout.Rect(work.X, work.Y, work.Width, work.Height), settings);
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));

        _quickShown = true;
        AppWindow.Show(true);
        Activate();
        // The hotkey press grants us the right to take the foreground.
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (SelectedTab is { } tab)
        {
            FocusActive(tab);
        }
    }

    public void HideQuick()
    {
        _quickShown = false;
        AppWindow.Hide();
    }

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
}
