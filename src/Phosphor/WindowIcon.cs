using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Phosphor;

/// <summary>
/// Sets a window's small and large icons from a multi-size .ico, picking the exact
/// frames for the window's DPI. AppWindow.SetIcon rescales a single frame instead,
/// which smears the pixel-art mark at 16px.
/// </summary>
internal static partial class WindowIcon
{
    public static void Apply(Window window, string icoPath)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var dpi = GetDpiForWindow(hwnd);
        Set(hwnd, IconSmall, icoPath, GetSystemMetricsForDpi(SmCxSmIcon, dpi), GetSystemMetricsForDpi(SmCySmIcon, dpi));
        Set(hwnd, IconBig, icoPath, GetSystemMetricsForDpi(SmCxIcon, dpi), GetSystemMetricsForDpi(SmCyIcon, dpi));
    }

    private static void Set(nint hwnd, nint which, string path, int width, int height)
    {
        // LoadImage chooses the closest frame for the requested size. The icons live as
        // long as the window, which for Phosphor is the process, so they're never freed.
        var icon = LoadImageW(0, path, ImageIcon, width, height, LrLoadFromFile);
        if (icon != 0)
        {
            SendMessageW(hwnd, WmSetIcon, which, icon);
        }
    }

    private const uint WmSetIcon = 0x0080;
    private const nint IconSmall = 0, IconBig = 1;
    private const uint ImageIcon = 1, LrLoadFromFile = 0x10;
    private const int SmCxIcon = 11, SmCyIcon = 12, SmCxSmIcon = 49, SmCySmIcon = 50;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);
}
