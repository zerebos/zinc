using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Zinc;

// The quick terminal: a window summoned and dismissed with a global hotkey (quick-terminal-* settings), and the
// notice shown when that hotkey is taken. Like Ghostty's: just the terminal (no title bar, caption buttons or
// workspace strip; a pane's own strip shows once it has a second tab), centered by default, sliding in and out.
// Closing it hides it (its shells and scrollback live on until Zinc quits or its last tab closes), and dismissing
// it gives the foreground back to the window you came from.
public sealed partial class MainWindow
{
    private readonly bool _quick;
    private bool _quickShown;
    private bool _active;
    private bool _hotkeyNoticeDismissed;

    // The window that had the foreground when the quick terminal was summoned: it gets it back on dismissal.
    private nint _previousForeground;

    // A slide in progress: in (true) or out, since when, and what runs when it's done.
    private bool? _sliding;
    private long _slideStart;
    private Action? _slideDone;
    private bool _slideHooked;

    public bool IsQuickShownAndActive => _quickShown && _active;
    public bool IsQuickShown => _quickShown;

    private void ConfigureQuickWindow()
    {
        // Out of the taskbar and Alt+Tab, above other windows while shown; no title bar or caption buttons (the thin
        // border stays: Windows rounds its corners).
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

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

    /// <summary>Places the window on the configured monitor, brings it to the front and slides it in.</summary>
    public void ShowQuick()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var settings = _config.Global.QuickTerminal;
        // Summoned again while sliding out: it turns around from where it is (still placed, still showing).
        double? from = null;
        if (_sliding == false)
        {
            from = SlideProgress();
            FinishSlide(run: false);
        }
        else if (!_quickShown)
        {
            var foreground = GetForegroundWindow();
            _previousForeground = foreground != hwnd ? foreground : 0;
            var area = settings.Screen == QuickTerminalScreen.Main || !GetCursorPos(out var cursor)
                ? DisplayArea.Primary
                : DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
            var work = area.WorkArea;
            var r = QuickTerminalLayout.Place(new QuickTerminalLayout.Rect(work.X, work.Y, work.Width, work.Height), settings);
            AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
            if (settings.AnimationDuration > 0)
            {
                ApplySlide(0); // nothing shown yet: no flash of the whole window before the first frame
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

        StartSlide(show: true, done: null, from);
    }

    /// <summary>
    /// Slides it out and hides it. <paramref name="restoreFocus"/>: dismissed here (the hotkey, closing), so the window
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
        StartSlide(show: false, done: () =>
        {
            AppWindow.Hide();
            ApplySlide(1); // whole again for next time (hidden: nothing shows)
            if (previous != 0 && IsWindow(previous) && IsWindowVisible(previous))
            {
                SetForegroundWindow(previous);
            }
        });
    }

    // ------------------------------------------------------------ the slide

    /// <summary>Slides in or out, from <paramref name="from"/> (0 hidden .. 1 whole), else from the far end or where a slide is now.</summary>
    private void StartSlide(bool show, Action? done, double? from = null)
    {
        var duration = _config.Global.QuickTerminal.AnimationDuration;
        if (duration <= 0 || _windowClosed)
        {
            FinishSlide(run: false);
            ApplySlide(1);
            done?.Invoke();
            return;
        }

        // Turning around mid-slide starts from where it is, not from the far end.
        var progress = from ?? (_sliding != null ? SlideProgress() : show ? 0 : 1);
        _sliding = show;
        _slideDone = done;
        var elapsed = (show ? progress : 1 - progress) * duration;
        _slideStart = Stopwatch.GetTimestamp() - (long)(elapsed * Stopwatch.Frequency);
        if (!_slideHooked)
        {
            _slideHooked = true;
            CompositionTarget.Rendering += OnSlideFrame;
        }
    }

    /// <summary>0 hidden .. 1 all there, at this moment of the slide.</summary>
    private double SlideProgress()
    {
        var duration = _config.Global.QuickTerminal.AnimationDuration;
        var t = duration <= 0 ? 1 : Math.Clamp(Stopwatch.GetElapsedTime(_slideStart).TotalSeconds / duration, 0, 1);
        return _sliding == false ? 1 - t : t;
    }

    private void OnSlideFrame(object? sender, object e) => Ui.Guard("quick terminal slide", () =>
    {
        if (_sliding is not { } show || _windowClosed)
        {
            FinishSlide(run: false);
            return;
        }

        var progress = SlideProgress();
        ApplySlide(progress);
        if (show ? progress >= 1 : progress <= 0)
        {
            FinishSlide(run: true);
        }
    });

    private void FinishSlide(bool run)
    {
        var done = _slideDone;
        var wasIn = _sliding == true;
        _sliding = null;
        _slideDone = null;
        if (_slideHooked)
        {
            _slideHooked = false;
            CompositionTarget.Rendering -= OnSlideFrame;
        }

        if (wasIn)
        {
            ApplySlide(1);
        }

        if (run)
        {
            done?.Invoke();
        }
    }

    /// <summary>
    /// One frame: the part of the window nearest its edge (a window region, in pixels of the window) with the
    /// content moved along (<see cref="QuickTerminalSlide"/>). At 1: no region (Windows' rounded corners back).
    /// </summary>
    private void ApplySlide(double progress)
    {
        if (_windowClosed)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var translate = Root.RenderTransform as TranslateTransform ?? new TranslateTransform();
        Root.RenderTransform = translate;
        if (progress >= 1)
        {
            SetWindowRgn(hwnd, 0, true);
            translate.X = translate.Y = 0;
            return;
        }

        var size = AppWindow.Size;
        var (visible, offsetX, offsetY) = QuickTerminalSlide.At(_config.Global.QuickTerminal.Position, size.Width, size.Height, progress);
        // A region of nothing would mean "no region" (all of it): a 1-pixel strip off the window's edge instead.
        var region = visible.Width * visible.Height > 0
            ? CreateRectRgn(visible.X, visible.Y, visible.X + visible.Width, visible.Y + visible.Height)
            : CreateRectRgn(-1, -1, 0, 0);
        SetWindowRgn(hwnd, region, true); // Windows owns the region from here
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        translate.X = offsetX / scale;
        translate.Y = offsetY / scale;
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);
}
