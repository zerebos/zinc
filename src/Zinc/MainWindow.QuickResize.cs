using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Zinc;

// Resizing the quick terminal by its edges. Its frame is a real resizing frame drawn nowhere (QuickFrame), with border
// regions along the edges that aren't against the screen: Windows runs the drags, with its own pointer, and each step
// gets the rectangle Core's QuickTerminalResize and Place give (centered stays centered). The size is remembered for
// that monitor (App.QuickTerminalSizes, saved with the session) until the config places it differently.
// Double-clicking an edge, or the palette's "Reset quick terminal size", goes back to the configured size.
public sealed partial class MainWindow
{
    // The minimum size, in DIPs: a few dozen columns and lines.
    private const double QuickMinWidth = 320, QuickMinHeight = 160;

    // How far in from the window's edge a border reaches, in DIPs.
    private const double QuickBorder = 6;

    // The drag in progress: where the pointer and the size started (physical pixels), and the monitor.
    private CursorPoint _resizeFrom;
    private (int Width, int Height) _resizeStart;
    private DisplayArea? _resizeArea;

    /// <summary>Hooks the frame up (once, when the quick window is made).</summary>
    private void AttachQuickFrame(nint hwnd)
    {
        QuickFrame.Attach(hwnd);
        QuickFrame.Started = () =>
        {
            GetCursorPos(out _resizeFrom);
            _resizeStart = (AppWindow.Size.Width, AppWindow.Size.Height);
            _resizeArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        };
        QuickFrame.Sizing = ResizeStep;
        QuickFrame.Sized = ResizeEnded;
        QuickFrame.DoubleClicked = ResetQuickSize;
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidSizeChange && !_windowClosed)
            {
                UpdateBorders();
            }
        };
    }

    /// <summary>The border regions for the window's size and the configured position (none against the screen edge).</summary>
    private void UpdateBorders()
    {
        if (_windowClosed)
        {
            return;
        }

        var w = AppWindow.Size.Width;
        var h = AppWindow.Size.Height;
        var b = (int)Math.Round(QuickBorder * Scale());
        var edges = QuickTerminalResize.Edges(_config.Global.QuickTerminal.Position);
        RectInt32[] Where(QuickTerminalEdge edge, RectInt32 rect) => edges.Contains(edge) ? [rect] : [];
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        source.SetRegionRects(NonClientRegionKind.LeftBorder, Where(QuickTerminalEdge.Left, new RectInt32(0, 0, b, h)));
        source.SetRegionRects(NonClientRegionKind.RightBorder, Where(QuickTerminalEdge.Right, new RectInt32(w - b, 0, b, h)));
        source.SetRegionRects(NonClientRegionKind.TopBorder, Where(QuickTerminalEdge.Top, new RectInt32(0, 0, w, b)));
        source.SetRegionRects(NonClientRegionKind.BottomBorder, Where(QuickTerminalEdge.Bottom, new RectInt32(0, h - b, w, b)));
    }

    /// <summary>The key a monitor's dragged size is kept under: where it is and how big (stable while the setup is).</summary>
    private static string MonitorKey(DisplayArea area) =>
        $"{area.OuterBounds.X},{area.OuterBounds.Y},{area.OuterBounds.Width}x{area.OuterBounds.Height}";

    // WM_SIZING's edge numbers (WMSZ_*).
    private static QuickTerminalEdge EdgeOf(int wmsz) => wmsz switch
    {
        1 => QuickTerminalEdge.Left,
        2 => QuickTerminalEdge.Right,
        3 => QuickTerminalEdge.Top,
        4 => QuickTerminalEdge.TopLeft,
        5 => QuickTerminalEdge.TopRight,
        6 => QuickTerminalEdge.Bottom,
        7 => QuickTerminalEdge.BottomLeft,
        _ => QuickTerminalEdge.BottomRight,
    };

    /// <summary>One step of a drag: the size the pointer gives, placed as the window belongs.</summary>
    private QuickFrame.Rect ResizeStep(int wmsz, QuickFrame.Rect proposed)
    {
        if (_resizeArea is not { } area || _windowClosed || !GetCursorPos(out var at))
        {
            return proposed;
        }

        var settings = _config.Global.QuickTerminal;
        var work = area.WorkArea;
        var scale = Scale();
        var size = QuickTerminalResize.Size(settings.Position, EdgeOf(wmsz), _resizeStart, at.X - _resizeFrom.X, at.Y - _resizeFrom.Y,
            ((int)Math.Round(QuickMinWidth * scale), (int)Math.Round(QuickMinHeight * scale)), (work.Width, work.Height));
        var r = QuickTerminalLayout.Place(new QuickTerminalLayout.Rect(work.X, work.Y, work.Width, work.Height), settings, size);
        return new QuickFrame.Rect { Left = r.X, Top = r.Y, Right = r.X + r.Width, Bottom = r.Y + r.Height };
    }

    /// <summary>A drag ended: the size is remembered for this monitor (unless it didn't change).</summary>
    private void ResizeEnded()
    {
        if (_resizeArea is not { } area || _windowClosed)
        {
            return;
        }

        _resizeArea = null;
        var size = (AppWindow.Size.Width, AppWindow.Size.Height);
        if (size == _resizeStart)
        {
            return; // a click, or dragged back to where it was
        }

        App.Instance.QuickTerminalSizes.Remember(MonitorKey(area), _config.Global.QuickTerminal, size.Width, size.Height);
        Log.Write($"quick terminal: resized to {size.Width}x{size.Height} on {MonitorKey(area)}");
    }

    /// <summary>Whether the monitor it's on has a dragged size (the palette offers to reset it).</summary>
    private bool HasQuickSize() =>
        App.Instance.QuickTerminalSizes.For(MonitorKey(DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)),
            _config.Global.QuickTerminal) != null;

    /// <summary>Back to the configured size on this monitor.</summary>
    private void ResetQuickSize()
    {
        if (_windowClosed)
        {
            return;
        }

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        App.Instance.QuickTerminalSizes.Forget(MonitorKey(area));
        var work = area.WorkArea;
        var r = QuickTerminalLayout.Place(new QuickTerminalLayout.Rect(work.X, work.Y, work.Width, work.Height), _config.Global.QuickTerminal);
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
        Log.Write($"quick terminal: size reset on {MonitorKey(area)}");
    }
}
