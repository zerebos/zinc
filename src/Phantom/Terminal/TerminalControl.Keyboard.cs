using Microsoft.UI.Xaml.Input;
using Phantom.Ghostty;
using Phantom.Input;

namespace Phantom.Terminal;

// Keyboard input: shortcuts, then either the key encoder or the character path.
public sealed partial class TerminalControl
{
    private bool _charHandledByKeyDown;
    private char _pendingHighSurrogate;

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        // Each key's WM_CHAR arrives after its KeyDown and before the next
        // key's KeyDown, so this flag scopes suppression to a single key.
        _charHandledByKeyDown = false;
        var session = _session;
        if (session == null)
        {
            return;
        }

        var vk = (int)e.Key;
        if (Keyboard.IsModifierKey(vk))
        {
            return;
        }

        if (_exited)
        {
            e.Handled = true;
            _charHandledByKeyDown = true;
            CloseRequested?.Invoke();
            return;
        }

        var status = e.KeyStatus;
        var state = Keyboard.State.Capture();

        if (_keybindings.TryGet(new KeyChord(e.Key, state.Ctrl, state.Alt, state.Shift), out var action) &&
            RunAction(action, session))
        {
            e.Handled = true;
            _charHandledByKeyDown = true;
            return;
        }

        var press = new KeyPress(
            KeyMap.FromScanCode(status.ScanCode, status.IsExtendedKey, e.Key),
            status.WasKeyDown,
            state.Shift, state.Ctrl, state.Alt, state.Mods,
            state.Translate((uint)vk, status.ScanCode),
            state.WithoutCtrlAlt().Translate((uint)vk, status.ScanCode),
            state.WithoutCtrlAlt().WithoutShift().Translate((uint)vk, status.ScanCode));

        if (KeyDecision.Route(press, session.KittyKeyboardFlags != 0, out var request) == KeyRoute.Character)
        {
            return;
        }

        var bytes = session.EncodeKey(request.Action, request.Key, request.Mods, request.ConsumedMods, request.Text,
            request.UnshiftedCodepoint);
        if (bytes.Length > 0)
        {
            session.SendInput(bytes);
            ResetBlink();
            e.Handled = true;
            _charHandledByKeyDown = true;
        }
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        // A key's WM_CHAR is always retrieved before its WM_KEYUP, so suppression
        // ends here; later chars without a KeyDown (on-screen keyboards, voice
        // typing, automation) must not be swallowed.
        _charHandledByKeyDown = false;

        var session = _session;
        if (session == null || Keyboard.IsModifierKey((int)e.Key))
        {
            return;
        }

        // Key releases are only reported under kitty's "report event types" flag.
        const byte reportEvents = 1 << 1;
        if ((session.KittyKeyboardFlags & reportEvents) == 0)
        {
            return;
        }

        var status = e.KeyStatus;
        var state = Keyboard.State.Capture();
        var key = KeyMap.FromScanCode(status.ScanCode, status.IsExtendedKey, e.Key);
        var bytes = session.EncodeKey(KeyAction.Release, key, state.Mods, GhosttyMods.None, null, 0);
        if (bytes.Length > 0)
        {
            session.SendInput(bytes);
            e.Handled = true;
        }
    }

    protected override void OnCharacterReceived(CharacterReceivedRoutedEventArgs e)
    {
        var session = _session;
        if (session == null)
        {
            return;
        }

        e.Handled = true;
        if (_charHandledByKeyDown)
        {
            return;
        }

        // Anything reaching here wasn't encoded by OnKeyDown (plain text,
        // AltGr text, composed dead keys, or input synthesized without key
        // state), so forward it verbatim, control characters included.
        var c = e.Character;
        if (char.IsHighSurrogate(c))
        {
            _pendingHighSurrogate = c;
            return;
        }

        string text;
        if (char.IsLowSurrogate(c))
        {
            if (_pendingHighSurrogate == 0)
            {
                return;
            }

            text = new string([_pendingHighSurrogate, c]);
            _pendingHighSurrogate = '\0';
        }
        else
        {
            text = c.ToString();
        }

        session.SendInput(text);
        ResetBlink();
    }

    /// <summary>Runs a bound action. Returns false to let the key through to the app.</summary>
    private bool RunAction(ActionInvocation invocation, TerminalSession session)
    {
        var action = invocation.Action;
        switch (action)
        {
            case TerminalAction.Copy:
                CopySelection();
                return true;
            case TerminalAction.CopyIfSelection:
                if (!HasSelection())
                {
                    return false;
                }

                CopySelection();
                return true;
            case TerminalAction.Paste:
                PasteFromClipboard();
                return true;
            case TerminalAction.FontSizeIncrease:
                SetFontSize(_renderer.FontSize + 1);
                return true;
            case TerminalAction.FontSizeDecrease:
                SetFontSize(_renderer.FontSize - 1);
                return true;
            case TerminalAction.FontSizeReset:
                SetFontSize(_config.FontSizeDip);
                return true;
            case TerminalAction.ScrollPageUp:
            case TerminalAction.ScrollPageDown:
                session.ScrollLines(action == TerminalAction.ScrollPageUp ? -(_rows - 1) : _rows - 1);
                _canvas.Invalidate();
                return true;
            case TerminalAction.ScrollToTop:
            case TerminalAction.ScrollToBottom:
                session.ScrollViewport(new GhosttyTerminalScrollViewport
                {
                    Tag = action == TerminalAction.ScrollToTop ? ScrollViewportTag.Top : ScrollViewportTag.Bottom,
                });
                _canvas.Invalidate();
                return true;
            default:
                return ActionHandler?.Invoke(invocation) ?? false;
        }
    }

    // ------------------------------------------------------------ clipboard

    private bool HasSelection() => _session?.GetSelectionText() != null;

    private void CopySelection()
    {
        var text = _session?.GetSelectionText();
        if (text == null)
        {
            return;
        }

        ClipboardText.Set(text);
        _session!.ClearSelection();
        _canvas.Invalidate();
    }

    private void PasteFromClipboard()
    {
        if (_session is { } session)
        {
            ClipboardText.PasteInto(session);
        }
    }
}
