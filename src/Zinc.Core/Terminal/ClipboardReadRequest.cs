namespace Zinc.Terminal;

/// <summary>
/// Finds a program's clipboard read (<c>OSC 52 ; &lt;where&gt; ; ? ST</c>) in its output, so the session
/// can ask before libghostty answers it. The callback that answers must reply at once, while the
/// terminal is locked, so a question can't be asked there. A request split across two reads isn't
/// seen here and gets the callback's safe answer (denied).
/// </summary>
public static class ClipboardReadRequest
{
    private static ReadOnlySpan<byte> Start => "\e]52;"u8;

    /// <summary>The first request in <paramref name="data"/>: where it starts and where it ends (exclusive).</summary>
    public static bool TryFind(ReadOnlySpan<byte> data, out int start, out int end)
    {
        var from = 0;
        while (from < data.Length)
        {
            var at = data[from..].IndexOf(Start);
            if (at < 0)
            {
                break;
            }

            start = from + at;
            var i = start + Start.Length;
            while (i < data.Length && data[i] is not ((byte)';' or 0x07 or 0x1b))
            {
                i++; // which clipboard: c, p, s, ... (or nothing)
            }

            if (i + 1 < data.Length && data[i] == ';' && data[i + 1] == '?')
            {
                var t = i + 2;
                if (t < data.Length && data[t] == 0x07)
                {
                    end = t + 1;
                    return true;
                }

                if (t + 1 < data.Length && data[t] == 0x1b && data[t + 1] == '\\')
                {
                    end = t + 2;
                    return true;
                }
            }

            from = start + Start.Length; // a write (OSC 52 ; c ; <base64>), or incomplete, so look further
        }

        start = end = -1;
        return false;
    }
}
