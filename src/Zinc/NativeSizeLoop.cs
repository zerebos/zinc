using System.Runtime.InteropServices;

namespace Zinc;

/// <summary>
/// Resizing a window through Windows' own size loop (the one a window border starts), with each step's rectangle
/// chosen by the caller. The quick terminal's edges use it: dragged through XAML, the pointer turned into the arrow the
/// moment the button went down and fought the resize shape on every move; in Windows' loop the pointer, the capture and
/// the shape are Windows' own (and a Win32 capture drag got no moves at all: WinUI's input doesn't come as messages).
/// </summary>
internal static unsafe partial class NativeSizeLoop
{
    private static Func<Rect>? _step;
    private static int _steps;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// Runs the loop for <paramref name="edge"/> (WMSZ_*: 1 left ... 8 bottom-right) while the left button is held;
    /// returns when it's released. <paramref name="step"/> gives the window's rectangle (screen pixels) for each step.
    /// </summary>
    public static void Run(nint hwnd, int edge, Func<Rect> step)
    {
        _step = step;
        _steps = 0;
        SetWindowSubclass(hwnd, &Proc, SubclassId, 0);
        try
        {
            ReleaseCapture(); // XAML's, from the press
            Log.Write($"size loop: starting (edge {edge})");
            SendMessageW(hwnd, 0x0112 /* WM_SYSCOMMAND */, 0xF000 /* SC_SIZE */ + edge, 0);
        }
        finally
        {
            RemoveWindowSubclass(hwnd, &Proc, SubclassId);
            Log.Write($"size loop: ended after {_steps} steps");
            _step = null;
        }
    }

    private const nuint SubclassId = 0x51535A4C; // "QSZL"

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == 0x0214 /* WM_SIZING */ && _step != null)
        {
            try
            {
                *(Rect*)lParam = _step();
                _steps++;
                return 1;
            }
            catch (Exception ex)
            {
                Log.Crash("quick terminal resize", ex);
            }
        }

        if (msg is 0x0231 or 0x0232) // WM_ENTERSIZEMOVE, WM_EXITSIZEMOVE
        {
            Log.Write($"size loop: {(msg == 0x0231 ? "entered" : "exited")}");
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
}
