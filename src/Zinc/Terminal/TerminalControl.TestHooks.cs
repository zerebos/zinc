using Zinc.Input;

namespace Zinc.Terminal;

// What the test harness (`MainWindow.ReplayTestKeys`) calls on a pane. Development only.
public sealed partial class TerminalControl
{
    /// <summary>Pastes <paramref name="text"/> as if it came from the clipboard (test hook).</summary>
    internal void PasteForTest(string text)
    {
        if (_session is { } session)
        {
            ClipboardText.PasteForTest(session, _config, XamlRoot, text);
        }
    }

    /// <summary>Test hook (ZINC_TEST_KEYS): a chord pressed here, as far as sequences go.</summary>
    internal KeyStep PressForTest(KeyChord pressed) =>
        _session is { } session ? HandleSequenceKey(pressed, repeat: false, session) : KeyStep.NotMine;

    /// <summary>Test hook: expanded, and kept so whatever the pointer does.</summary>
    internal void PinScrollBarExpandedForTest()
    {
        _scrollBarPinned = true;
        SetScrollBarExpanded(true);
    }
}
