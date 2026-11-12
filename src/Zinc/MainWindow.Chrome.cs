using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Zinc.Input;

namespace Zinc;

// With workspace-tabs = left, the title bar and the workspace list can get out of the way:
// title-bar = show | auto-hide and sidebar = full | rail | auto-hide (config), switched per window
// by toggle-title-bar / toggle-sidebar / focus-mode. Auto-hidden, each is an overlay (the
// terminals don't reflow) that slides in when the pointer reaches its window edge, or a dragged
// tab does, and slides out once the pointer has been away for a moment. The pointer is polled
// with GetCursorPos, because the caption buttons and flyouts aren't in XAML's tree, so
// PointerExited would fire on the way to them.
public sealed partial class MainWindow
{
    /// <summary>
    /// How far in from the window's edge the pointer reveals a hidden bar (DIPs). The top 4 px
    /// stay the resize border (non-client), so the top zone's usable part is what's below.
    /// </summary>
    private const double HotZoneSize = 8;

    private SidebarMode _sidebarMode;
    private TitleBarMode _titleBarMode;

    /// <summary>The modes focus mode replaced, while it's on.</summary>
    private (SidebarMode, TitleBarMode)? _beforeFocusMode;

    /// <summary>
    /// The config's modes when last applied. A reload only overrides the window's toggles when these change.
    /// </summary>
    private (SidebarMode, TitleBarMode) _chromeConfig;

    private readonly Border _sidebarHotZone = HotZone();
    private readonly Border _titleHotZone = HotZone();
    private bool _sidebarRevealed, _titleRevealed;
    private bool _chromePinned;
    private DispatcherQueueTimer? _chromeTimer;
    private long _sidebarAwaySince, _titleAwaySince;
    private Action? _refreshClickThrough;

    private static Border HotZone() => new()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        AllowDrop = true,
        Visibility = Visibility.Collapsed,
    };

    private Brush OverlayBackground => (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

    private void SetUpChrome()
    {
        _sidebarHotZone.Width = HotZoneSize;
        _sidebarHotZone.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRowSpan(_sidebarHotZone, 2);
        _titleHotZone.Height = HotZoneSize;
        _titleHotZone.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRowSpan(_titleHotZone, 2);
        Grid.SetColumnSpan(_titleHotZone, 2);
        foreach (var zone in new[] { _sidebarHotZone, _titleHotZone })
        {
            Canvas.SetZIndex(zone, 3);
            Root.Children.Add(zone);
        }

        _sidebarHotZone.PointerEntered += (_, _) => RevealSidebar(true);
        _titleHotZone.PointerEntered += (_, _) => RevealTitleBar(true);
        // A dragged workspace or tab reaching the left edge opens the list to be dropped on.
        _sidebarHotZone.DragEnter += (_, _) => RevealSidebar(true);

        _chromeTimer = DispatcherQueue.CreateTimer();
        _chromeTimer.Interval = TimeSpan.FromMilliseconds(150);
        _chromeTimer.OnTick(_ => HideChromeWhenAway());

        _chromeConfig = (_config.Global.Sidebar, _config.Global.TitleBar);
        SetSidebarMode(_config.Global.Sidebar);
        SetTitleBarMode(_config.Global.TitleBar);
    }

    /// <summary>After a config reload, the config's modes win only if they changed.</summary>
    private void ApplyChromeConfig(Config global)
    {
        if (_workspaceList == null || (global.Sidebar, global.TitleBar) == _chromeConfig)
        {
            return;
        }

        _chromeConfig = (global.Sidebar, global.TitleBar);
        _beforeFocusMode = null;
        SetSidebarMode(global.Sidebar);
        SetTitleBarMode(global.TitleBar);
    }

    /// <summary>This window's modes for the session (null without the workspace list).</summary>
    private Session.WindowChrome? CaptureChrome() => _workspaceList == null
        ? null
        : new(_sidebarMode, _titleBarMode, _chromeConfig.Item1, _chromeConfig.Item2, _beforeFocusMode?.Item1, _beforeFocusMode?.Item2);

    /// <summary>Reopens with a saved window's modes, unless the config's have changed since.</summary>
    private void RestoreChrome(Session.WindowChrome? saved)
    {
        if (_workspaceList == null || saved == null)
        {
            return;
        }

        if (!saved.Applies(_chromeConfig.Item1, _chromeConfig.Item2))
        {
            Log.Write($"session: config's sidebar/title bar changed since the save; using the config's");
            return;
        }

        SetSidebarMode(saved.Sidebar);
        SetTitleBarMode(saved.TitleBar);
        _beforeFocusMode = saved is { FocusSidebar: { } sidebar, FocusTitleBar: { } titleBar } ? (sidebar, titleBar) : null;
    }

    /// <summary>Runs the chrome actions, or returns false without the workspace list (nothing to switch).</summary>
    private bool HandleChromeAction(ActionInvocation invocation)
    {
        if (_workspaceList == null)
        {
            return false;
        }

        switch (invocation.Action)
        {
            case TerminalAction.ToggleSidebar:
                _beforeFocusMode = null;
                SetSidebarMode(invocation.Index < 0
                    ? (SidebarMode)(((int)_sidebarMode + 1) % 3)
                    : _sidebarMode == (SidebarMode)invocation.Index ? SidebarMode.Full : (SidebarMode)invocation.Index);
                return true;
            case TerminalAction.ToggleTitleBar:
                _beforeFocusMode = null;
                SetTitleBarMode(_titleBarMode == TitleBarMode.Show ? TitleBarMode.AutoHide : TitleBarMode.Show);
                return true;
            case TerminalAction.FocusMode when _beforeFocusMode is { } before:
                _beforeFocusMode = null;
                SetSidebarMode(before.Item1);
                SetTitleBarMode(before.Item2);
                return true;
            case TerminalAction.FocusMode:
                _beforeFocusMode = (_sidebarMode, _titleBarMode);
                SetSidebarMode(SidebarMode.AutoHide);
                SetTitleBarMode(TitleBarMode.AutoHide);
                return true;
            default:
                return false;
        }
    }

    private void SetSidebarMode(SidebarMode mode)
    {
        if (_workspaceList is not { } list)
        {
            return;
        }

        Log.Write($"sidebar: {mode}");
        _sidebarMode = mode;
        list.Compact = mode == SidebarMode.Rail;
        SetListFooterCompact(mode == SidebarMode.Rail);
        UpdateMark(mode);

        // Hidden, the list floats over the workspace (which takes both columns) instead of beside it.
        var overlay = mode == SidebarMode.AutoHide;
        Grid.SetColumn(Tabs, overlay ? 0 : 1);
        Grid.SetColumnSpan(Tabs, overlay ? 2 : 1);
        Grid.SetColumnSpan(list, overlay ? 2 : 1);
        list.HorizontalAlignment = HorizontalAlignment.Left;
        list.Background = overlay ? OverlayBackground : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        list.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        list.BorderThickness = new Thickness(0, 0, overlay ? 1 : 0, 0);
        Canvas.SetZIndex(list, overlay ? 1 : 0);
        _sidebarHotZone.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;
        _sidebarRevealed = !overlay;
        list.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
        Translate(list).X = 0;
        _refreshClickThrough?.Invoke();
    }

    private void SetTitleBarMode(TitleBarMode mode)
    {
        if (_workspaceList == null)
        {
            return;
        }

        Log.Write($"title bar: {mode}");
        _titleBarMode = mode;

        // Hidden, the row takes no room and floats over the top when revealed.
        var overlay = mode == TitleBarMode.AutoHide;
        Root.RowDefinitions[0].Height = new GridLength(overlay ? 0 : TitleBarHeight);
        Grid.SetRowSpan(DragRegion, overlay ? 2 : 1);
        DragRegion.Height = overlay ? TitleBarHeight : double.NaN;
        DragRegion.VerticalAlignment = overlay ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        DragRegion.Background = overlay ? OverlayBackground : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Canvas.SetZIndex(DragRegion, overlay ? 2 : 0);
        _titleHotZone.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;
        _titleRevealed = !overlay;
        DragRegion.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
        Translate(DragRegion).Y = 0;
        AppWindow.TitleBar.PreferredHeightOption = overlay ? TitleBarHeightOption.Collapsed : TitleBarHeightOption.Standard;
        if (overlay)
        {
            ReleaseTitleBarArea();
        }
        else
        {
            SetTitleBar(DragRegion);
        }

        _refreshClickThrough?.Invoke();
    }

    /// <param name="pinned">Stay revealed wherever the pointer is (test captures).</param>
    internal void RevealSidebar(bool show, bool pinned = false)
    {
        if (_windowClosed)
        {
            return; // its AppWindow is gone
        }

        if (_workspaceList is not { } list || _sidebarMode != SidebarMode.AutoHide || _sidebarRevealed == show)
        {
            return;
        }

        _sidebarRevealed = show;
        _sidebarAwaySince = 0;
        _chromePinned |= pinned;
        _sidebarHotZone.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        Slide(list, "X", show, -WorkspaceList.DefaultWidth);
        UpdateChromeTimer();
    }

    /// <param name="pinned">Stay revealed wherever the pointer is (test captures).</param>
    internal void RevealTitleBar(bool show, bool pinned = false)
    {
        if (_windowClosed)
        {
            return; // its AppWindow is gone
        }

        if (_workspaceList == null || _titleBarMode != TitleBarMode.AutoHide || _titleRevealed == show)
        {
            return;
        }

        _titleRevealed = show;
        _titleAwaySince = 0;
        _chromePinned |= pinned;
        _titleHotZone.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        // The window buttons come and go with the row.
        AppWindow.TitleBar.PreferredHeightOption = show ? TitleBarHeightOption.Standard : TitleBarHeightOption.Collapsed;
        Slide(DragRegion, "Y", show, -TitleBarHeight);
        UpdateChromeTimer();
    }

    private void UpdateChromeTimer()
    {
        var polling = (_sidebarRevealed && _sidebarMode == SidebarMode.AutoHide) ||
                      (_titleRevealed && _titleBarMode == TitleBarMode.AutoHide);
        if (polling && !_chromePinned)
        {
            _chromeTimer?.Start();
        }
        else
        {
            _chromeTimer?.Stop();
        }
    }

    private static TranslateTransform Translate(UIElement element)
    {
        if (element.RenderTransform is not TranslateTransform t)
        {
            element.RenderTransform = t = new TranslateTransform();
        }

        return t;
    }

    /// <summary>Slides an overlay in from (or out to) <paramref name="hidden"/> along <paramref name="axis"/>.</summary>
    private void Slide(FrameworkElement element, string axis, bool show, double hidden)
    {
        var transform = Translate(element);
        if (show)
        {
            element.Visibility = Visibility.Visible;
            if (axis == "X")
            {
                transform.X = hidden;
            }
            else
            {
                transform.Y = hidden;
            }
        }

        var slide = new DoubleAnimation
        {
            To = show ? 0 : hidden,
            Duration = TimeSpan.FromMilliseconds(TestHooks.Deterministic ? 0 : 140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, axis);
        var board = new Storyboard { Children = { slide } };
        board.Completed += (_, _) =>
        {
            // Unless it was revealed again meanwhile.
            var revealed = ReferenceEquals(element, DragRegion) ? _titleRevealed : _sidebarRevealed;
            if (!show && !revealed)
            {
                element.Visibility = Visibility.Collapsed;
                if (ReferenceEquals(element, DragRegion))
                {
                    ReleaseTitleBarArea();
                }
            }
            else if (show && revealed && ReferenceEquals(element, DragRegion))
            {
                // WinUI takes the drag area from where the row is now, so this waits until the slide puts it in place.
                SetTitleBar(DragRegion);
            }

            _refreshClickThrough?.Invoke();
        };
        board.Begin();
    }

    /// <summary>
    /// Gives the hidden row's area back to the terminals, because WinUI keeps the drag area it
    /// last computed for the title bar element even once it's collapsed.
    /// </summary>
    private void ReleaseTitleBarArea()
    {
        if (_windowClosed)
        {
            return; // its AppWindow is gone
        }

        var source = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        source.ClearRegionRects(Microsoft.UI.Input.NonClientRegionKind.Caption);
    }

    /// <summary>Hides a revealed bar once the pointer has been away from it for a moment.</summary>
    private void HideChromeWhenAway()
    {
        if (_windowClosed)
        {
            return; // its AppWindow is gone
        }

        if (Content?.XamlRoot is not { } root || !GetCursorPos(out var screen))
        {
            return;
        }

        // Where the pointer is in the window's content, in DIPs.
        var client = new CursorPoint { X = screen.X, Y = screen.Y };
        ScreenToClient(WinRT.Interop.WindowNative.GetWindowHandle(this), ref client);
        var scale = root.RasterizationScale;
        var (x, y) = (client.X / scale, client.Y / scale);
        var inside = x >= 0 && y >= 0 && x < Root.ActualWidth && y < Root.ActualHeight;

        // Menus (a workspace's context menu, the profile menu, rename) and drags keep them open.
        var busy = VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0 || App.Instance.Drag != null;
        var now = Environment.TickCount64;

        bool Away(bool near, ref long since)
        {
            if (near || busy)
            {
                since = 0;
                return false;
            }

            if (since == 0)
            {
                since = now;
            }

            return now - since > 400;
        }

        if (_sidebarRevealed && _sidebarMode == SidebarMode.AutoHide &&
            Away(inside && x < WorkspaceList.DefaultWidth + 24, ref _sidebarAwaySince))
        {
            RevealSidebar(false);
        }

        if (_titleRevealed && _titleBarMode == TitleBarMode.AutoHide &&
            Away(y > -16 && y < TitleBarHeight + 16 && x >= -8 && x < Root.ActualWidth + 8, ref _titleAwaySince))
        {
            RevealTitleBar(false);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(IntPtr hwnd, ref CursorPoint point);
}
