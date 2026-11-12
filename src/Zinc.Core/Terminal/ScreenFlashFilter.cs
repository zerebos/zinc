namespace Zinc.Terminal;

/// <summary>
/// Catches "visible bells": a program that flashes the screen instead of ringing turns on reverse
/// video (<c>CSI ? 5 h</c>) and off again (<c>CSI ? 5 l</c>) a moment later (readline's
/// <c>bell-style visible</c>, as Git Bash ships). The filter holds the <c>h</c> back. If the
/// <c>l</c> follows within <see cref="FlashMs"/>, both are dropped and the filter reports a bell.
/// Otherwise the program wanted reverse video, and <see cref="TakeExpired"/> hands the held
/// <c>h</c> back to be applied, late. Sequences split across reads are followed too, and a read that
/// ends partway into one is held until the next read (or the expiry).
/// Not thread-safe, because the session calls it under its own lock.
/// </summary>
public sealed class ScreenFlashFilter
{
    /// <summary>Longest reverse video that still counts as a flash.</summary>
    public const int FlashMs = 250;

    private static ReadOnlySpan<byte> On => "\e[?5h"u8;
    private static ReadOnlySpan<byte> Off => "\e[?5l"u8;

    /// <summary>Bytes from the end of the last read that may start one of the sequences.</summary>
    private readonly List<byte> _carry = [];

    /// <summary>When the held <c>CSI ? 5 h</c> arrived, or null when none is held.</summary>
    private long? _heldSince;

    /// <summary>Whether something is held (so <see cref="TakeExpired"/> must be called later).</summary>
    public bool Waiting => _heldSince != null || _carry.Count > 0;

    /// <summary>
    /// Whether <paramref name="input"/> holds either sequence or ends partway into one. If not, and nothing is
    /// <see cref="Waiting"/>, <see cref="Filter"/> would return it unchanged, so the caller can skip the filter, which
    /// copies every read and would make most of the garbage under heavy output. No allocation.
    /// </summary>
    public static bool Concerns(ReadOnlySpan<byte> input)
    {
        for (var at = input.IndexOf((byte)0x1b); at >= 0; at = input.IndexOf((byte)0x1b))
        {
            input = input[at..];
            if (input.StartsWith(On) || input.StartsWith(Off) ||
                (input.Length < On.Length && (On.StartsWith(input) || Off.StartsWith(input))))
            {
                return true;
            }

            input = input[1..];
        }

        return false;
    }

    /// <summary>The output with caught flashes taken out, with <paramref name="flashes"/> counting them.</summary>
    public byte[] Filter(ReadOnlySpan<byte> input, long nowMs, out int flashes)
    {
        flashes = 0;
        var data = _carry.Count == 0 ? input.ToArray() : [.. _carry, .. input];
        _carry.Clear();
        var output = new List<byte>(data.Length);
        var i = 0;
        while (i < data.Length)
        {
            if (data[i] != 0x1b)
            {
                output.Add(data[i++]);
                continue;
            }

            var rest = data.AsSpan(i);
            if (rest.StartsWith(On))
            {
                if (_heldSince == null)
                {
                    _heldSince = nowMs;
                }

                i += On.Length;
            }
            else if (rest.StartsWith(Off))
            {
                if (_heldSince != null)
                {
                    _heldSince = null; // on and off again, so a flash and not reverse video
                    flashes++;
                }
                else
                {
                    output.AddRange(Off.ToArray());
                }

                i += Off.Length;
            }
            else if (rest.Length < On.Length && (On.StartsWith(rest) || Off.StartsWith(rest)))
            {
                _carry.AddRange(rest.ToArray()); // may finish in the next read
                break;
            }
            else
            {
                output.Add(data[i++]);
            }
        }

        return [.. output];
    }

    /// <summary>
    /// What to write now that waiting is over: a held <c>h</c> older than <see cref="FlashMs"/>
    /// (reverse video after all), and a partial sequence no read has finished. Empty if nothing's due.
    /// </summary>
    public byte[] TakeExpired(long nowMs)
    {
        var output = new List<byte>();
        if (_heldSince is { } since && nowMs - since >= FlashMs)
        {
            _heldSince = null;
            output.AddRange(On.ToArray());
        }

        if (_heldSince == null && _carry.Count > 0)
        {
            output.AddRange(_carry);
            _carry.Clear();
        }

        return [.. output];
    }
}
