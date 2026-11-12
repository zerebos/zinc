using System.Globalization;
using System.Text;

namespace Zinc.Input;

/// <summary>
/// Where typing spends its time, for the debug log, to find out why typing feels sluggish or letters arrive swapped.
/// The pane reports each step with a timestamp in milliseconds, and <see cref="Summary"/> gives one line per stretch of
/// typing:
/// <list type="bullet">
/// <item>queue: how long a key press waited before the UI thread got to it (a busy UI thread);</item>
/// <item>key→char: from a key press to its character (Windows delivers text separately from the key);</item>
/// <item>handling: Zinc's own time in the key and character handlers, sending included;</item>
/// <item>echo: from sending input to the shell's output arriving, and from that output to the frame showing it;</item>
/// <item>out of order: a character arriving before one typed earlier (the swapped letters), with examples;</item>
/// <item>swallowed: a character dropped because a later key claimed it.</item>
/// </list>
/// Thread-safe, because output arrives on the reader thread and the rest on the UI thread.
/// </summary>
public sealed class InputLatency
{
    /// <summary>Characters still expected for key presses that went to the character path, oldest first.</summary>
    private readonly List<(string Text, double At)> _expected = [];

    private readonly List<double> _queue = [], _keyToChar = [], _handling = [], _toOutput = [], _toSettled = [], _pieces = [], _toScreen = [];
    private readonly List<string> _examples = [];
    private readonly object _gate = new();
    private int _keys, _characters, _outOfOrder, _swallowed, _unexpected;
    private double _firstAt = -1, _sentAt = -1, _outputAt = -1;

    // The current key's echo: from its send to the last output read within EchoWindow, and how many reads.
    private double _echoFrom = -1, _echoLast = -1;
    private int _echoReads;

    /// <summary>How long after a send output still counts as its echo (a shell may answer in several pieces).</summary>
    private const double EchoWindow = 250;

    /// <summary>
    /// How long expected characters are kept. Older ones are taken to have never come (a dead key, an IME).
    /// </summary>
    private const double ExpectFor = 2000;

    /// <summary>
    /// A key press reached the pane. <paramref name="character"/> is the text it should produce, when it goes to the
    /// character path.
    /// </summary>
    public void KeyDown(double at, double queued, string? character)
    {
        lock (_gate)
        {
            _keys++;
            if (_firstAt < 0)
            {
                _firstAt = at;
            }

            if (queued >= 0)
            {
                _queue.Add(queued);
            }

            _expected.RemoveAll(e => at - e.At > ExpectFor);
            if (!string.IsNullOrEmpty(character))
            {
                _expected.Add((character, at));
            }
        }
    }

    /// <summary>A character arrived and was sent.</summary>
    public void Character(double at, string text)
    {
        lock (_gate)
        {
            _characters++;
            if (_firstAt < 0)
            {
                _firstAt = at;
            }

            var index = _expected.FindIndex(e => e.Text == text);
            if (index < 0)
            {
                _unexpected++; // no key press of ours made it (an on-screen keyboard, a composed character)
                return;
            }

            _keyToChar.Add(at - _expected[index].At);
            if (index > 0)
            {
                // This character was typed after characters that haven't arrived yet, so it overtook them.
                _outOfOrder++;
                if (_examples.Count < 5)
                {
                    _examples.Add($"'{Show(text)}' before '{Show(_expected[0].Text)}'");
                }
            }

            _expected.RemoveAt(index);
        }
    }

    /// <summary>A character dropped because a later key press had already been handled (its suppression flag).</summary>
    public void Swallowed(string text)
    {
        lock (_gate)
        {
            if (_expected.FindIndex(e => e.Text == text) is var index and >= 0)
            {
                _swallowed++;
                if (_examples.Count < 5)
                {
                    _examples.Add($"'{Show(text)}' swallowed");
                }

                _expected.RemoveAt(index);
            }
        }
    }

    /// <summary>Time spent in a handler.</summary>
    public void Handled(double ms)
    {
        lock (_gate)
        {
            _handling.Add(ms);
        }
    }

    /// <summary>Input went to the shell (the first send since the last echo starts the clock).</summary>
    public void Sent(double at)
    {
        lock (_gate)
        {
            if (_sentAt < 0)
            {
                _sentAt = at;
            }

            CloseEcho();
            _echoFrom = at;
        }
    }

    /// <summary>The shell's output arrived (reader thread).</summary>
    public void Output(double at)
    {
        lock (_gate)
        {
            if (_sentAt >= 0)
            {
                _toOutput.Add(at - _sentAt);
                _sentAt = -1;
                _outputAt = at;
            }

            if (_echoFrom >= 0 && at - _echoFrom <= EchoWindow)
            {
                _echoLast = at;
                _echoReads++;
            }
        }
    }

    /// <summary>A frame was drawn.</summary>
    public void Drawn(double at)
    {
        lock (_gate)
        {
            if (_outputAt >= 0)
            {
                _toScreen.Add(at - _outputAt);
                _outputAt = -1;
            }
        }
    }

    /// <summary>
    /// The line for the log once a stretch of typing is over (every 60 keys or so, or 5 seconds after it began),
    /// then starts the next stretch. Null until then (or with nothing typed).
    /// </summary>
    public string? Summary(double now, bool force = false)
    {
        lock (_gate)
        {
            if (_keys + _characters == 0 || (!force && _keys + _characters < 120 && now - _firstAt < 5000))
            {
                return null;
            }

            if (_echoFrom >= 0 && now - _echoFrom > EchoWindow)
            {
                CloseEcho(); // the last key's echo is complete
            }

            var line = new StringBuilder($"input: {_keys} keys, {_characters} characters");
            Add(line, "queue", _queue);
            Add(line, "key→char", _keyToChar);
            Add(line, "handling", _handling);
            Add(line, "send→output", _toOutput);
            Add(line, "send→last output", _toSettled);
            Add(line, "output reads per key", _pieces, "");
            Add(line, "output→frame", _toScreen);
            line.Append(CultureInfo.InvariantCulture, $"; out of order {_outOfOrder}, swallowed {_swallowed}, unexpected {_unexpected}");
            if (_examples.Count > 0)
            {
                line.Append(" (").Append(string.Join(", ", _examples)).Append(')');
            }

            _keys = _characters = _outOfOrder = _swallowed = _unexpected = 0;
            _firstAt = -1;
            _queue.Clear();
            _keyToChar.Clear();
            _handling.Clear();
            _toOutput.Clear();
            _toSettled.Clear();
            _pieces.Clear();
            _toScreen.Clear();
            _examples.Clear();
            return line.ToString();
        }
    }

    /// <summary>
    /// Ends the current key's echo, recording how long until its last piece arrived and in how many reads.
    /// </summary>
    private void CloseEcho()
    {
        if (_echoFrom >= 0 && _echoLast >= 0)
        {
            _toSettled.Add(_echoLast - _echoFrom);
            _pieces.Add(_echoReads);
        }

        _echoFrom = _echoLast = -1;
        _echoReads = 0;
    }

    private static void Add(StringBuilder line, string name, List<double> ms, string unit = " ms")
    {
        if (ms.Count == 0)
        {
            return;
        }

        ms.Sort();
        double At(double p) => ms[Math.Min(ms.Count - 1, (int)Math.Ceiling(p * ms.Count) - 1)];
        line.Append(CultureInfo.InvariantCulture, $"; {name} median {At(0.5):0.#}, p95 {At(0.95):0.#}, max {ms[^1]:0.#}{unit}");
    }

    private static string Show(string text) => text == " " ? "space" : text;
}
