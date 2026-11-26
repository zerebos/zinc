namespace Zinc.Input;

/// <summary>
/// A key, as a Windows virtual-key code. Key bindings, the config file's key names, and the global hotkey are all
/// written in these, on every platform, so a config means the same keys everywhere. The names and values are those of
/// WinRT's <c>Windows.System.VirtualKey</c>, which the app converts from with a cast, and which Zinc.Core can't use
/// itself because it also builds without the Windows SDK. KeyCodeTests checks the two stay the same.
/// </summary>
public enum KeyCode
{
    None = 0, LeftButton = 1, RightButton = 2, Cancel = 3, MiddleButton = 4, XButton1 = 5, XButton2 = 6,
    Back = 8, Tab = 9, Clear = 12, Enter = 13, Shift = 16, Control = 17, Menu = 18, Pause = 19, CapitalLock = 20,
    Kana = 21, Hangul = 21, ImeOn = 22, Junja = 23, Final = 24, Hanja = 25, Kanji = 25, ImeOff = 26, Escape = 27,
    Convert = 28, NonConvert = 29, Accept = 30, ModeChange = 31, Space = 32, PageUp = 33, PageDown = 34, End = 35,
    Home = 36, Left = 37, Up = 38, Right = 39, Down = 40, Select = 41, Print = 42, Execute = 43, Snapshot = 44,
    Insert = 45, Delete = 46, Help = 47,
    Number0 = 48, Number1, Number2, Number3, Number4, Number5, Number6, Number7, Number8, Number9,
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    LeftWindows = 91, RightWindows = 92, Application = 93, Sleep = 95,
    NumberPad0 = 96, NumberPad1, NumberPad2, NumberPad3, NumberPad4, NumberPad5, NumberPad6, NumberPad7, NumberPad8, NumberPad9,
    Multiply = 106, Add = 107, Separator = 108, Subtract = 109, Decimal = 110, Divide = 111,
    F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    NavigationView = 136, NavigationMenu = 137, NavigationUp = 138, NavigationDown = 139, NavigationLeft = 140,
    NavigationRight = 141, NavigationAccept = 142, NavigationCancel = 143,
    NumberKeyLock = 144, Scroll = 145,
    LeftShift = 160, RightShift = 161, LeftControl = 162, RightControl = 163, LeftMenu = 164, RightMenu = 165,
    GoBack = 166, GoForward = 167, Refresh = 168, Stop = 169, Search = 170, Favorites = 171, GoHome = 172,
    GamepadA = 195, GamepadB = 196, GamepadX = 197, GamepadY = 198, GamepadRightShoulder = 199, GamepadLeftShoulder = 200,
    GamepadLeftTrigger = 201, GamepadRightTrigger = 202, GamepadDPadUp = 203, GamepadDPadDown = 204, GamepadDPadLeft = 205,
    GamepadDPadRight = 206, GamepadMenu = 207, GamepadView = 208, GamepadLeftThumbstickButton = 209,
    GamepadRightThumbstickButton = 210, GamepadLeftThumbstickUp = 211, GamepadLeftThumbstickDown = 212,
    GamepadLeftThumbstickRight = 213, GamepadLeftThumbstickLeft = 214, GamepadRightThumbstickUp = 215,
    GamepadRightThumbstickDown = 216, GamepadRightThumbstickRight = 217, GamepadRightThumbstickLeft = 218,
}
