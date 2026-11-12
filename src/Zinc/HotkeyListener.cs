using System.Runtime.InteropServices;

namespace Zinc;

/// <summary>A system-wide hotkey, delivered to the app's <see cref="MessageWindow"/>.</summary>
internal sealed partial class HotkeyListener : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const int HotkeyId = 0x5048; // 'PH'

    private readonly MessageWindow _window;
    private readonly GlobalHotkey _hotkey;

    public HotkeyListener(MessageWindow window, GlobalHotkey hotkey)
    {
        _window = window;
        _hotkey = hotkey;
        window.Message += OnMessage;
    }

    public event Action? Pressed;

    public bool IsRegistered { get; private set; }

    /// <summary>Tries to claim the hotkey, and returns false if another app holds it.</summary>
    public bool TryRegister()
    {
        if (!IsRegistered && _window.Hwnd != 0)
        {
            IsRegistered = RegisterHotKey(_window.Hwnd, HotkeyId, _hotkey.Modifiers | GlobalHotkey.NoRepeat, (uint)_hotkey.Key);
            Log.Write($"hotkey: register {_hotkey}: {IsRegistered}");
        }

        return IsRegistered;
    }

    public void Dispose()
    {
        _window.Message -= OnMessage;
        if (IsRegistered)
        {
            UnregisterHotKey(_window.Hwnd, HotkeyId);
            IsRegistered = false;
        }
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg != WmHotkey || wParam != HotkeyId)
        {
            return false;
        }

        Pressed?.Invoke();
        return true;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}
