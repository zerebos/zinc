using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Zinc;

/// <summary>
/// A drag run by Windows instead of XAML: the window takes the mouse capture and a window subclass gets the moves, the
/// release and the pointer-shape requests until the button comes up. The quick terminal's edges use it: dragged through
/// XAML, the pointer turned into the arrow the moment the button went down (XAML picks the shape itself while it holds
/// the pointer) and back with every move, a flicker no amount of setting the shape again could stop.
/// </summary>
internal static unsafe partial class Win32Drag
{
    private static nint _hwnd;
    private static nint _cursor;
    private static Action? _moved;
    private static Action? _ended;

    public static bool Active => _hwnd != 0;

    /// <summary>
    /// Starts a drag on <paramref name="hwnd"/> showing system cursor <paramref name="cursorId"/> (IDC_*):
    /// <paramref name="moved"/> on each move (read the pointer with GetCursorPos), <paramref name="ended"/> once.
    /// The capture is taken just after the press handler returns: XAML takes its own while handling the press.
    /// </summary>
    public static void Start(nint hwnd, int cursorId, Action moved, Action ended, DispatcherQueue queue)
    {
        Stop();
        _hwnd = hwnd;
        _cursor = LoadCursorW(0, cursorId);
        _moved = moved;
        _ended = ended;
        var subclassed = SetWindowSubclass(hwnd, &Proc, SubclassId, 0);
        SetCursor(_cursor);
        Log.Write($"drag: start on {hwnd:X} (subclassed {subclassed}, capture {GetCapture():X})");
        _seen.Clear();
        queue.SafeEnqueue(DispatcherQueuePriority.High, () =>
        {
            if (_hwnd == hwnd)
            {
                var before = GetCapture();
                SetCapture(hwnd);
                SetCursor(_cursor);
                Log.Write($"drag: capture {before:X} -> {GetCapture():X}");
            }
        });
    }

    /// <summary>Ends the drag (if one runs) and calls its end action.</summary>
    public static void Stop()
    {
        if (_hwnd == 0)
        {
            return;
        }

        var hwnd = _hwnd;
        var ended = _ended;
        Log.Write($"drag: end (capture {GetCapture():X})");
        _hwnd = 0;
        _moved = _ended = null;
        RemoveWindowSubclass(hwnd, &Proc, SubclassId);
        if (GetCapture() == hwnd)
        {
            ReleaseCapture();
        }

        ended?.Invoke();
    }

    private const nuint SubclassId = 0x5144524D; // "QDRM"
    private static readonly HashSet<uint> _seen = [];

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (_seen.Add(msg) && msg is 0x0020 or 0x0200 or 0x0202 or 0x0215 or 0x0245 or 0x0247)
            {
                Log.Write($"drag: first message {msg:X4}");
            }

            switch (msg)
            {
                case 0x0020: // WM_SETCURSOR
                    SetCursor(_cursor);
                    return 1;
                case 0x0200 or 0x0245: // WM_MOUSEMOVE, WM_POINTERUPDATE
                    if ((GetAsyncKeyState(0x01 /* VK_LBUTTON */) & 0x8000) == 0)
                    {
                        Stop(); // the release went elsewhere
                        return 0;
                    }

                    SetCursor(_cursor);
                    _moved?.Invoke();
                    SetCursor(_cursor);
                    return 0;
                case 0x0202 or 0x0247: // WM_LBUTTONUP, WM_POINTERUP
                    Stop();
                    return 0;
                case 0x0215: // WM_CAPTURECHANGED: someone else took the mouse (or ours was released)
                    if (lParam != hwnd)
                    {
                        Stop();
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Crash("quick terminal drag", ex);
            Stop();
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
}
