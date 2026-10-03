namespace Zinc.Terminal;

/// <summary>What a piece of output erased (<see cref="EraseWatch"/>).</summary>
[Flags]
public enum Erased
{
    None = 0,

    /// <summary><c>CSI J</c>, <c>CSI 1 J</c>, <c>CSI 2 J</c>: part or all of the screen.</summary>
    Display = 1,

    /// <summary><c>CSI 3 J</c> (the scrollback: what <c>clear</c> sends in bash and PowerShell) or a full reset (<c>ESC c</c>).</summary>
    Scrollback = 2,
}

/// <summary>
/// Spots output that erases rows as it streams by, split across reads included, so the commands Zinc tracked on erased
/// rows go with them: a tester saw failure bars stay on the blank screen after <c>clear</c> (2026-10-03). Scans from
/// escape to escape; keeps three bytes of state between reads; allocates nothing.
/// </summary>
public sealed class EraseWatch
{
    private const byte Esc = 0x1B;

    private enum State : byte
    {
        Ground,
        Escape,
        Csi,
    }

    private State _state;
    private int _param, _digits;

    public Erased Seen(ReadOnlySpan<byte> data)
    {
        var seen = Erased.None;
        var i = 0;
        while (i < data.Length)
        {
            var b = data[i];
            switch (_state)
            {
                case State.Ground:
                    var esc = data[i..].IndexOf(Esc);
                    if (esc < 0)
                    {
                        return seen;
                    }

                    i += esc + 1;
                    _state = State.Escape;
                    continue;
                case State.Escape:
                    if (b == (byte)'[')
                    {
                        (_state, _param, _digits) = (State.Csi, 0, 0);
                    }
                    else
                    {
                        seen |= b == (byte)'c' ? Erased.Scrollback : Erased.None;
                        _state = b == Esc ? State.Escape : State.Ground;
                    }

                    break;
                case State.Csi:
                    if (b is >= (byte)'0' and <= (byte)'9' && _digits < 3)
                    {
                        _param = _param * 10 + (b - '0');
                        _digits++;
                    }
                    else
                    {
                        seen |= b != (byte)'J' ? Erased.None : _param == 3 ? Erased.Scrollback : Erased.Display;
                        _state = b == Esc ? State.Escape : State.Ground;
                    }

                    break;
            }

            i++;
        }

        return seen;
    }
}
