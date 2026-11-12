using System.Globalization;
using System.Text;

namespace Zinc.Terminal;

/// <summary>
/// One OSC 133 mark from the shell: <c>A</c> a prompt starts, <c>B</c> the prompt ends and the
/// command line starts, <c>C</c> the command runs (its output follows), <c>D</c> it finished, with
/// its exit code when the shell sends one. A prompt mark (<c>A</c>, or <c>N</c>) may say how a
/// click in the command line moves the cursor (<see cref="Click"/>).
/// </summary>
public readonly record struct ShellMark(char Kind, int? ExitCode = null, string? CommandLine = null, PromptClickMode? Click = null)
{
    /// <summary>
    /// A prompt mark's options as sent ("click_events=1", "aid=123", or "" for none, and null on
    /// other marks). Each shell sends its own, so they tell nested shells apart.
    /// </summary>
    public string? Options { get; init; }

    /// <summary>Reads the text after <c>ESC ] 133;</c> (up to the terminator), or null if it isn't a mark.</summary>
    public static ShellMark? Parse(string payload)
    {
        if (payload.Length == 0)
        {
            return null;
        }

        var kind = payload[0];
        if (payload.Length > 1 && payload[1] != ';')
        {
            return null;
        }

        int? exit = null;
        if (kind == 'D' && payload.Length > 2)
        {
            var param = payload[2..];
            var end = param.IndexOf(';');
            if (int.TryParse(end < 0 ? param : param[..end], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var code))
            {
                exit = code;
            }
        }

        // C may carry the command line (kitty's cmdline_url, percent-encoded), which is better than reading it
        // back from the screen, where a line editor's redraws can leave stray cells.
        string? commandLine = null;
        if (kind == 'C')
        {
            foreach (var option in payload.Split(';').Skip(1))
            {
                if (option.StartsWith("cmdline_url=", StringComparison.Ordinal))
                {
                    try
                    {
                        commandLine = Uri.UnescapeDataString(option["cmdline_url=".Length..]);
                    }
                    catch (UriFormatException)
                    {
                    }
                }
            }
        }

        if (kind is 'A' or 'N')
        {
            var options = payload.Length > 2 ? payload[2..] : "";
            return new ShellMark(kind, Click: PromptClick.Announced(options.Split(';'))) { Options = options };
        }

        return new ShellMark(kind, exit, commandLine);
    }
}

/// <summary>
/// Finds OSC 133 marks in PTY output as it streams by, so the session can note where the cursor
/// is at each one (libghostty parses them too, for its own prompt bookkeeping, but keeps no
/// command history and ignores exit codes). Keeps state between chunks, because a mark may be split
/// across reads.
/// </summary>
public sealed class Osc133Scanner
{
    private const byte Esc = 0x1B, Bel = 0x07;
    private static readonly byte[] Prefix = "133;"u8.ToArray();

    private enum State
    {
        Ground,
        Escape,
        Osc,
        OscEscape,
    }

    private State _state;

    // The OSC's payload while it could still be a 133 (with room for a command line, and longer ones are dropped).
    private readonly byte[] _payload = new byte[4096];
    private int _length;
    private bool _candidate;

    /// <summary>
    /// Scans <paramref name="data"/> up to the end of the next complete OSC 133 mark. Returns the
    /// index just past it (write the data up to there, note the mark, continue from there), or
    /// -1 if <paramref name="data"/> holds no complete mark.
    /// </summary>
    public int Next(ReadOnlySpan<byte> data, out ShellMark mark)
    {
        mark = default;
        var i = 0;
        while (i < data.Length)
        {
            switch (_state)
            {
                case State.Ground:
                    var esc = data[i..].IndexOf(Esc);
                    if (esc < 0)
                    {
                        return -1;
                    }

                    i += esc + 1;
                    _state = State.Escape;
                    break;
                case State.Escape:
                    if (data[i] == (byte)']')
                    {
                        _state = State.Osc;
                        _length = 0;
                        _candidate = true;
                        i++;
                    }
                    else if (data[i] == Esc)
                    {
                        i++;
                    }
                    else
                    {
                        _state = State.Ground;
                    }

                    break;
                case State.Osc:
                    var b = data[i];
                    if (b == Bel || b == Esc)
                    {
                        i++;
                        if (b == Esc)
                        {
                            _state = State.OscEscape;
                            break;
                        }

                        _state = State.Ground;
                        if (Finish() is { } bel)
                        {
                            mark = bel;
                            return i;
                        }

                        break;
                    }

                    if (b is 0x18 or 0x1A)
                    {
                        _state = State.Ground; // CAN / SUB cancel the sequence
                    }
                    else if (_candidate)
                    {
                        Collect(b);
                    }

                    i++;
                    break;
                case State.OscEscape:
                    if (data[i] == (byte)'\\')
                    {
                        i++;
                        _state = State.Ground;
                        if (Finish() is { } st)
                        {
                            mark = st;
                            return i;
                        }
                    }
                    else
                    {
                        // Another sequence cut this one off, and the ESC starts it.
                        _state = State.Escape;
                    }

                    break;
            }
        }

        return -1;
    }

    private void Collect(byte b)
    {
        if (_length == _payload.Length || (_length < Prefix.Length && b != Prefix[_length]))
        {
            _candidate = false;
            return;
        }

        _payload[_length++] = b;
    }

    private ShellMark? Finish()
    {
        if (!_candidate || _length <= Prefix.Length)
        {
            return null;
        }

        return ShellMark.Parse(Encoding.UTF8.GetString(_payload, Prefix.Length, _length - Prefix.Length));
    }
}
