using System.Runtime.InteropServices;
using Phantom.Ghostty;

namespace Phantom.Input;

/// <summary>Win32 keyboard state and layout translation helpers.</summary>
public static unsafe partial class Keyboard
{
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkCapital = 0x14, VkNumLock = 0x90;
    private const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLMenu = 0xA4, VkRMenu = 0xA5;
    private const int VkLWin = 0x5B, VkRWin = 0x5C;

    /// <summary>A snapshot of the thread's keyboard state (GetKeyboardState).</summary>
    public struct State
    {
        private fixed byte _keys[256];

        public static State Capture()
        {
            var s = new State();
            GetKeyboardState(s._keys);
            return s;
        }

        public bool IsDown(int vk) => (_keys[vk] & 0x80) != 0;

        public bool IsToggled(int vk) => (_keys[vk] & 0x01) != 0;

        public void Release(int vk) => _keys[vk] = (byte)(_keys[vk] & ~0x80);

        public bool Shift => IsDown(VkShift);
        public bool Ctrl => IsDown(VkControl);
        public bool Alt => IsDown(VkMenu);
        public bool Super => IsDown(VkLWin) || IsDown(VkRWin);

        public GhosttyMods Mods
        {
            get
            {
                var mods = GhosttyMods.None;
                if (Shift) mods |= GhosttyMods.Shift;
                if (Ctrl) mods |= GhosttyMods.Ctrl;
                if (Alt) mods |= GhosttyMods.Alt;
                if (Super) mods |= GhosttyMods.Super;
                if (IsToggled(VkCapital)) mods |= GhosttyMods.CapsLock;
                if (IsToggled(VkNumLock)) mods |= GhosttyMods.NumLock;
                if (IsDown(VkRShift) && !IsDown(VkLShift)) mods |= GhosttyMods.ShiftSide;
                if (IsDown(VkRControl) && !IsDown(VkLControl)) mods |= GhosttyMods.CtrlSide;
                if (IsDown(VkRMenu) && !IsDown(VkLMenu)) mods |= GhosttyMods.AltSide;
                if (IsDown(VkRWin) && !IsDown(VkLWin)) mods |= GhosttyMods.SuperSide;
                return mods;
            }
        }

        public State WithoutCtrlAlt()
        {
            var s = this;
            s.Release(VkControl); s.Release(VkLControl); s.Release(VkRControl);
            s.Release(VkMenu); s.Release(VkLMenu); s.Release(VkRMenu);
            return s;
        }

        public State WithoutShift()
        {
            var s = this;
            s.Release(VkShift); s.Release(VkLShift); s.Release(VkRShift);
            s._keys[VkCapital] = 0;
            return s;
        }

        /// <summary>
        /// Translates a key through the active layout without disturbing
        /// dead-key state. Returns null for dead keys, "" for keys without text.
        /// </summary>
        public string? Translate(uint vk, uint scanCode)
        {
            var buf = stackalloc char[8];
            fixed (byte* keys = _keys)
            {
                // Flag 0x4: don't change keyboard state (Windows 10 1607+),
                // so probing a dead key doesn't consume it.
                var n = ToUnicodeEx(vk, scanCode, keys, buf, 8, 0x4, GetKeyboardLayout(0));
                return n switch
                {
                    < 0 => null,
                    0 => "",
                    _ => new string(buf, 0, n),
                };
            }
        }
    }

    public static bool IsModifierKey(int vk) =>
        vk is VkShift or VkControl or VkMenu or VkCapital or VkNumLock or 0x91 /* scroll lock */
            or >= VkLShift and <= VkRMenu or VkLWin or VkRWin;

    public static bool IsPrintable(string? s) => !string.IsNullOrEmpty(s) && !char.IsControl(s[0]);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetKeyboardState(byte* state);

    [LibraryImport("user32.dll")]
    private static partial int ToUnicodeEx(uint vk, uint scanCode, byte* state, char* buf, int bufLen, uint flags, nint hkl);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint threadId);
}
