using Phosphor.Ghostty;
using Phosphor.Input;
using Phosphor.Panes;
using Windows.System;

namespace Phosphor.Tests;

public class KeyDecisionTests
{
    // Shorthand: what the layout produces with all mods / without Ctrl+Alt / unshifted.
    private static KeyPress Key(GhosttyKey key, string? text, string? noCtrlAlt, string? unshifted,
        bool shift = false, bool ctrl = false, bool alt = false, bool repeat = false)
    {
        var mods = GhosttyMods.None;
        if (shift) mods |= GhosttyMods.Shift;
        if (ctrl) mods |= GhosttyMods.Ctrl;
        if (alt) mods |= GhosttyMods.Alt;
        return new KeyPress(key, repeat, shift, ctrl, alt, mods, text, noCtrlAlt, unshifted);
    }

    [Fact]
    public void PlainTextGoesToCharacterPath()
    {
        Assert.Equal(KeyRoute.Character, KeyDecision.Route(Key(GhosttyKey.A, "a", "a", "a"), false, out _));
        Assert.Equal(KeyRoute.Character, KeyDecision.Route(Key(GhosttyKey.A, "A", "A", "a", shift: true), false, out _));
    }

    [Fact]
    public void AltGrTextGoesToCharacterPath()
    {
        // German layout: AltGr+Q = '@', reported by Windows as Ctrl+Alt.
        var altGrQ = Key(GhosttyKey.Q, "@", "q", "q", ctrl: true, alt: true);
        Assert.Equal(KeyRoute.Character, KeyDecision.Route(altGrQ, false, out _));
    }

    [Fact]
    public void DeadKeyGoesToCharacterPath()
    {
        Assert.Equal(KeyRoute.Character, KeyDecision.Route(Key(GhosttyKey.Quote, null, null, null), false, out _));
    }

    [Fact]
    public void CtrlLetterIsEncodedWithUnmodifiedText()
    {
        // ToUnicode gives the control character 0x03 for Ctrl+C.
        Assert.Equal(KeyRoute.Encode, KeyDecision.Route(Key(GhosttyKey.C, "\x03", "c", "c", ctrl: true), false, out var r));
        Assert.Equal(new KeyEncodeRequest(KeyAction.Press, GhosttyKey.C, GhosttyMods.Ctrl, GhosttyMods.None, "c", 'c'), r);
    }

    [Fact]
    public void AltLetterIsEncoded()
    {
        Assert.Equal(KeyRoute.Encode, KeyDecision.Route(Key(GhosttyKey.X, "x", "x", "x", alt: true), false, out var r));
        Assert.Equal("x", r.Text);
        Assert.Equal(GhosttyMods.Alt, r.Mods);
    }

    [Fact]
    public void KeysWithoutTextAreEncoded()
    {
        Assert.Equal(KeyRoute.Encode, KeyDecision.Route(Key(GhosttyKey.ArrowUp, "", "", ""), false, out var r));
        Assert.Null(r.Text);
        Assert.Equal(0u, r.UnshiftedCodepoint);

        // Enter/Tab/Backspace produce control characters, which are never passed as text.
        Assert.Equal(KeyRoute.Encode, KeyDecision.Route(Key(GhosttyKey.Enter, "\r", "\r", "\r"), false, out r));
        Assert.Null(r.Text);
    }

    [Fact]
    public void KittyModeEncodesPlainText()
    {
        Assert.Equal(KeyRoute.Encode, KeyDecision.Route(Key(GhosttyKey.A, "a", "a", "a"), true, out var r));
        Assert.Equal("a", r.Text);
        Assert.Equal((uint)'a', r.UnshiftedCodepoint);
    }

    [Fact]
    public void ShiftIsConsumedWhenItChangesTheText()
    {
        KeyDecision.Route(Key(GhosttyKey.Digit1, "!", "!", "1", shift: true), true, out var r);
        Assert.Equal(GhosttyMods.Shift, r.ConsumedMods);
        Assert.Equal((uint)'1', r.UnshiftedCodepoint);

        // Shift+Space doesn't change the text, so Shift is not consumed.
        KeyDecision.Route(Key(GhosttyKey.Space, " ", " ", " ", shift: true), true, out r);
        Assert.Equal(GhosttyMods.None, r.ConsumedMods);
    }

    [Fact]
    public void RepeatIsReported()
    {
        KeyDecision.Route(Key(GhosttyKey.ArrowDown, "", "", "", repeat: true), false, out var r);
        Assert.Equal(KeyAction.Repeat, r.Action);
    }
}

public class KeybindingTests
{
    [Theory]
    [InlineData("ctrl+shift+c", VirtualKey.C, true, false, true)]
    [InlineData("Ctrl+V", VirtualKey.V, true, false, false)]
    [InlineData("alt+shift+d", VirtualKey.D, false, true, true)]
    [InlineData("shift+pgup", VirtualKey.PageUp, false, false, true)]
    [InlineData("ctrl+plus", (VirtualKey)0xBB, true, false, false)]
    [InlineData("ctrl+numpad_minus", VirtualKey.Subtract, true, false, false)]
    [InlineData("ctrl+shift+1", VirtualKey.Number1, true, false, true)]
    [InlineData("ctrl+numpad0", VirtualKey.NumberPad0, true, false, false)]
    [InlineData("alt+left", VirtualKey.Left, false, true, false)]
    [InlineData("shift+f11", VirtualKey.F11, false, false, true)]
    [InlineData("ctrl+tab", VirtualKey.Tab, true, false, false)]
    public void ParsesChords(string text, VirtualKey key, bool ctrl, bool alt, bool shift)
    {
        Assert.Equal(new KeyChord(key, ctrl, alt, shift), KeyChord.Parse(text));
    }

    [Theory]
    [InlineData("ctrl+shift")]
    [InlineData("ctrl+a+b")]
    [InlineData("ctrl+nosuchkey")]
    [InlineData("f25")]
    public void RejectsBadChords(string text)
    {
        Assert.Throws<FormatException>(() => KeyChord.Parse(text));
    }

    [Theory]
    [InlineData("ctrl+c", TerminalAction.CopyIfSelection)]
    [InlineData("ctrl+shift+c", TerminalAction.Copy)]
    [InlineData("ctrl+v", TerminalAction.Paste)]
    [InlineData("ctrl+shift+v", TerminalAction.Paste)]
    [InlineData("shift+insert", TerminalAction.Paste)]
    [InlineData("ctrl+plus", TerminalAction.FontSizeIncrease)]
    [InlineData("ctrl+shift+plus", TerminalAction.FontSizeIncrease)]
    [InlineData("ctrl+0", TerminalAction.FontSizeReset)]
    [InlineData("shift+pgdn", TerminalAction.ScrollPageDown)]
    [InlineData("ctrl+shift+end", TerminalAction.ScrollToBottom)]
    public void DefaultsFollowWindowsTerminal(string chord, TerminalAction expected)
    {
        Assert.True(Keybindings.Default.TryGet(KeyChord.Parse(chord), out var action));
        Assert.Equal(expected, action.Action);
    }

    [Theory]
    [InlineData("ctrl+shift+t", TerminalAction.NewTab, 0)]
    [InlineData("ctrl+shift+1", TerminalAction.NewTabFromProfile, 0)]
    [InlineData("ctrl+shift+9", TerminalAction.NewTabFromProfile, 8)]
    [InlineData("ctrl+alt+1", TerminalAction.SwitchToTab, 0)]
    [InlineData("ctrl+alt+8", TerminalAction.SwitchToTab, 7)]
    [InlineData("ctrl+alt+9", TerminalAction.SwitchToTab, -1)]
    [InlineData("ctrl+shift+w", TerminalAction.ClosePane, 0)]
    [InlineData("ctrl+tab", TerminalAction.NextTab, 0)]
    [InlineData("ctrl+shift+tab", TerminalAction.PreviousTab, 0)]
    [InlineData("ctrl+shift+space", TerminalAction.OpenNewTabMenu, 0)]
    [InlineData("ctrl+shift+d", TerminalAction.DuplicateTab, 0)]
    [InlineData("ctrl+shift+n", TerminalAction.NewWindow, 0)]
    public void TabDefaultsFollowWindowsTerminal(string chord, TerminalAction expected, int index)
    {
        Assert.True(Keybindings.Default.TryGet(KeyChord.Parse(chord), out var action));
        Assert.Equal(new ActionInvocation(expected, index), action);
    }

    [Theory]
    [InlineData("alt+shift+plus", TerminalAction.SplitPane, PaneDirection.Right)]
    [InlineData("alt+shift+minus", TerminalAction.SplitPane, PaneDirection.Down)]
    [InlineData("alt+shift+d", TerminalAction.DuplicatePane, PaneDirection.Left)]
    [InlineData("alt+left", TerminalAction.MoveFocus, PaneDirection.Left)]
    [InlineData("alt+down", TerminalAction.MoveFocus, PaneDirection.Down)]
    [InlineData("alt+shift+right", TerminalAction.ResizePane, PaneDirection.Right)]
    [InlineData("alt+shift+up", TerminalAction.ResizePane, PaneDirection.Up)]
    public void PaneDefaultsFollowWindowsTerminal(string chord, TerminalAction expected, PaneDirection direction)
    {
        // DuplicatePane carries no direction (default Left) — it picks one from the pane's shape.
        Assert.True(Keybindings.Default.TryGet(KeyChord.Parse(chord), out var action));
        Assert.Equal(new ActionInvocation(expected, Direction: direction), action);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("ctrl+alt+c")]
    [InlineData("alt+v")]
    public void UnboundChordsFallThrough(string chord)
    {
        Assert.False(Keybindings.Default.TryGet(KeyChord.Parse(chord), out _));
    }
}
