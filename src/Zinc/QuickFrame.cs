using System.Runtime.InteropServices;

namespace Zinc;

/// <summary>
/// The quick terminal's frame: a real resizing frame drawn nowhere. WM_NCCALCSIZE makes the whole window client area
/// (no light strip across the top, no margins), and border regions on its edges (InputNonClientPointerSource, set by
/// the window) make Windows treat them as borders: its own resize pointer, its own press-drag-release loop. Each step's
/// rectangle comes from <see cref="Sizing"/> (centered stays centered).
/// </summary>
/// <remarks>
/// Why not XAML strips that move the window: every window move makes Windows refresh the pointer, and XAML answered
/// with the arrow, which flickered against the resize shape all drag long (a pane divider, which only changes layout,
/// held its shape). Windows' loop driven from XAML failed too: WinUI's press never reaches Windows' button state, so
/// the loop ran click-to-finish, and a Win32 capture got no moves (WinUI's input doesn't come as window messages).
/// </remarks>
internal static unsafe partial class QuickFrame
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>A drag on a border began (WM_ENTERSIZEMOVE).</summary>
    public static Action? Started { get; set; }

    /// <summary>Each step of a drag: the edge (WMSZ_*: 1 left ... 8 bottom-right); returns the rectangle to use.</summary>
    public static Func<int, Rect, Rect>? Sizing { get; set; }

    /// <summary>A drag ended (WM_EXITSIZEMOVE).</summary>
    public static Action? Sized { get; set; }

    /// <summary>A border was double-clicked (Windows would stretch the window to the screen's height).</summary>
    public static Action? DoubleClicked { get; set; }

    public static void Attach(nint hwnd) => SetWindowSubclass(hwnd, &Proc, SubclassId, 0);

    /// <summary>
    /// Zinc is placing the window (summoning it, a reset): the moves and resizes it makes go through. Any other comes
    /// from Windows' snapping (Win+arrows snap any window with a resizing frame) and is refused, except a border drag.
    /// </summary>
    public static bool Placing { get; set; }

    private static bool _dragging;

    private const nuint SubclassId = 0x5146524D; // "QFRM"

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            switch (msg)
            {
                case 0x0083 when wParam != 0: // WM_NCCALCSIZE: the client area is the whole window (no frame drawn)
                    return 0;
                case 0x0085: // WM_NCPAINT: no frame to paint (Windows painted a white one on a DPI change, around the fade)
                    return 0;
                case 0x0086: // WM_NCACTIVATE: the same on losing focus; lParam -1 = don't repaint the frame
                    return DefSubclassProc(hwnd, msg, wParam, -1);
                case 0x0046 when !Placing && !_dragging: // WM_WINDOWPOSCHANGING from someone else: stay put
                    ((WindowPos*)lParam)->Flags |= 0x0001 | 0x0002; // SWP_NOSIZE | SWP_NOMOVE
                    break;
                case 0x0231: // WM_ENTERSIZEMOVE
                    _dragging = true;
                    Started?.Invoke();
                    break;
                case 0x0214 when Sizing != null: // WM_SIZING
                    *(Rect*)lParam = Sizing((int)wParam, *(Rect*)lParam);
                    return 1;
                case 0x0232: // WM_EXITSIZEMOVE
                    _dragging = false;
                    Sized?.Invoke();
                    break;
                case 0x00A3 when wParam is >= 10 and <= 17: // WM_NCLBUTTONDBLCLK on a border (HTLEFT ... HTBOTTOMRIGHT)
                    DoubleClicked?.Invoke();
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Crash("quick terminal frame", ex);
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
}
