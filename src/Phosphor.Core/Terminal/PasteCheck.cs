using System.Text;

namespace Phosphor.Terminal;

/// <summary>When a paste asks first. Config: <c>[terminal] paste-warning = auto | multiline | never</c>.</summary>
public enum PasteWarning
{
    /// <summary>A paste with a line break the program isn't ready for (bracketed paste is off): each line would run.</summary>
    Auto,

    /// <summary>Any paste with a line break.</summary>
    Multiline,

    /// <summary>Never.</summary>
    Never,
}

/// <summary>Why a paste asks first.</summary>
public enum PasteConcern
{
    /// <summary>More than one line, and the program would take each line break as Enter.</summary>
    CouldRunCommands,

    /// <summary>More than one line (the program marks pastes, but the setting asks anyway).</summary>
    MultipleLines,

    /// <summary>Longer than <see cref="PasteCheck.LargeBytes"/>.</summary>
    Large,
}

/// <summary>
/// Whether a paste should ask first. A line break is only dangerous when the program hasn't
/// turned on bracketed paste (mode 2004): then each one reaches the shell as Enter and runs what
/// came before it (Ghostty's rule; inside a bracketed paste, escape bytes are stripped anyway).
/// </summary>
public static class PasteCheck
{
    /// <summary>A paste this long asks first (with <c>large-paste-warning</c>, on by default): 5 KB, as Windows Terminal.</summary>
    public const int LargeBytes = 5 * 1024;

    public static PasteConcern? Concern(string text, bool bracketed, PasteWarning warning, bool warnLarge)
    {
        var multiline = text.AsSpan().IndexOfAny('\n', '\r') >= 0;
        if (multiline && warning == PasteWarning.Multiline)
        {
            return bracketed ? PasteConcern.MultipleLines : PasteConcern.CouldRunCommands;
        }

        if (multiline && warning == PasteWarning.Auto && !bracketed)
        {
            return PasteConcern.CouldRunCommands;
        }

        return warnLarge && Encoding.UTF8.GetByteCount(text) > LargeBytes ? PasteConcern.Large : null;
    }

    /// <summary>How many lines <paramref name="text"/> is (a final line break doesn't start another).</summary>
    public static int Lines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n', '\r');
        return lines[^1].Length == 0 ? Math.Max(1, lines.Length - 1) : lines.Length;
    }
}
