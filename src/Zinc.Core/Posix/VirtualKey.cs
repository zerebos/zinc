// Spike: Core's key bindings are written against WinRT's VirtualKey. Off Windows there's no WinRT, so this stands in
// with the same names and Win32 values. Uno has its own copy, so the head never names this type and goes through
// KeyCodes instead.
namespace Windows.System
{
    public enum VirtualKey
    {
        None = 0, Back = 8, Tab = 9, Clear = 12, Enter = 13, Shift = 16, Control = 17, Menu = 18, Pause = 19,
        CapitalLock = 20, Escape = 27, Space = 32, PageUp = 33, PageDown = 34, End = 35, Home = 36, Left = 37, Up = 38,
        Right = 39, Down = 40, Print = 42, Snapshot = 44, Insert = 45, Delete = 46,
        Number0 = 48, Number1, Number2, Number3, Number4, Number5, Number6, Number7, Number8, Number9,
        A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        LeftWindows = 91, RightWindows = 92, Application = 93,
        NumberPad0 = 96, NumberPad1, NumberPad2, NumberPad3, NumberPad4, NumberPad5, NumberPad6, NumberPad7, NumberPad8, NumberPad9,
        Multiply = 106, Add = 107, Separator = 108, Subtract = 109, Decimal = 110, Divide = 111,
        F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
        NumberKeyLock = 144, Scroll = 145, LeftShift = 160, RightShift = 161, LeftControl = 162, RightControl = 163,
        LeftMenu = 164, RightMenu = 165,
    }
}

namespace Zinc.Input
{
    public static class KeyCodes
    {
        /// <summary>The libghostty key for a Win32 virtual key code, which is what Uno reports on every platform.</summary>
        public static Zinc.Ghostty.GhosttyKey ToGhostty(int virtualKey) => KeyMap.FromScanCode(0, false, (Windows.System.VirtualKey)virtualKey);
    }
}
