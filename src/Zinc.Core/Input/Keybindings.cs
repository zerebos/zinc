using Zinc.Panes;
using Windows.System;

namespace Zinc.Input;

public enum TerminalAction
{
    Copy,

    /// <summary>Copy when there is a selection, and otherwise let the key go to the app (Ctrl+C).</summary>
    CopyIfSelection,

    /// <summary>Copy the selection with its colors and styles (HTML next to the text).</summary>
    CopyFormatted,
    Paste,
    ZoomIn,
    ZoomOut,
    ZoomReset,
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

    /// <summary>Close the focused tab (close-tab). Its pane goes with its last tab.</summary>
    CloseTab,

    /// <summary>Close the focused pane and every tab in it (close-pane), asking once.</summary>
    CloseGroup,
    NextTab,
    PreviousTab,

    /// <summary>Go to tab <see cref="ActionInvocation.Index"/> (0-based, or -1 for the last).</summary>
    SwitchToTab,
    NewWindow,
    ReopenWorkspace,
    ReopenWindow,

    /// <summary>New pane with the default profile on side <see cref="ActionInvocation.Direction"/>.</summary>
    SplitPane,

    /// <summary>Duplicate the focused pane, split along its longer side.</summary>
    DuplicatePane,
    MoveFocus,
    ResizePane,

    /// <summary>Fill the workspace with the focused pane, hiding the others. Trigger again to show them.</summary>
    MaximizePane,
    OpenCommandPalette,

    /// <summary>Open the pane's find box.</summary>
    Find,

    /// <summary>New workspace (title-bar tab) with the default profile.</summary>
    NewWorkspace,

    /// <summary>Read config.toml again.</summary>
    ReloadConfig,

    /// <summary>Open the settings window (or bring it forward).</summary>
    OpenSettings,

    /// <summary>Close every window and exit.</summary>
    Quit,
    QuitFresh,

    /// <summary>
    /// Switch the workspace list's mode. <see cref="ActionInvocation.Index"/> -1 cycles full → rail →
    /// auto-hide, and any other index is a <see cref="SidebarMode"/> to toggle with full.
    /// </summary>
    ToggleSidebar,

    /// <summary>Title bar shown ↔ auto-hidden.</summary>
    ToggleTitleBar,

    /// <summary>Auto-hide both the title bar and the workspace list. Running it again brings them back.</summary>
    FocusMode,

    /// <summary>Scroll to the previous (<see cref="ActionInvocation.Index"/> -1) or next (+1) prompt.</summary>
    JumpToPrompt,

    /// <summary>Select the output of the command in view (or the last one).</summary>
    SelectOutput,

    /// <summary>Copy the output of the command in view (or the last one).</summary>
    CopyOutput,

    /// <summary>The command palette searching this window's tabs (its "@" mode).</summary>
    TabSwitcher,

    /// <summary>
    /// Go to the next tab with a dot. Programs that exited with an error come first, then failed commands, the bell,
    /// programs' notifications and finished commands.
    /// </summary>
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
    private const VirtualKey OemPlus = (VirtualKey)0xBB, OemMinus = (VirtualKey)0xBD, OemComma = (VirtualKey)0xBC;

    // VK_OEM_3: the `/~ key on US layouts ("grave" in Ghostty, "`" in Windows Terminal).
    private const VirtualKey OemGrave = (VirtualKey)0xC0;

    // For each key, the first name here that starts with a letter is the one ToConfig writes (and the reference
    // lists), and the others are aliases. So reordering an entry changes what config.toml and the reference say.
    private static readonly Dictionary<string, VirtualKey> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["plus"] = OemPlus, ["minus"] = OemMinus, ["comma"] = OemComma, [","] = OemComma, ["grave"] = OemGrave, ["grave_accent"] = OemGrave, ["`"] = OemGrave,
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
            OemComma => ",",
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

    /// <summary>
    /// The form config.toml takes ("ctrl+shift+t", "alt+shift+plus"), which <see cref="Parse"/>
    /// reads back. Null for a key it has no name for.
    /// </summary>
    public string? ToConfig()
    {
        var key = Key;
        var name = key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => ((char)('a' + (Key - VirtualKey.A))).ToString(),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)('0' + (Key - VirtualKey.Number0))).ToString(),
            >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => $"numpad{Key - VirtualKey.NumberPad0}",
            >= VirtualKey.F1 and <= VirtualKey.F24 => $"f{Key - VirtualKey.F1 + 1}",
            _ => Names.FirstOrDefault(n => n.Value == key && char.IsLetter(n.Key[0])).Key,
        };
        return name == null ? null : (Ctrl ? CtrlName + Separator : "") + (Alt ? AltName + Separator : "") + (Shift ? ShiftName + Separator : "") + name;
    }

    /// <summary>What goes between a chord's modifiers and its key ("ctrl+shift+t").</summary>
    public const char Separator = '+';

    private const string CtrlName = "ctrl", AltName = "alt", ShiftName = "shift";

    /// <summary>The modifier names <see cref="Parse"/> reads, in the order <see cref="ToConfig"/> writes them.</summary>
    internal static IReadOnlyList<string> ModifierNames { get; } = [CtrlName, AltName, ShiftName];

    /// <summary>
    /// Every key name <see cref="Parse"/> reads, for the reference export: the name <see cref="ToConfig"/> writes, the
    /// other spellings it accepts, and the Windows virtual-key code it stands for.
    /// </summary>
    internal static IEnumerable<(string Name, IReadOnlyList<string> Aliases, int VirtualKey)> KeyNames
    {
        get
        {
            var keys = Enumerable.Range(0, 26).Select(i => VirtualKey.A + i)
                .Concat(Enumerable.Range(0, 10).Select(i => VirtualKey.Number0 + i))
                .Concat(Enumerable.Range(0, 10).Select(i => VirtualKey.NumberPad0 + i))
                .Concat(Enumerable.Range(0, 24).Select(i => VirtualKey.F1 + i))
                .Concat(Names.Values.Distinct());
            foreach (var key in keys)
            {
                var name = new KeyChord(key).ToConfig() ?? throw new InvalidOperationException($"{key} has no config name");
                IReadOnlyList<string> aliases = [.. Names.Where(n => n.Value == key && n.Key != name).Select(n => n.Key)];
                yield return (name, aliases, (int)key);
            }
        }
    }

    public static KeyChord Parse(string text)
    {
        bool ctrl = false, alt = false, shift = false;
        VirtualKey? key = null;
        foreach (var part in text.Split(Separator, StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case CtrlName: ctrl = true; continue;
                case AltName: alt = true; continue;
                case ShiftName: shift = true; continue;
            }

            if (key != null)
            {
                throw new FormatException($"\"{text}\" has more than one key");
            }

            key = ParseKey(part) ?? throw new FormatException($"\"{text}\" has an unknown key, \"{part}\"");
        }

        return new KeyChord(key ?? throw new FormatException($"\"{text}\" has no key"), ctrl, alt, shift);
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

/// <summary>
/// Chords pressed one after another, like a tmux prefix: <c>"ctrl+a>c"</c> (spaces around the
/// <c>&gt;</c> are fine). The first chord is the leader, which starts the sequence instead of doing
/// anything itself.
/// </summary>
public sealed class KeySequence : IEquatable<KeySequence>
{
    public KeySequence(IReadOnlyList<KeyChord> keys)
    {
        if (keys.Count < 2)
        {
            throw new ArgumentException("a sequence has at least two keys", nameof(keys));
        }

        Keys = keys.ToArray();
    }

    public IReadOnlyList<KeyChord> Keys { get; }

    /// <summary>Whether <paramref name="text"/> is written as a sequence (has a <c>&gt;</c>).</summary>
    public static bool IsSequence(string text) => text.Contains(Separator);

    /// <summary>What goes between a sequence's chords. Spaces around it are allowed.</summary>
    public const char Separator = '>';

    public static KeySequence Parse(string text)
    {
        var parts = text.Split(Separator, StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(p => p.Length == 0))
        {
            throw new FormatException($"\"{text}\" is missing a key around >");
        }

        return new KeySequence(parts.Select(KeyChord.Parse).ToArray());
    }

    /// <summary>Whether the sequence begins with <paramref name="pressed"/> (all of it, or more).</summary>
    public bool StartsWith(IReadOnlyList<KeyChord> pressed) =>
        pressed.Count <= Keys.Count && pressed.Select((k, i) => Keys[i] == k).All(x => x);

    /// <summary>Display form: "Ctrl+A > C", as config.toml writes it.</summary>
    public override string ToString() => string.Join(" > ", Keys);

    public bool Equals(KeySequence? other) => other != null && Keys.SequenceEqual(other.Keys);

    public override bool Equals(object? obj) => Equals(obj as KeySequence);

    public override int GetHashCode() => Keys.Aggregate(0, (h, k) => HashCode.Combine(h, k));
}

/// <summary>What a key press means to a <see cref="KeySequencer"/>.</summary>
public enum KeyStep
{
    /// <summary>No sequence is going and this key starts none, so handle it as usual.</summary>
    NotMine,

    /// <summary>A sequence started or went on, so wait for the next key (show what can come next).</summary>
    Waiting,

    /// <summary>A sequence completed, so run its action.</summary>
    Ran,

    /// <summary>Escape, or a key no sequence continues with. The sequence ends and the key is dropped.</summary>
    Cancelled,

    /// <summary>
    /// The leader pressed twice (and not bound that way). The sequence ends and the key goes to the terminal.
    /// </summary>
    PassThrough,
}

/// <summary>
/// Follows the keys of a sequence as they're pressed (each pane has its own sequencer). A leader starts a sequence,
/// and the following keys continue or complete it. There's no timeout, so it waits until the next key.
/// </summary>
public sealed class KeySequencer(Keybindings bindings)
{
    private readonly List<KeyChord> _pressed = [];

    /// <summary>The keys pressed so far (empty when no sequence is going).</summary>
    public IReadOnlyList<KeyChord> Pressed => _pressed;

    public bool Active => _pressed.Count > 0;

    public void Cancel() => _pressed.Clear();

    public KeyStep Press(KeyChord chord, out ActionInvocation action)
    {
        action = default;
        if (_pressed.Count == 0)
        {
            if (!bindings.StartsSequence(chord))
            {
                return KeyStep.NotMine;
            }

            _pressed.Add(chord);
            return KeyStep.Waiting;
        }

        List<KeyChord> keys = [.. _pressed, chord];
        if (bindings.TryGetSequence(keys, out action))
        {
            _pressed.Clear();
            return KeyStep.Ran;
        }

        if (bindings.SequencesStartingWith(keys).Any())
        {
            _pressed.Add(chord);
            return KeyStep.Waiting;
        }

        var leaderTwice = keys.Count == 2 && chord == keys[0];
        _pressed.Clear();
        return leaderTwice ? KeyStep.PassThrough : KeyStep.Cancelled;
    }
}

/// <summary>Maps key chords (and <see cref="KeySequence"/>s) to actions. Defaults follow Windows Terminal.</summary>
public sealed class Keybindings
{
    private readonly Dictionary<KeyChord, ActionInvocation> _map = new();

    // Binding order, for display (Dictionary enumeration order isn't guaranteed).
    private readonly List<KeyChord> _order = [];

    // Key sequences ("ctrl+a>c"), in binding order. Few enough to search.
    private readonly List<(KeySequence Keys, ActionInvocation Action)> _sequences = [];

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

    public void Bind(KeySequence keys, ActionInvocation action)
    {
        Unbind(keys);
        _sequences.Add((keys, action));
    }

    public void Unbind(KeySequence keys) => _sequences.RemoveAll(s => s.Keys.Equals(keys));

    /// <summary>Every single-chord binding, in binding order.</summary>
    public IEnumerable<(KeyChord Chord, ActionInvocation Action)> All => _order.Select(c => (c, _map[c]));

    /// <summary>Every key sequence, in binding order.</summary>
    public IReadOnlyList<(KeySequence Keys, ActionInvocation Action)> Sequences => _sequences;

    /// <summary>Whether <paramref name="chord"/> is a leader, which starts a sequence (and does nothing else).</summary>
    public bool StartsSequence(KeyChord chord) => _sequences.Any(s => s.Keys.Keys[0] == chord);

    /// <summary>The sequence that is exactly <paramref name="keys"/>.</summary>
    public bool TryGetSequence(IReadOnlyList<KeyChord> keys, out ActionInvocation action)
    {
        foreach (var (seq, a) in _sequences)
        {
            if (seq.Keys.Count == keys.Count && seq.StartsWith(keys))
            {
                action = a;
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>The sequences that go on after <paramref name="pressed"/> (longer than it).</summary>
    public IEnumerable<(KeySequence Keys, ActionInvocation Action)> SequencesStartingWith(IReadOnlyList<KeyChord> pressed) =>
        _sequences.Where(s => s.Keys.Keys.Count > pressed.Count && s.Keys.StartsWith(pressed));

    /// <summary>
    /// What can follow <paramref name="pressed"/>: each next key, with the action it runs, or null
    /// when more keys come after it. For the hint shown while a sequence waits.
    /// </summary>
    public IReadOnlyList<(KeyChord Next, ActionInvocation? Action)> Continuations(IReadOnlyList<KeyChord> pressed) =>
        SequencesStartingWith(pressed)
            .GroupBy(s => s.Keys.Keys[pressed.Count])
            .Select(g => (g.Key, g.FirstOrDefault(s => s.Keys.Keys.Count == pressed.Count + 1) is { Keys: not null } done
                ? (ActionInvocation?)done.Action
                : null))
            .ToList();

    /// <summary>
    /// The shortcut to show for an action, which is its first chord, else its first sequence ("Ctrl+A > C").
    /// </summary>
    public string? ShortcutFor(ActionInvocation action) =>
        ChordFor(action)?.ToString() ?? _sequences.Where(s => s.Action == action).Select(s => s.Keys.ToString()).FirstOrDefault();

    public Keybindings Clone()
    {
        var copy = new Keybindings();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>
    /// Takes <paramref name="other"/>'s bindings in place (a config reload). Every window and
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

        _sequences.Clear();
        _sequences.AddRange(other._sequences);
    }

    /// <summary>
    /// The defaults with a <c>[keys]</c> table on top. Each entry binds a chord to an action
    /// string (<see cref="Actions.Parse"/>), or unbinds it with "none". Problems go to
    /// <paramref name="warnings"/> prefixed with the entry's location, and the entry is skipped.
    /// </summary>
    public static Keybindings Configure(IEnumerable<(string Chord, string Action, string Where)> entries, List<string> warnings)
    {
        var bindings = Default.Clone();
        var seen = new Dictionary<KeyChord, string>();
        var seenSequences = new Dictionary<KeySequence, string>();
        foreach (var (chordText, actionText, where) in entries)
        {
            if (KeySequence.IsSequence(chordText))
            {
                ConfigureSequence(bindings, chordText, actionText, where, seenSequences, warnings);
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

        // A leader only starts sequences, so a chord that is also bound on its own loses that binding.
        foreach (var (keys, _) in bindings.Sequences.DistinctBy(s => s.Keys.Keys[0]).ToList())
        {
            var leader = keys.Keys[0];
            if (bindings.TryGet(leader, out var shadowed))
            {
                warnings.Add($"{seenSequences[keys]}: {leader} starts key sequences now, so it no longer runs \"{Actions.Format(shadowed)}\"");
                bindings.Unbind(leader);
            }
        }

        // A sequence that goes on past a complete one can't be reached (the shorter one runs first).
        foreach (var (keys, _) in bindings.Sequences)
        {
            if (bindings.Sequences.FirstOrDefault(s => s.Keys.Keys.Count < keys.Keys.Count && keys.StartsWith(s.Keys.Keys)) is { Keys: { } shorter } &&
                seenSequences.TryGetValue(keys, out var where))
            {
                warnings.Add($"{where}: {keys} can't be reached, because {shorter} runs first");
            }
        }

        return bindings;
    }

    private static void ConfigureSequence(Keybindings bindings, string text, string actionText, string where,
        Dictionary<KeySequence, string> seen, List<string> warnings)
    {
        KeySequence keys;
        try
        {
            keys = KeySequence.Parse(text);
        }
        catch (FormatException ex)
        {
            warnings.Add($"{where}: {ex.Message}");
            return;
        }

        if (seen.TryGetValue(keys, out var first))
        {
            warnings.Add($"{where}: {keys} is already bound ({first})");
            return;
        }

        seen[keys] = where;
        if (actionText.Trim().Equals(Actions.None, StringComparison.OrdinalIgnoreCase))
        {
            bindings.Unbind(keys);
        }
        else if (Actions.Parse(actionText, out var error) is { } action)
        {
            bindings.Bind(keys, action);
        }
        else
        {
            warnings.Add($"{where}: {error}");
        }
    }

    public bool TryGet(KeyChord chord, out ActionInvocation action) => _map.TryGetValue(chord, out action);

    /// <summary>The chord to show for an action, which is the first one bound to it, or null if it has none.</summary>
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
            b.Bind($"ctrl+{shift}plus", TerminalAction.ZoomIn);
            b.Bind($"ctrl+{shift}numpad_plus", TerminalAction.ZoomIn);
            b.Bind($"ctrl+{shift}minus", TerminalAction.ZoomOut);
            b.Bind($"ctrl+{shift}numpad_minus", TerminalAction.ZoomOut);
            b.Bind($"ctrl+{shift}0", TerminalAction.ZoomReset);
            b.Bind($"ctrl+{shift}numpad0", TerminalAction.ZoomReset);
        }

        b.Bind("shift+pgup", TerminalAction.ScrollPageUp);
        b.Bind("shift+pgdn", TerminalAction.ScrollPageDown);
        b.Bind("ctrl+shift+home", TerminalAction.ScrollToTop);
        b.Bind("ctrl+shift+end", TerminalAction.ScrollToBottom);

        b.Bind("ctrl+shift+t", TerminalAction.NewTab);
        b.Bind("ctrl+alt+t", TerminalAction.NewWorkspace);
        b.Bind("ctrl+comma", TerminalAction.OpenSettings); // Windows Terminal's and VS Code's
        b.Bind("ctrl+shift+d", TerminalAction.DuplicateTab);
        b.Bind("ctrl+shift+space", TerminalAction.OpenNewTabMenu);
        b.Bind("ctrl+shift+w", TerminalAction.CloseTab);
        b.Bind("ctrl+tab", TerminalAction.NextTab);
        b.Bind("ctrl+shift+tab", TerminalAction.PreviousTab);
        b.Bind("ctrl+shift+n", TerminalAction.NewWindow);
        b.Bind("ctrl+shift+p", TerminalAction.OpenCommandPalette);
        b.Bind("ctrl+shift+o", TerminalAction.TabSwitcher);
        b.Bind("ctrl+shift+f", TerminalAction.Find);
        b.Bind("ctrl+shift+b", new ActionInvocation(TerminalAction.ToggleSidebar, -1));
        // Windows Terminal scrolls a line with these. Prompts are the better stops (and without
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
