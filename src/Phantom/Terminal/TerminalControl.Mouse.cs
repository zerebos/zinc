using System.Diagnostics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Phantom.Ghostty;
using Windows.System;

namespace Phantom.Terminal;

// Mouse input: app mouse reporting when enabled (Shift bypasses it), otherwise
// selection, right-click copy/paste and scrollback.
public sealed partial class TerminalControl
{
    private bool _selecting;
    private MouseButton? _reportedButton;
    private bool _arrowCursor;
    private InputSystemCursorShape _cursorShape;

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var session = _session;
        if (session == null || IsFromScrollBar(e.OriginalSource))
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
            Select(GesturePhase.Press, point.Position);
        }
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        var session = _session;
        if (session == null || (!_selecting && IsFromScrollBar(e.OriginalSource)))
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
            Select(GesturePhase.Release, e.GetCurrentPoint(_canvas).Position);
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
        if (session == null)
        {
            return;
        }

        e.Handled = true;
        var delta = e.GetCurrentPoint(_canvas).Properties.MouseWheelDelta;
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

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        SetScrollBarExpanded(false);
        _nearScrollBar = false;
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
        var padLeftPx = (uint)(_config.PaddingLeft * m.Scale);
        var padTopPx = (uint)(_config.PaddingTop * m.Scale);
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
        var col = (ushort)Math.Clamp((int)((pos.X - _config.PaddingLeft) / m.Width), 0, _cols - 1);
        var row = (uint)Math.Clamp((int)((pos.Y - _config.PaddingTop) / m.Height), 0, _rows - 1);
        var geometry = new GhosttySelectionGestureGeometry
        {
            Columns = _cols,
            CellWidth = m.WidthPx,
            PaddingLeft = (uint)(_config.PaddingLeft * m.Scale),
            ScreenHeight = (uint)(_canvas.ActualHeight * m.Scale),
        };
        var nowNs = (ulong)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));

        _selection.Handle(phase, col, row, pos.X * m.Scale, pos.Y * m.Scale, geometry, nowNs, 4.0 * m.Scale);
        _canvas.Invalidate();
    }
}
