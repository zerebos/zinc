using Microsoft.UI.Xaml.Input;
using Zinc.Ghostty;
using Zinc.Input;

namespace Zinc.Terminal;

// Keyboard input: shortcuts, then either the key encoder or the character path.
public sealed partial class TerminalControl
{
    private bool _charHandledByKeyDown;
    private char _pendingHighSurrogate;

    /// <summary>
    /// Keys already down when this pane got focus: pressed in something that then handed focus
    /// here (Enter in the rename box or palette, Escape in the find box). Their WM_CHAR and
    /// release still arrive, but belong to where the key went down, so neither reaches the
    /// shell (a stray Enter would run whatever is typed at the prompt).
    /// </summary>
    private readonly HashSet<Windows.System.VirtualKey> _foreignKeys = [];

    /// <summary>
    /// The keys popups commit/cancel/move with, and the character each produces. Only these are
    /// tracked, and only their own character is dropped: a stale key state can then cost at
    /// most one such character, never ordinary typing.
    /// </summary>
    private static readonly Dictionary<Windows.System.VirtualKey, char> HandOffKeys = new()
    {
        [Windows.System.VirtualKey.Enter] = '\r',
        [Windows.System.VirtualKey.Escape] = '\x1b',
        [Windows.System.VirtualKey.Tab] = '\t',
        [Windows.System.VirtualKey.Back] = '\b',
    };

    /// <summary>Notes which of <see cref="HandOffKeys"/> are held as focus arrives (see <see cref="_foreignKeys"/>).</summary>
    private void NoteForeignKeys()
    {
        _foreignKeys.Clear();
        foreach (var key in HandOffKeys.Keys)
        {
            if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                _foreignKeys.Add(key);
            }
        }

        if (_foreignKeys.Count > 0)
        {
            Log.Write($"pane: focused with {string.Join("+", _foreignKeys)} held; its char/release won't be sent");
        }
    }

    /// <summary>Whether <paramref name="c"/> is the character of a key that went down elsewhere.</summary>
    private bool IsForeignChar(char c) =>
        _foreignKeys.Count > 0 && _foreignKeys.Any(k => HandOffKeys[k] == c || (k == Windows.System.VirtualKey.Enter && c == '\n'));

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (!IsFromInputMethodBox(e.OriginalSource)) // (the box's keys were handled before it saw them)
        {
            HandleKeyDown(e);
        }
    }

    /// <summary>A key press, from the pane or (with input methods on) its input box, before the box sees it.</summary>
    private void HandleKeyDown(KeyRoutedEventArgs e)
    {
        if (InputTrace is not { } trace)
        {
            HandleKeyPress(e);
            return;
        }

        // Timed for the debug log, with the character it should produce if it goes to the character path.
        var at = TraceNow;
        var queued = QueuedFor();
        _traceExpect = null;
        _traceModifier = false;
        HandleKeyPress(e);
        if (!_traceModifier)
        {
            trace.KeyDown(at, queued, _traceExpect);
            trace.Handled(TraceNow - at);
            TraceSummary();
        }
    }

    // What HandleKeyPress tells the trace: the character a key press should produce, and that it was a modifier.
    private string? _traceExpect;
    private bool _traceModifier;

    private void HandleKeyPress(KeyRoutedEventArgs e)
    {
        // Pressed (or auto-repeating) here now: from this press on, the key is ours.
        _foreignKeys.Remove(e.Key);

        // Each key's WM_CHAR arrives after its KeyDown and before the next
        // key's KeyDown, so this flag scopes suppression to a single key.
        _charHandledByKeyDown = false;
        var session = _session;
        if (session == null)
        {
            return;
        }

        if (IsFromSearch(e.OriginalSource))
        {
            // Typing in the find box; only our own shortcuts (reopen = refocus) apply.
            var chord = Keyboard.State.Capture();
            if (_keybindings.TryGet(new KeyChord(e.Key, chord.Ctrl, chord.Alt, chord.Shift), out var own) &&
                own.Action == TerminalAction.Find)
            {
                OpenSearch();
                e.Handled = true;
            }

            return;
        }

        var vk = (int)e.Key;
        if (Keyboard.IsModifierKey(vk))
        {
            _traceModifier = true;
            if (IsCtrlKey(e.Key))
            {
                UpdateHoverLink(ctrl: true);
            }

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
        var pressed = new KeyChord(e.Key, state.Ctrl, state.Alt, state.Shift);

        // A key sequence's leader, or a key after it (see TerminalControl.Sequences.cs).
        if (HandleSequenceKey(pressed, status.WasKeyDown, session) is not (KeyStep.NotMine or KeyStep.PassThrough))
        {
            e.Handled = true;
            _charHandledByKeyDown = true;
            return;
        }

        if (_keybindings.TryGet(pressed, out var action) &&
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
            _traceExpect = press.Text;
            return;
        }

        var bytes = session.EncodeKey(request.Action, request.Key, request.Mods, request.ConsumedMods, request.Text,
            request.UnshiftedCodepoint);
        if (bytes.Length > 0)
        {
            session.SendInput(bytes);
            _inputLatency?.Sent(TraceNow);
            ResetBlink();
            e.Handled = true;
            _charHandledByKeyDown = true;
        }
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        if (!IsFromInputMethodBox(e.OriginalSource))
        {
            HandleKeyUp(e);
        }
    }

    private void HandleKeyUp(KeyRoutedEventArgs e)
    {
        // A key's WM_CHAR is always retrieved before its WM_KEYUP, so suppression
        // ends here; later chars without a KeyDown (on-screen keyboards, voice
        // typing, automation) must not be swallowed.
        _charHandledByKeyDown = false;
        if (_foreignKeys.Remove(e.Key))
        {
            return; // released here, pressed elsewhere: not a key event of ours
        }

        if (IsCtrlKey(e.Key))
        {
            UpdateHoverLink(ctrl: false);
        }

        var session = _session;
        if (session == null || Keyboard.IsModifierKey((int)e.Key) || IsFromSearch(e.OriginalSource))
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
        if (!IsFromInputMethodBox(e.OriginalSource))
        {
            HandleCharacter(e);
        }
    }

    private void HandleCharacter(CharacterReceivedRoutedEventArgs e)
    {
        var session = _session;
        if (session == null || IsFromSearch(e.OriginalSource))
        {
            return;
        }

        e.Handled = true;
        var trace = InputTrace;
        var at = trace != null ? TraceNow : 0;
        if (_charHandledByKeyDown || IsForeignChar(e.Character))
        {
            if (_charHandledByKeyDown)
            {
                trace?.Swallowed(e.Character.ToString()); // counted only if it was a typed character's
            }

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
        if (trace != null)
        {
            trace.Character(at, text);
            trace.Sent(at);
            trace.Handled(TraceNow - at);
        }
    }

    /// <summary>
    /// Runs an action as if its key had been pressed in this pane (the command palette
    /// uses this): pane actions run here, the rest go to <see cref="ActionHandler"/>.
    /// </summary>
    public bool Invoke(ActionInvocation invocation) =>
        _session is { } session ? RunAction(invocation, session) : ActionHandler?.Invoke(invocation) ?? false;

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
            case TerminalAction.Find:
                OpenSearch();
                return true;
            case TerminalAction.ZoomIn:
                Zoom(_zoomLevel + 1);
                return true;
            case TerminalAction.ZoomOut:
                Zoom(_zoomLevel - 1);
                return true;
            case TerminalAction.ZoomReset:
                Zoom(0);
                return true;
            case TerminalAction.ScrollPageUp:
            case TerminalAction.ScrollPageDown:
                session.ScrollLines(action == TerminalAction.ScrollPageUp ? -(_rows - 1) : _rows - 1);
                Redraw();
                return true;
            case TerminalAction.JumpToPrompt or TerminalAction.SelectOutput or TerminalAction.CopyOutput:
                return RunCommandAction(invocation, session);
            case TerminalAction.ScrollToTop:
            case TerminalAction.ScrollToBottom:
                session.ScrollViewport(new GhosttyTerminalScrollViewport
                {
                    Tag = action == TerminalAction.ScrollToTop ? ScrollViewportTag.Top : ScrollViewportTag.Bottom,
                });
                Redraw();
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
        Redraw();
    }

    /// <summary>Pastes <paramref name="text"/> as if it came from the clipboard (test hook).</summary>
    internal void PasteForTest(string text)
    {
        if (_session is { } session)
        {
            ClipboardText.PasteForTest(session, _config, XamlRoot, text);
        }
    }

    private void PasteFromClipboard()
    {
        if (_session is { } session)
        {
            ClipboardText.PasteInto(session, _config, XamlRoot);
        }
    }
}
