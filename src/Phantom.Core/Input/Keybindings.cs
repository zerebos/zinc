using Phantom.Panes;
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

    // Window actions, handled by whoever hosts the pane.
    NewTab,

    /// <summary>New tab with profile number <see cref="ActionInvocation.Index"/> (0-based).</summary>
    NewTabFromProfile,
    DuplicateTab,
    OpenNewTabMenu,

    /// <summary>Close the focused pane (and its tab, if it was the last one).</summary>
    ClosePane,
    NextTab,
    PreviousTab,

    /// <summary>Go to tab <see cref="ActionInvocation.Index"/> (0-based; -1 = last).</summary>
    SwitchToTab,
    NewWindow,

    /// <summary>New pane with the default profile on side <see cref="ActionInvocation.Direction"/>.</summary>
    SplitPane,

    /// <summary>Duplicate the focused pane, split along its longer side.</summary>
    DuplicatePane,
    MoveFocus,
    ResizePane,
    OpenCommandPalette,
}

/// <summary>An action plus its argument (a tab/profile index or a pane direction), as bound to a key.</summary>
public readonly record struct ActionInvocation(TerminalAction Action, int Index = 0, PaneDirection Direction = default)
{
    public static implicit operator ActionInvocation(TerminalAction action) => new(action);
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

    /// <summary>Display form, e.g. "Ctrl+Shift+T" or "Alt+Shift+Plus".</summary>
    public override string ToString()
    {
        var name = Key switch
        {
            OemPlus => "Plus",
            OemMinus => "Minus",
            VirtualKey.Add => "Numpad Plus",
            VirtualKey.Subtract => "Numpad Minus",
            VirtualKey.PageUp => "PgUp",
            VirtualKey.PageDown => "PgDn",
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)('0' + (Key - VirtualKey.Number0))).ToString(),
            >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => $"Numpad {Key - VirtualKey.NumberPad0}",
            _ => Key.ToString(),
        };
        return (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + name;
    }

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
    private readonly Dictionary<KeyChord, ActionInvocation> _map = new();

    // Binding order, for display (Dictionary enumeration order isn't guaranteed).
    private readonly List<KeyChord> _order = [];

    public static Keybindings Default { get; } = CreateDefault();

    public void Bind(string chord, ActionInvocation action)
    {
        var parsed = KeyChord.Parse(chord);
        if (!_map.ContainsKey(parsed))
        {
            _order.Add(parsed);
        }

        _map[parsed] = action;
    }

    public bool TryGet(KeyChord chord, out ActionInvocation action) => _map.TryGetValue(chord, out action);

    /// <summary>The chord to show for an action, if it has one: the first bound for it.</summary>
    public KeyChord? ChordFor(ActionInvocation action) =>
        _order.Where(c => _map[c] == action).Select(c => (KeyChord?)c).FirstOrDefault();

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

        b.Bind("ctrl+shift+t", TerminalAction.NewTab);
        b.Bind("ctrl+shift+d", TerminalAction.DuplicateTab);
        b.Bind("ctrl+shift+space", TerminalAction.OpenNewTabMenu);
        b.Bind("ctrl+shift+w", TerminalAction.ClosePane);
        b.Bind("ctrl+tab", TerminalAction.NextTab);
        b.Bind("ctrl+shift+tab", TerminalAction.PreviousTab);
        b.Bind("ctrl+shift+n", TerminalAction.NewWindow);
        b.Bind("ctrl+shift+p", TerminalAction.OpenCommandPalette);
        b.Bind("alt+shift+plus", new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Right));
        b.Bind("alt+shift+minus", new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Down));
        b.Bind("alt+shift+d", TerminalAction.DuplicatePane);
        foreach (var (name, direction) in new[]
                 {
                     ("left", PaneDirection.Left), ("right", PaneDirection.Right),
                     ("up", PaneDirection.Up), ("down", PaneDirection.Down),
                 })
        {
            b.Bind($"alt+{name}", new ActionInvocation(TerminalAction.MoveFocus, Direction: direction));
            b.Bind($"alt+shift+{name}", new ActionInvocation(TerminalAction.ResizePane, Direction: direction));
        }
        for (var i = 1; i <= 9; i++)
        {
            b.Bind($"ctrl+shift+{i}", new ActionInvocation(TerminalAction.NewTabFromProfile, i - 1));
            // Windows Terminal: ctrl+alt+1..8 pick a tab, ctrl+alt+9 the last one.
            b.Bind($"ctrl+alt+{i}", new ActionInvocation(TerminalAction.SwitchToTab, i == 9 ? -1 : i - 1));
        }

        return b;
    }
}
