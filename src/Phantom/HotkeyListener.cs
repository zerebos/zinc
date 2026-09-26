using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Phantom;

/// <summary>
/// A system-wide hotkey, received by a hidden message-only window on the UI thread,
/// so it keeps working however Phantom's visible windows come and go.
/// </summary>
internal sealed unsafe partial class HotkeyListener : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const int HotkeyId = 0x5048; // 'PH'
    private static readonly nint HwndMessage = -3;
    private static HotkeyListener? _instance;

    private readonly nint _hwnd;
    private readonly GlobalHotkey _hotkey;

    public HotkeyListener(GlobalHotkey hotkey)
    {
        _hotkey = hotkey;
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
            _hwnd = CreateWindowExW(0, className, className, 0, 0, 0, 0, 0, HwndMessage, 0, wc.hInstance, 0);
        }

        // Test tools read this to post WM_HOTKEY; FindWindowEx(HWND_MESSAGE) can't see it from a sandbox.
        Log.Write($"hotkey: listener window {_hwnd:X}");
    }

    /// <summary>Window class of the message-only window (tests find it to post WM_HOTKEY).</summary>
    public const string ClassName = "PhantomHotkeyListener";

    public event Action? Pressed;

    public bool IsRegistered { get; private set; }

    /// <summary>Tries to claim the hotkey; false if another app holds it.</summary>
    public bool TryRegister()
    {
        if (!IsRegistered && _hwnd != 0)
        {
            IsRegistered = RegisterHotKey(_hwnd, HotkeyId, _hotkey.Modifiers | GlobalHotkey.NoRepeat, (uint)_hotkey.Key);
            Log.Write($"hotkey: register {_hotkey}: {IsRegistered}");
        }

        return IsRegistered;
    }

    public void Dispose()
    {
        if (IsRegistered)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            IsRegistered = false;
        }

        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmHotkey && wParam == HotkeyId)
        {
            _instance?.Pressed?.Invoke();
            return 0;
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
