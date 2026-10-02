using Zinc.Ghostty;
using Windows.System;

namespace Zinc.Input;

/// <summary>
/// Maps Windows keys to physical <see cref="GhosttyKey"/> codes. Ghostty keys
/// are physical (W3C "code" values), so scan codes are the primary source and
/// virtual keys are only a fallback for synthesized input without a scan code.
/// </summary>
public static class KeyMap
{
    private static readonly GhosttyKey[] Normal = new GhosttyKey[0x80];
    private static readonly GhosttyKey[] Extended = new GhosttyKey[0x80];

    static KeyMap()
    {
        void N(int scan, GhosttyKey key) => Normal[scan] = key;
        void E(int scan, GhosttyKey key) => Extended[scan] = key;

        N(0x01, GhosttyKey.Escape);
        GhosttyKey[] digits =
        [
            GhosttyKey.Digit1, GhosttyKey.Digit2, GhosttyKey.Digit3, GhosttyKey.Digit4, GhosttyKey.Digit5,
            GhosttyKey.Digit6, GhosttyKey.Digit7, GhosttyKey.Digit8, GhosttyKey.Digit9, GhosttyKey.Digit0,
        ];
        for (var i = 0; i < digits.Length; i++)
        {
            N(0x02 + i, digits[i]);
        }

        N(0x0C, GhosttyKey.Minus);
        N(0x0D, GhosttyKey.Equal);
        N(0x0E, GhosttyKey.Backspace);
        N(0x0F, GhosttyKey.Tab);
        Row(0x10, "QWERTYUIOP");
        N(0x1A, GhosttyKey.BracketLeft);
        N(0x1B, GhosttyKey.BracketRight);
        N(0x1C, GhosttyKey.Enter);
        E(0x1C, GhosttyKey.NumpadEnter);
        N(0x1D, GhosttyKey.ControlLeft);
        E(0x1D, GhosttyKey.ControlRight);
        Row(0x1E, "ASDFGHJKL");
        N(0x27, GhosttyKey.Semicolon);
        N(0x28, GhosttyKey.Quote);
        N(0x29, GhosttyKey.Backquote);
        N(0x2A, GhosttyKey.ShiftLeft);
        N(0x2B, GhosttyKey.Backslash);
        Row(0x2C, "ZXCVBNM");
        N(0x33, GhosttyKey.Comma);
        N(0x34, GhosttyKey.Period);
        N(0x35, GhosttyKey.Slash);
        E(0x35, GhosttyKey.NumpadDivide);
        N(0x36, GhosttyKey.ShiftRight);
        N(0x37, GhosttyKey.NumpadMultiply);
        E(0x37, GhosttyKey.PrintScreen);
        N(0x38, GhosttyKey.AltLeft);
        E(0x38, GhosttyKey.AltRight);
        N(0x39, GhosttyKey.Space);
        N(0x3A, GhosttyKey.CapsLock);
        GhosttyKey[] f1To10 =
        [
            GhosttyKey.F1, GhosttyKey.F2, GhosttyKey.F3, GhosttyKey.F4, GhosttyKey.F5,
            GhosttyKey.F6, GhosttyKey.F7, GhosttyKey.F8, GhosttyKey.F9, GhosttyKey.F10,
        ];
        for (var i = 0; i < f1To10.Length; i++)
        {
            N(0x3B + i, f1To10[i]);
        }

        N(0x45, GhosttyKey.NumLock);
        N(0x46, GhosttyKey.ScrollLock);
        N(0x47, GhosttyKey.Numpad7);
        E(0x47, GhosttyKey.Home);
        N(0x48, GhosttyKey.Numpad8);
        E(0x48, GhosttyKey.ArrowUp);
        N(0x49, GhosttyKey.Numpad9);
        E(0x49, GhosttyKey.PageUp);
        N(0x4A, GhosttyKey.NumpadSubtract);
        N(0x4B, GhosttyKey.Numpad4);
        E(0x4B, GhosttyKey.ArrowLeft);
        N(0x4C, GhosttyKey.Numpad5);
        N(0x4D, GhosttyKey.Numpad6);
        E(0x4D, GhosttyKey.ArrowRight);
        N(0x4E, GhosttyKey.NumpadAdd);
        N(0x4F, GhosttyKey.Numpad1);
        E(0x4F, GhosttyKey.End);
        N(0x50, GhosttyKey.Numpad2);
        E(0x50, GhosttyKey.ArrowDown);
        N(0x51, GhosttyKey.Numpad3);
        E(0x51, GhosttyKey.PageDown);
        N(0x52, GhosttyKey.Numpad0);
        E(0x52, GhosttyKey.Insert);
        N(0x53, GhosttyKey.NumpadDecimal);
        E(0x53, GhosttyKey.Delete);
        N(0x56, GhosttyKey.IntlBackslash);
        N(0x57, GhosttyKey.F11);
        N(0x58, GhosttyKey.F12);
        E(0x5B, GhosttyKey.MetaLeft);
        E(0x5C, GhosttyKey.MetaRight);
        E(0x5D, GhosttyKey.ContextMenu);
        GhosttyKey[] f13To23 =
        [
            GhosttyKey.F13, GhosttyKey.F14, GhosttyKey.F15, GhosttyKey.F16, GhosttyKey.F17, GhosttyKey.F18,
            GhosttyKey.F19, GhosttyKey.F20, GhosttyKey.F21, GhosttyKey.F22, GhosttyKey.F23,
        ];
        for (var i = 0; i < f13To23.Length; i++)
        {
            N(0x64 + i, f13To23[i]);
        }

        N(0x73, GhosttyKey.IntlRo);
        N(0x76, GhosttyKey.F24);
        N(0x7D, GhosttyKey.IntlYen);

        static void Row(int start, string letters)
        {
            for (var i = 0; i < letters.Length; i++)
            {
                Normal[start + i] = GhosttyKey.A + (letters[i] - 'A');
            }
        }
    }

    public static GhosttyKey FromScanCode(uint scanCode, bool extended, VirtualKey vk)
    {
        if (scanCode is > 0 and < 0x80)
        {
            var key = (extended ? Extended : Normal)[scanCode];
            if (key != GhosttyKey.Unidentified)
            {
                return key;
            }
        }

        return FromVirtualKey(vk);
    }

    private static GhosttyKey FromVirtualKey(VirtualKey vk) => vk switch
    {
        >= VirtualKey.A and <= VirtualKey.Z => GhosttyKey.A + (vk - VirtualKey.A),
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => GhosttyKey.Digit0 + (vk - VirtualKey.Number0),
        >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => GhosttyKey.Numpad0 + (vk - VirtualKey.NumberPad0),
        >= VirtualKey.F1 and <= VirtualKey.F24 => GhosttyKey.F1 + (vk - VirtualKey.F1),
        VirtualKey.Enter => GhosttyKey.Enter,
        VirtualKey.Tab => GhosttyKey.Tab,
        VirtualKey.Back => GhosttyKey.Backspace,
        VirtualKey.Escape => GhosttyKey.Escape,
        VirtualKey.Space => GhosttyKey.Space,
        VirtualKey.Left => GhosttyKey.ArrowLeft,
        VirtualKey.Right => GhosttyKey.ArrowRight,
        VirtualKey.Up => GhosttyKey.ArrowUp,
        VirtualKey.Down => GhosttyKey.ArrowDown,
        VirtualKey.Home => GhosttyKey.Home,
        VirtualKey.End => GhosttyKey.End,
        VirtualKey.PageUp => GhosttyKey.PageUp,
        VirtualKey.PageDown => GhosttyKey.PageDown,
        VirtualKey.Insert => GhosttyKey.Insert,
        VirtualKey.Delete => GhosttyKey.Delete,
        _ => GhosttyKey.Unidentified,
    };
}
