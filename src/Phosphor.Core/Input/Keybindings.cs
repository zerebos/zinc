using Phosphor.Panes;
using Windows.System;

namespace Phosphor.Input;

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

    /// <summary>Open the pane's find box.</summary>
    Find,

    /// <summary>New workspace (title-bar tab) with the default profile.</summary>
    NewWorkspace,

    /// <summary>Read config.toml again.</summary>
    ReloadConfig,

    /// <summary>Close every window and exit.</summary>
    Quit,

    /// <summary>
    /// Switch the workspace list's mode: <see cref="ActionInvocation.Index"/> -1 cycles full → rail →
    /// auto-hide; otherwise it's a <see cref="SidebarMode"/> to toggle with full.
    /// </summary>
    ToggleSidebar,

    /// <summary>Title bar shown ↔ auto-hidden.</summary>
    ToggleTitleBar,

    /// <summary>Auto-hide both the title bar and the workspace list; again to bring them back.</summary>
    FocusMode,

    /// <summary>Scroll to the previous (<see cref="ActionInvocation.Index"/> -1) or next (+1) prompt.</summary>
    JumpToPrompt,

    /// <summary>Select the output of the command in view (or the last one).</summary>
    SelectOutput,

    /// <summary>Copy the output of the command in view (or the last one).</summary>
    CopyOutput,

    /// <summary>The command palette searching this window's tabs (its "@" mode).</summary>
    TabSwitcher,

    /// <summary>Go to the next tab with a dot (a failed command first, then programs' notifications, then finished ones).</summary>
    NextAttention,
}

/// <summary>
/// An action plus its argument (a tab/profile index, a pane direction, or a profile id), as bound
/// to a key. <see cref="Actions"/> reads and writes it as text ("split right").
/// </summary>
public readonly record struct ActionInvocation(TerminalAction Action, int Index = 0, PaneDirection Direction = default, string? Profile = null)
{
    public static implicit operator ActionInvocation(TerminalAction action) => new(action);
}

/// <summary>A key plus exact modifier state, parsed from Windows Terminal-style names ("ctrl+shift+c").</summary>
public readonly record struct KeyChord(VirtualKey Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    // VK_OEM_PLUS / VK_OEM_MINUS: the '=' and '-' keys on US layouts, which Windows
    // Terminal calls "plus" and "minus".
    private const VirtualKey OemPlus = (VirtualKey)0xBB, OemMinus = (VirtualKey)0xBD;

    // VK_OEM_3: the `/~ key on US layouts ("grave" in Ghostty, "`" in Windows Terminal).
    private const VirtualKey OemGrave = (VirtualKey)0xC0;

    private static readonly Dictionary<string, VirtualKey> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["plus"] = OemPlus, ["minus"] = OemMinus, ["grave"] = OemGrave, ["grave_accent"] = OemGrave, ["`"] = OemGrave,
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
            OemGrave => "`",
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

    public void Bind(string chord, ActionInvocation action) => Bind(KeyChord.Parse(chord), action);

    public void Bind(KeyChord chord, ActionInvocation action)
    {
        if (!_map.ContainsKey(chord))
        {
            _order.Add(chord);
        }

        _map[chord] = action;
    }

    public void Unbind(KeyChord chord)
    {
        if (_map.Remove(chord))
        {
            _order.Remove(chord);
        }
    }

    /// <summary>Every binding, in binding order.</summary>
    public IEnumerable<(KeyChord Chord, ActionInvocation Action)> All => _order.Select(c => (c, _map[c]));

    public Keybindings Clone()
    {
        var copy = new Keybindings();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>
    /// Takes <paramref name="other"/>'s bindings in place (a config reload): every window and
    /// pane holds this same instance, so they all see the change.
    /// </summary>
    public void CopyFrom(Keybindings other)
    {
        _map.Clear();
        _order.Clear();
        foreach (var (chord, action) in other.All)
        {
            Bind(chord, action);
        }
    }

    /// <summary>
    /// The defaults with a <c>[keys]</c> table on top: each entry binds a chord to an action
    /// string (<see cref="Actions.Parse"/>), or unbinds it with "none". Problems go to
    /// <paramref name="warnings"/> prefixed with the entry's location; the entry is skipped.
    /// </summary>
    public static Keybindings Configure(IEnumerable<(string Chord, string Action, string Where)> entries, List<string> warnings)
    {
        var bindings = Default.Clone();
        var seen = new Dictionary<KeyChord, string>();
        foreach (var (chordText, actionText, where) in entries)
        {
            if (chordText.Contains('>'))
            {
                warnings.Add($"{where}: key sequences like '{chordText}' aren't supported yet");
                continue;
            }

            KeyChord chord;
            try
            {
                chord = KeyChord.Parse(chordText);
            }
            catch (FormatException ex)
            {
                warnings.Add($"{where}: {ex.Message}");
                continue;
            }

            // "ctrl+shift+c" and "shift+ctrl+c" are different TOML keys but one chord.
            if (seen.TryGetValue(chord, out var first))
            {
                warnings.Add($"{where}: {chord} is already bound ({first})");
                continue;
            }

            seen[chord] = where;
            if (actionText.Trim().Equals(Actions.None, StringComparison.OrdinalIgnoreCase))
            {
                bindings.Unbind(chord);
            }
            else if (Actions.Parse(actionText, out var error) is { } action)
            {
                bindings.Bind(chord, action);
            }
            else
            {
                warnings.Add($"{where}: {error}");
            }
        }

        return bindings;
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
        b.Bind("ctrl+alt+t", TerminalAction.NewWorkspace);
        b.Bind("ctrl+shift+d", TerminalAction.DuplicateTab);
        b.Bind("ctrl+shift+space", TerminalAction.OpenNewTabMenu);
        b.Bind("ctrl+shift+w", TerminalAction.ClosePane);
        b.Bind("ctrl+tab", TerminalAction.NextTab);
        b.Bind("ctrl+shift+tab", TerminalAction.PreviousTab);
        b.Bind("ctrl+shift+n", TerminalAction.NewWindow);
        b.Bind("ctrl+shift+p", TerminalAction.OpenCommandPalette);
        b.Bind("ctrl+shift+o", TerminalAction.TabSwitcher);
        b.Bind("ctrl+shift+f", TerminalAction.Find);
        b.Bind("ctrl+shift+b", new ActionInvocation(TerminalAction.ToggleSidebar, -1));
        // Windows Terminal scrolls a line with these; prompts are the better stops (and without
        // shell integration they still scroll a line).
        b.Bind("ctrl+shift+up", new ActionInvocation(TerminalAction.JumpToPrompt, -1));
        b.Bind("ctrl+shift+down", new ActionInvocation(TerminalAction.JumpToPrompt, 1));
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
