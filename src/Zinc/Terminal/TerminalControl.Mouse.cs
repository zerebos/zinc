using System.Diagnostics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Zinc.Ghostty;
using Windows.System;

namespace Zinc.Terminal;

// Mouse input: app mouse reporting when enabled (Shift bypasses it), otherwise
// selection, right-click copy/paste and scrollback.
public sealed partial class TerminalControl
{
    private bool _selecting;

    /// <summary>Where a plain left press landed, for click-to-move on its release.</summary>
    private (int Col, int Row)? _clickCell;
    private MouseButton? _reportedButton;
    private bool _arrowCursor;
    private InputSystemCursorShape _cursorShape;

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        // A click that brings the pane into focus only focuses it (no click-to-move).
        var wasFocused = FocusIsWithin();
        Focus(FocusState.Pointer);
        var session = _session;
        if (session == null || IsFromScrollBar(e.OriginalSource) || IsFromSearch(e.OriginalSource))
        {
            return;
        }

        var point = e.GetCurrentPoint(_canvas);
        var props = point.Properties;
        e.Handled = true;
        if (props.IsLeftButtonPressed && (e.KeyModifiers & VirtualKeyModifiers.Control) != 0 &&
            LinkUnder(point.Position) is { } link)
        {
            // Links win over app mouse reporting, like selecting with Shift.
            OpenLink(link.Uri);
            return;
        }

        CapturePointer(e.Pointer);

        if (ShouldReportMouse(session, e))
        {
            var button = props.IsLeftButtonPressed ? MouseButton.Left
                : props.IsRightButtonPressed ? MouseButton.Right
                : props.IsMiddleButtonPressed ? MouseButton.Middle
                : MouseButton.Unknown;
            _reportedButton = button;
            SendMouse(session, MouseAction.Press, button, e, anyPressed: true);
            return;
        }

        if (props.IsRightButtonPressed)
        {
            // Windows Terminal convention: right-click copies a selection, or pastes.
            if (HasSelection()) CopySelection();
            else PasteFromClipboard();
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            _selecting = true;
            _clickCell = wasFocused && (e.KeyModifiers & (VirtualKeyModifiers.Shift | VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu)) == 0
                ? CellAt(point.Position)
                : null;
            Select(GesturePhase.Press, point.Position);
        }
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        var session = _session;
        if (session == null || (!_selecting && (IsFromScrollBar(e.OriginalSource) || IsFromSearch(e.OriginalSource))))
        {
            return;
        }

        var point = e.GetCurrentPoint(_canvas);
        _pointerPosition = point.Position;
        UpdateHoverLink((e.KeyModifiers & VirtualKeyModifiers.Control) != 0);
        var reporting = !_selecting && ShouldReportMouse(session, e);
        UpdatePointerCursor(arrow: reporting || _nearScrollBar || !_config.TextPointer);

        if (_selecting)
        {
            Select(GesturePhase.Drag, point.Position);
            e.Handled = true;
        }
        else if (reporting)
        {
            SendMouse(session, MouseAction.Motion, _reportedButton, e, anyPressed: _reportedButton != null);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        var session = _session;
        ReleasePointerCapture(e.Pointer);
        if (session == null)
        {
            return;
        }

        e.Handled = true;
        if (_selecting)
        {
            _selecting = false;
            var position = e.GetCurrentPoint(_canvas).Position;
            Select(GesturePhase.Release, position);

            // Copy on select: the text goes to the clipboard, and stays selected.
            var selected = session.GetSelectionText();
            if (_config.CopyOnSelect && selected != null)
            {
                ClipboardText.Set(selected);
            }

            // A plain click (no drag, nothing selected) in a shell's command line moves its cursor there.
            if (_clickCell is { } pressed && selected == null && CellAt(position) == pressed)
            {
                ClickToMove(pressed.Col, pressed.Row);
            }

            _clickCell = null;
        }
        else if (_reportedButton is { } button)
        {
            _reportedButton = null;
            SendMouse(session, MouseAction.Release, button, e, anyPressed: false);
        }
    }

    protected override unsafe void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        var session = _session;
        if (session == null || IsFromSearch(e.OriginalSource))
        {
            return;
        }

        e.Handled = true;
        var delta = e.GetCurrentPoint(_canvas).Properties.MouseWheelDelta;

        // Ctrl+wheel zooms (before programs' mouse reporting, as in Windows Terminal). A precision
        // touchpad sends small steps: they add up to a notch (120) per point of font size.
        if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0)
        {
            _zoomDelta += delta;
            var level = _zoomLevel;
            while (Math.Abs(_zoomDelta) >= 120)
            {
                var step = Math.Sign(_zoomDelta);
                level += step;
                _zoomDelta -= step * 120;
            }

            if (level != _zoomLevel)
            {
                Zoom(level);
            }

            return;
        }

        _zoomDelta = 0;
        var notches = Math.Max(1, Math.Abs(delta) / 120);
        var up = delta > 0;

        if (ShouldReportMouse(session, e))
        {
            for (var i = 0; i < notches; i++)
            {
                SendMouse(session, MouseAction.Press, up ? MouseButton.Four : MouseButton.Five, e, anyPressed: false);
            }

            return;
        }

        bool alternateScreen;
        lock (session.Lock)
        {
            int screen;
            Native.TerminalGet(session.Handle, TerminalData.ActiveScreen, &screen);
            alternateScreen = screen == 1;
        }

        if (alternateScreen && session.GetMode(GhosttyMode.AltScroll))
        {
            // Full-screen apps without mouse support (less, man) get arrow keys.
            var key = up ? GhosttyKey.ArrowUp : GhosttyKey.ArrowDown;
            for (var i = 0; i < notches * 3; i++)
            {
                session.SendInput(session.EncodeKey(KeyAction.Press, key, GhosttyMods.None, GhosttyMods.None, null, 0));
            }

            return;
        }

        session.ScrollLines((up ? -3 : 3) * notches);
        UpdateHoverLink((e.KeyModifiers & VirtualKeyModifiers.Control) != 0); // new text under the pointer
        _canvas.Invalidate();
    }

    /// <summary>
    /// Focus follows the pointer (<c>focus-follows-mouse</c>): entering a pane focuses it, but only
    /// when focus is in another terminal (not the palette, a find box or a menu) and no button is
    /// held (a drag passing through).
    /// </summary>
    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!_config.FocusFollowsMouse || XamlRoot == null || e.GetCurrentPoint(this).Properties is { IsLeftButtonPressed: true } or
            { IsRightButtonPressed: true } or { IsMiddleButtonPressed: true })
        {
            return;
        }

        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused &&
            TerminalOf(focused) is { } other && other != this)
        {
            Focus(FocusState.Pointer);
        }
    }

    /// <summary>Ctrl+wheel movement not yet a whole notch (precision touchpads send small steps).</summary>
    private int _zoomDelta;

    /// <summary>The terminal <paramref name="element"/> is part of, if any.</summary>
    private static TerminalControl? TerminalOf(DependencyObject element)
    {
        for (DependencyObject? at = element; at != null; at = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(at))
        {
            if (at is TerminalControl terminal)
            {
                return terminal;
            }
        }

        return null;
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        // (Dragging the thumb out of the pane keeps the bar: see ReleaseScrollBar.)
        if (!_scrollBarHeld)
        {
            SetScrollBarExpanded(false);
            _nearScrollBar = false;
        }

        _pointerPosition = null;
        UpdateHoverLink(ctrl: false);
    }

    /// <summary>
    /// The pointer shape: an arrow by default (like Windows Terminal), over the
    /// scrollbar, and while the app consumes the mouse; with <c>mouse-pointer = text</c>,
    /// an I-beam where the mouse selects text.
    /// </summary>
    private void UpdatePointerCursor(bool arrow)
    {
        _arrowCursor = arrow;
        // A hand over a Ctrl+hovered link, else what the pointer would do here.
        var shape = _renderer.HoverLink != null ? InputSystemCursorShape.Hand
            : arrow ? InputSystemCursorShape.Arrow
            : InputSystemCursorShape.IBeam;
        if (shape != _cursorShape)
        {
            _cursorShape = shape;
            ProtectedCursor = InputSystemCursor.Create(shape);
        }
    }

    private static bool ShouldReportMouse(TerminalSession session, PointerRoutedEventArgs e) =>
        // Holding Shift bypasses mouse reporting so text can still be selected.
        (e.KeyModifiers & VirtualKeyModifiers.Shift) == 0 && session.IsMouseTracking;

    private void SendMouse(TerminalSession session, MouseAction action, MouseButton? button, PointerRoutedEventArgs e,
        bool anyPressed)
    {
        var m = _renderer.Metrics;
        var pos = e.GetCurrentPoint(_canvas).Position;
        var mods = GhosttyMods.None;
        if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0) mods |= GhosttyMods.Shift;
        if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0) mods |= GhosttyMods.Ctrl;
        if ((e.KeyModifiers & VirtualKeyModifiers.Menu) != 0) mods |= GhosttyMods.Alt;

        var widthPx = (uint)(_canvas.ActualWidth * m.Scale);
        var heightPx = (uint)(_canvas.ActualHeight * m.Scale);
        var padLeftPx = (uint)(_gridLeft * m.Scale);
        var padTopPx = (uint)(_gridTop * m.Scale);
        var size = new GhosttyMouseEncoderSize
        {
            ScreenWidth = widthPx,
            ScreenHeight = heightPx,
            CellWidth = m.WidthPx,
            CellHeight = m.HeightPx,
            PaddingLeft = padLeftPx,
            PaddingTop = padTopPx,
            PaddingRight = (uint)Math.Max(0, (int)widthPx - (int)padLeftPx - _cols * (int)m.WidthPx),
            PaddingBottom = (uint)Math.Max(0, (int)heightPx - (int)padTopPx - _rows * (int)m.HeightPx),
        };

        var bytes = session.EncodeMouse(action, button, mods, (float)(pos.X * m.Scale), (float)(pos.Y * m.Scale), size,
            anyPressed);
        if (bytes.Length > 0)
        {
            session.SendInput(bytes);
        }
    }

    private void Select(GesturePhase phase, Windows.Foundation.Point pos)
    {
        if (_selection == null)
        {
            return;
        }

        var m = _renderer.Metrics;
        var (col, row) = CellAt(pos);
        var geometry = new GhosttySelectionGestureGeometry
        {
            Columns = _cols,
            CellWidth = m.WidthPx,
            PaddingLeft = (uint)(_gridLeft * m.Scale),
            ScreenHeight = (uint)(_canvas.ActualHeight * m.Scale),
        };
        var nowNs = (ulong)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));

        _selection.Handle(phase, (ushort)col, (uint)row, pos.X * m.Scale, pos.Y * m.Scale, geometry, nowNs, 4.0 * m.Scale);
        _canvas.Invalidate();
    }

    /// <summary>
    /// A plain click at viewport cell (<paramref name="col"/>, <paramref name="row"/>): in a shell's
    /// command line, moves its cursor there (<see cref="TerminalSession.ClickToMove"/>).
    /// <c>test:click:col:row</c> calls it directly.
    /// </summary>
    internal void ClickToMove(int col, int row)
    {
        if (_session is { } session && _config.ClickToMove &&
            session.ClickToMove(col, row, assumeArrows: true) is { Length: > 0 } move)
        {
            Log.Write($"click-to-move: ({col}, {row}) -> {System.Text.Encoding.ASCII.GetString(move).Replace("\x1b", "ESC")}");
            session.SendInput(move);
        }
    }

    /// <summary>The viewport cell under <paramref name="pos"/> (canvas DIPs), clamped to the grid.</summary>
    private (int Col, int Row) CellAt(Windows.Foundation.Point pos)
    {
        var m = _renderer.Metrics;
        return (Math.Clamp((int)((pos.X - _gridLeft) / m.Width), 0, _cols - 1),
            Math.Clamp((int)((pos.Y - _gridTop) / m.Height), 0, _rows - 1));
    }
}
