using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Zinc;

// Resizing the quick terminal by its edges. It has no frame (Windows' resizing frame drew a light strip and margins
// around it), so invisible strips along its edges do it: a drag resizes the window through Core's
// QuickTerminalResize, keeping its place (centered stays centered), and the size is remembered for that monitor
// (App.QuickTerminalSizes, saved with the session) until the config places it differently. Double-clicking an edge,
// or the palette's "Reset quick terminal size", goes back to the configured size.
public sealed partial class MainWindow
{
    // The minimum size, in DIPs: a few dozen columns and lines.
    private const double QuickMinWidth = 320, QuickMinHeight = 160;

    private Grid? _resizeEdges;
    private QuickTerminalPosition? _edgesFor;

    // The drag in progress: the edge, where the pointer and the size started (physical pixels), and the monitor.
    private QuickResizeEdge? _resizing;
    private QuickResizeEdge? _resizeCover;
    private CursorPoint _resizeFrom;
    private (int Width, int Height) _resizeStart;
    private DisplayArea? _resizeArea;

    /// <summary>The strips for the configured position (none against the screen edge it sits on), made again when it changes.</summary>
    private void UpdateResizeEdges()
    {
        var position = _config.Global.QuickTerminal.Position;
        if (_edgesFor == position)
        {
            return;
        }

        if (_resizeEdges == null)
        {
            _resizeEdges = new Grid();
            Canvas.SetZIndex(_resizeEdges, 5); // over the terminal, under the palette
            Root.Children.Add(_resizeEdges);
        }

        _edgesFor = position;
        _resizeEdges.Children.Clear();
        foreach (var edge in QuickTerminalResize.Edges(position))
        {
            var strip = new QuickResizeEdge(edge);
            strip.PointerPressed += OnResizePressed;
            strip.PointerMoved += OnResizeMoved;
            strip.PointerReleased += OnResizeEnded;
            strip.PointerCaptureLost += OnResizeEnded;
            strip.DoubleTapped += (_, e) =>
            {
                e.Handled = true;
                ResetQuickSize();
            };
            _resizeEdges.Children.Add(strip);
        }
    }

    /// <summary>The key a monitor's dragged size is kept under: where it is and how big (stable while the setup is).</summary>
    private static string MonitorKey(DisplayArea area) =>
        $"{area.OuterBounds.X},{area.OuterBounds.Y},{area.OuterBounds.Width}x{area.OuterBounds.Height}";

    private void OnResizePressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not QuickResizeEdge strip || !_quickShown || _windowClosed ||
            !e.GetCurrentPoint(strip).Properties.IsLeftButtonPressed || !GetCursorPos(out var from))
        {
            return;
        }

        e.Handled = true;
        if (!strip.CapturePointer(e.Pointer))
        {
            return;
        }

        _resizing = strip;
        _resizeCover = new QuickResizeEdge(strip.Edge, cover: true);
        _resizeEdges?.Children.Add(_resizeCover);
        _resizeFrom = from;
        _resizeStart = (AppWindow.Size.Width, AppWindow.Size.Height);
        _resizeArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
    }

    private void OnResizeMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _resizing) || _resizeArea is not { } area || _windowClosed || !GetCursorPos(out var at))
        {
            return;
        }

        e.Handled = true;
        var settings = _config.Global.QuickTerminal;
        var work = area.WorkArea;
        var scale = Scale();
        var size = QuickTerminalResize.Size(settings.Position, _resizing.Edge, _resizeStart, at.X - _resizeFrom.X, at.Y - _resizeFrom.Y,
            ((int)Math.Round(QuickMinWidth * scale), (int)Math.Round(QuickMinHeight * scale)), (work.Width, work.Height));
        var r = QuickTerminalLayout.Place(new QuickTerminalLayout.Rect(work.X, work.Y, work.Width, work.Height), settings, size);
        if (r.Width != AppWindow.Size.Width || r.Height != AppWindow.Size.Height || r.X != AppWindow.Position.X || r.Y != AppWindow.Position.Y)
        {
            AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
        }
    }

    private void OnResizeEnded(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _resizing))
        {
            return;
        }

        var strip = _resizing;
        _resizing = null;
        strip.ReleasePointerCaptures();
        if (_resizeCover != null)
        {
            _resizeEdges?.Children.Remove(_resizeCover);
            _resizeCover = null;
        }

        if (_resizeArea is not { } area || _windowClosed)
        {
            return;
        }

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
