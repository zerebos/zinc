using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Zinc;

/// <summary>
/// Stops Windows from beeping on Alt+letter shortcuts (Alt+Shift+D, for example). Alt plus a
/// character key makes DefWindowProc look for a menu mnemonic (WM_SYSCOMMAND /
/// SC_KEYMENU). Zinc has no menu bar, so the lookup always fails and beeps, even
/// though our key handler already ran the action. Alt+Space (the window's system
/// menu) and a bare Alt tap still reach Windows.
/// </summary>
internal static unsafe partial class MenuKeySuppressor
{
    private const uint WmSysCommand = 0x0112;
    private const int ScKeyMenu = 0xF100;

    public static void Attach(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowSubclass(hwnd, &SubclassProc, 1, 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        // lParam carries the character: 0 for a bare Alt tap, ' ' for Alt+Space.
        if (msg == WmSysCommand && ((int)wParam & 0xFFF0) == ScKeyMenu && lParam != 0 && lParam != ' ')
        {
            Log.Write($"suppressed SC_KEYMENU for '{(char)lParam}'");
            return 0;
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd,
        delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
}
