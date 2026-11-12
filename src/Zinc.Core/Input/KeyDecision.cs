using Zinc.Ghostty;

namespace Zinc.Input;

/// <summary>
/// A key press, described by what the active keyboard layout produces for it
/// under different modifier combinations (see <see cref="Keyboard.State.Translate"/>).
/// </summary>
/// <param name="Text">Text with all held modifiers, or null for a dead key and "" for none.</param>
/// <param name="TextWithoutCtrlAlt">Text with Ctrl and Alt released.</param>
/// <param name="TextUnshifted">Text with Ctrl, Alt and Shift released.</param>
public readonly record struct KeyPress(
    GhosttyKey Key,
    bool Repeat,
    bool Shift,
    bool Ctrl,
    bool Alt,
    GhosttyMods Mods,
    string? Text,
    string? TextWithoutCtrlAlt,
    string? TextUnshifted);

/// <summary>Arguments for <c>TerminalSession.EncodeKey</c>.</summary>
public readonly record struct KeyEncodeRequest(
    KeyAction Action,
    GhosttyKey Key,
    GhosttyMods Mods,
    GhosttyMods ConsumedMods,
    string? Text,
    uint UnshiftedCodepoint);

public enum KeyRoute
{
    /// <summary>Leave it to the character (WM_CHAR) path: plain text, AltGr text, dead keys.</summary>
    Character,

    /// <summary>Encode it with libghostty's key encoder.</summary>
    Encode,
}

/// <summary>
/// Decides whether a key press is text for the character path or a key event for
/// the encoder. Windows delivers text separately (WM_CHAR, which also handles dead
/// keys and IME), so only keys whose meaning depends on the terminal's keyboard
/// modes go through the encoder.
/// </summary>
public static class KeyDecision
{
    public static KeyRoute Route(in KeyPress key, bool kittyKeyboard, out KeyEncodeRequest request)
    {
        request = default;
        if (key.Text == null)
        {
            // A dead key. The system composes it and delivers the result as a character.
            return KeyRoute.Character;
        }

        // Plain text, and AltGr text (which Windows reports as Ctrl+Alt), arrive as
        // characters, unless the kitty protocol wants every key as an event.
        if (Keyboard.IsPrintable(key.Text) && !kittyKeyboard && key.Ctrl == key.Alt)
        {
            return KeyRoute.Character;
        }

        var text = Keyboard.IsPrintable(key.TextWithoutCtrlAlt) ? key.TextWithoutCtrlAlt : null;
        var unshifted = Keyboard.IsPrintable(key.TextUnshifted) ? (uint)char.ConvertToUtf32(key.TextUnshifted!, 0) : 0;
        var consumed = text != null && key.Shift && text != key.TextUnshifted ? GhosttyMods.Shift : GhosttyMods.None;

        request = new KeyEncodeRequest(
            key.Repeat ? KeyAction.Repeat : KeyAction.Press, key.Key, key.Mods, consumed, text, unshifted);
        return KeyRoute.Encode;
    }
}
