using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Phosphor;

/// <summary>
/// A hidden, never-shown top-level window on the UI thread that receives the app's
/// system-level messages (global hotkey, tray icon), so those keep working however
/// Phosphor's visible windows come and go. Top-level rather than message-only: only
/// top-level windows get Explorer's "TaskbarCreated" broadcast.
/// </summary>
internal sealed unsafe partial class MessageWindow : IDisposable
{
    public const string ClassName = "PhosphorMessageWindow";
    private static MessageWindow? _instance;

    public MessageWindow()
    {
        _instance = this;
        fixed (char* className = ClassName)
        {
            var wc = new WndClassEx
            {
                cbSize = (uint)sizeof(WndClassEx),
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandleW(null),
                lpszClassName = className,
            };
            RegisterClassExW(&wc); // fails harmlessly if already registered
            const uint WsPopup = 0x80000000, WsExToolWindow = 0x80;
            Hwnd = CreateWindowExW(WsExToolWindow, className, className, WsPopup, 0, 0, 0, 0, 0, 0, wc.hInstance, 0);
        }

        TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        // Test tools read this to post messages (e.g. WM_HOTKEY) to the window.
        Log.Write($"message window {Hwnd:X}");
    }

    public nint Hwnd { get; }

    /// <summary>The message Explorer broadcasts when the taskbar (re)starts.</summary>
    public uint TaskbarCreated { get; }

    /// <summary>Handlers get (msg, wParam, lParam) and return true when they handled it.</summary>
    public event Func<uint, nint, nint, bool>? Message;

    public void Dispose()
    {
        if (Hwnd != 0)
        {
            DestroyWindow(Hwnd);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (_instance?.Message is { } handlers)
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Func<uint, nint, nint, bool>>())
            {
                try
                {
                    if (handler(msg, wParam, lParam))
                    {
                        return 0;
                    }
                }
                catch (Exception ex)
                {
                    // An exception must not unwind into native code.
                    Log.Write($"message window: handler failed: {ex}");
                    return 0;
                }
            }
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint cbSize, style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WndClassEx* wc);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
