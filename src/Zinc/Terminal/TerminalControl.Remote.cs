namespace Zinc.Terminal;

// zn's terminal send and terminal read.
public sealed partial class TerminalControl
{
    /// <summary>
    /// Types <paramref name="text"/> into the program as if typed (<c>\r</c> is Enter), or with
    /// <paramref name="paste"/> as a paste (bracketed when the program asked for that). False when nothing runs here.
    /// </summary>
    public bool SendText(string text, bool paste)
    {
        if (_session is not { } session || _exited || _previewScript != null)
        {
            return false;
        }

        if (paste)
        {
            session.Paste(text);
        }
        else
        {
            session.SendInput(text);
        }

        return true;
    }

    /// <summary>The screen's text (or the last <paramref name="lines"/>, scrollback included); null before the terminal starts.</summary>
    public string? ReadText(int? lines, bool styled) => _session?.ReadText(lines, styled);
}
