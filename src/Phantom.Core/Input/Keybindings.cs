using Windows.System;

namespace Phantom.Input;

public enum TerminalAction
{
    Copy,

    /// <summary>Copy when there is a selection; otherwise the key goes to the app (Ctrl+C).</summary>
    CopyIfSelection,
    Paste,
    FontSizeIncrease,
    FontSizeDecrease,
    FontSizeReset,
    ScrollPageUp,
    ScrollPageDown,
    ScrollToTop,
    ScrollToBottom,
}

/// <summary>A key plus exact modifier state, parsed from Windows Terminal-style names ("ctrl+shift+c").</summary>
public readonly record struct KeyChord(VirtualKey Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    // VK_OEM_PLUS / VK_OEM_MINUS: the '=' and '-' keys on US layouts, which Windows
    // Terminal calls "plus" and "minus".
    private const VirtualKey OemPlus = (VirtualKey)0xBB, OemMinus = (VirtualKey)0xBD;

    private static readonly Dictionary<string, VirtualKey> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["plus"] = OemPlus, ["minus"] = OemMinus,
        ["numpad_plus"] = VirtualKey.Add, ["numpad_minus"] = VirtualKey.Subtract,
        ["tab"] = VirtualKey.Tab, ["enter"] = VirtualKey.Enter, ["space"] = VirtualKey.Space,
        ["esc"] = VirtualKey.Escape, ["escape"] = VirtualKey.Escape, ["backspace"] = VirtualKey.Back,
        ["insert"] = VirtualKey.Insert, ["delete"] = VirtualKey.Delete,
        ["home"] = VirtualKey.Home, ["end"] = VirtualKey.End,
        ["pgup"] = VirtualKey.PageUp, ["pageup"] = VirtualKey.PageUp,
        ["pgdn"] = VirtualKey.PageDown, ["pagedown"] = VirtualKey.PageDown,
        ["up"] = VirtualKey.Up, ["down"] = VirtualKey.Down, ["left"] = VirtualKey.Left, ["right"] = VirtualKey.Right,
    };

    public static KeyChord Parse(string text)
    {
        bool ctrl = false, alt = false, shift = false;
        VirtualKey? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
            }

            if (key != null)
            {
                throw new FormatException($"'{text}': more than one key");
            }

            key = ParseKey(part) ?? throw new FormatException($"'{text}': unknown key '{part}'");
        }

        return new KeyChord(key ?? throw new FormatException($"'{text}': no key"), ctrl, alt, shift);
    }

    private static VirtualKey? ParseKey(string name)
    {
        if (Names.TryGetValue(name, out var named))
        {
            return named;
        }

        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            return VirtualKey.A + (char.ToUpperInvariant(name[0]) - 'A');
        }

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return VirtualKey.Number0 + (name[0] - '0');
        }

        if (name.StartsWith("numpad", StringComparison.OrdinalIgnoreCase) && name.Length == 7 && char.IsAsciiDigit(name[6]))
        {
            return VirtualKey.NumberPad0 + (name[6] - '0');
        }

        if (name.Length is 2 or 3 && (name[0] is 'f' or 'F') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24)
        {
            return VirtualKey.F1 + (f - 1);
        }

        return null;
    }
}

/// <summary>Maps key chords to actions. Defaults follow Windows Terminal.</summary>
public sealed class Keybindings
{
    private readonly Dictionary<KeyChord, TerminalAction> _map = new();

    public static Keybindings Default { get; } = CreateDefault();

    public void Bind(string chord, TerminalAction action) => _map[KeyChord.Parse(chord)] = action;

    public bool TryGet(KeyChord chord, out TerminalAction action) => _map.TryGetValue(chord, out action);

    private static Keybindings CreateDefault()
    {
        var b = new Keybindings();
        b.Bind("ctrl+shift+c", TerminalAction.Copy);
        b.Bind("ctrl+c", TerminalAction.CopyIfSelection);
        b.Bind("ctrl+v", TerminalAction.Paste);
        b.Bind("ctrl+shift+v", TerminalAction.Paste);
        b.Bind("shift+insert", TerminalAction.Paste);
        foreach (var shift in new[] { "", "shift+" })
        {
            b.Bind($"ctrl+{shift}plus", TerminalAction.FontSizeIncrease);
            b.Bind($"ctrl+{shift}numpad_plus", TerminalAction.FontSizeIncrease);
            b.Bind($"ctrl+{shift}minus", TerminalAction.FontSizeDecrease);
            b.Bind($"ctrl+{shift}numpad_minus", TerminalAction.FontSizeDecrease);
            b.Bind($"ctrl+{shift}0", TerminalAction.FontSizeReset);
            b.Bind($"ctrl+{shift}numpad0", TerminalAction.FontSizeReset);
        }

        b.Bind("shift+pgup", TerminalAction.ScrollPageUp);
        b.Bind("shift+pgdn", TerminalAction.ScrollPageDown);
        b.Bind("ctrl+shift+home", TerminalAction.ScrollToTop);
        b.Bind("ctrl+shift+end", TerminalAction.ScrollToBottom);
        return b;
    }
}
