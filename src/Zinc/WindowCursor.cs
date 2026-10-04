using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Zinc;

/// <summary>
/// Leaves the pointer's shape to XAML. WinUI's top-level window class comes with the arrow as its class cursor, so on
/// every WM_SETCURSOR (each mouse move, and more) Windows put the arrow back, and XAML its own shape after it: a resize
/// pointer over the quick terminal's edges or a pane divider flickered between the two. Class-wide: once is enough, but
/// it costs nothing to repeat for each window.
/// </summary>
internal static partial class WindowCursor
{
    public static void LeaveToXaml(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (GetClassLongPtrW(hwnd, GCLP_HCURSOR) != 0)
        {
            SetClassLongPtrW(hwnd, GCLP_HCURSOR, 0);
        }
    }

    private const int GCLP_HCURSOR = -12;

    [LibraryImport("user32.dll")]
    private static partial nint GetClassLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetClassLongPtrW(nint hwnd, int index, nint value);
}
