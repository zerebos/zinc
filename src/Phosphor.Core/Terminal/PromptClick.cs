using Phosphor.Ghostty;

namespace Phosphor.Terminal;

/// <summary>How a shell wants a click in its command line handled (OSC 133 options on the prompt mark).</summary>
public enum PromptClickMode
{
    /// <summary>Not at all (the shell said nothing, and it marks no input either).</summary>
    None,

    /// <summary><c>cl=…</c>: left/right arrow keys, one per character, within the line.</summary>
    Arrows,

    /// <summary><c>click_events=1</c>: an SGR mouse click at the clicked screen row (fish 4.1+, nushell).</summary>
    Events,

    /// <summary><c>click_events=2</c>: an SGR mouse click with the row counted from the prompt's first row.</summary>
    EventsRelative,
}

/// <summary>The cells click-to-move looks at, in viewport rows and columns.</summary>
public interface IPromptCells
{
    int Columns { get; }

    int Rows { get; }

    CellSemanticContent Content(int row, int col);

    /// <summary>The second half of a wide character, or the gap a wide character left at a row's end.</summary>
    bool IsSpacer(int row, int col);

    /// <summary>Empty or a space.</summary>
    bool IsBlank(int row, int col);

    /// <summary>The row continues on the next one (soft wrap).</summary>
    bool Wraps(int row);

    /// <summary>The row continues the one above (soft wrap).</summary>
    bool Continues(int row);

    /// <summary>The row is a prompt's continuation line (OSC 133 marks it so).</summary>
    bool IsPromptContinuation(int row);
}

/// <summary>
/// Click to move the cursor in a shell's command line: what to send for a click. A port of
/// Ghostty's <c>Screen.promptClickLine</c> (arrow-key counting within one logical line: input
/// cells only, across soft wraps), plus the options on OSC 133's prompt mark.
/// </summary>
public static class PromptClick
{
    /// <summary>Columns of nothing after which a prompt's input-marked cells are decoration, not text.</summary>
    private const int DecorationGap = 8;

    /// <summary>
    /// The click mode a prompt mark (<c>A</c> or <c>N</c>) announces, or null if it says nothing.
    /// <c>click_events</c> wins over <c>cl</c>, as in Ghostty; every <c>cl</c> value moves by arrows
    /// within the line (Ghostty does the same for <c>m</c>, <c>v</c> and <c>w</c>).
    /// </summary>
    public static PromptClickMode? Announced(IEnumerable<string> options)
    {
        PromptClickMode? cl = null;
        foreach (var option in options)
        {
            var eq = option.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var (key, value) = (option[..eq], option[(eq + 1)..]);
            if (key == "click_events")
            {
                switch (value)
                {
                    case "1":
                        return PromptClickMode.Events;
                    case "2":
                        return PromptClickMode.EventsRelative;
                }
            }
            else if (key == "cl" && value is "line" or "m" or "v" or "w")
            {
                cl = PromptClickMode.Arrows;
            }
        }

        return cl;
    }

    /// <summary>
    /// Where to send a click event for a click at column <paramref name="col"/> of row
    /// <paramref name="row"/>, so every shell does what arrow keys would: a click on the prompt goes
    /// to the start of the input, one past the text (blanks, decoration, empty space) to just after
    /// it. Shells ignore clicks outside their text (nushell does). The column as it is if the row has
    /// no input.
    /// </summary>
    public static int EventColumn(IPromptCells cells, int col, int row)
    {
        var first = -1;
        var lastText = -1;
        for (var x = 0; x < cells.Columns; x++)
        {
            if (cells.Content(row, x) != CellSemanticContent.Input || cells.IsSpacer(row, x))
            {
                continue;
            }

            if (first < 0)
            {
                first = x;
            }

            if (!cells.IsBlank(row, x))
            {
                if (lastText >= 0 && x - lastText > DecorationGap)
                {
                    break;
                }

                lastText = x;
            }
        }

        if (first < 0)
        {
            return col;
        }

        if (col < first)
        {
            return first;
        }

        if (lastText < 0)
        {
            return first;
        }

        // Just after the last character (a wide one takes two columns).
        var end = lastText + 1;
        while (end < cells.Columns && cells.IsSpacer(row, end))
        {
            end++;
        }

        return Math.Min(col, end);
    }

    /// <summary>
    /// The arrow presses that move the cursor at (<paramref name="cursorCol"/>, <paramref name="cursorRow"/>)
    /// to the click: right presses if the click is after it, left presses if before. Only input
    /// cells count (the shell's arrows skip prompt text), a wide character counts once, and only the
    /// cursor's own logical line is crossed. A click past the end of the text goes to its end.
    /// </summary>
    /// <param name="cursorOnInput">The cursor is in the command line (after the prompt's end mark).</param>
    public static (int Left, int Right) Moves(IPromptCells cells, int cursorCol, int cursorRow, int clickCol, int clickRow, bool cursorOnInput)
    {
        if (!cursorOnInput && cells.Content(cursorRow, cursorCol) != CellSemanticContent.Input)
        {
            return (0, 0);
        }

        // A click on a wide character's second half means the character.
        while (clickCol > 0 && cells.IsSpacer(clickRow, clickCol))
        {
            clickCol--;
        }

        if (clickRow == cursorRow && clickCol == cursorCol)
        {
            return (0, 0);
        }

        var cols = cells.Columns;
        bool Counts(int row, int col) => cells.Content(row, col) == CellSemanticContent.Input && !cells.IsSpacer(row, col);

        if (clickRow > cursorRow || (clickRow == cursorRow && clickCol > cursorCol))
        {
            // The input cells after the cursor, to the end of its logical line.
            var after = new List<(int Row, int Col, bool Blank)>();
            for (var row = cursorRow; row < cells.Rows; row++)
            {
                var isCursorRow = row == cursorRow;
                if (!isCursorRow && !cells.IsPromptContinuation(row) && !cells.Continues(row))
                {
                    break;
                }

                // Past the cursor on its row; elsewhere from the first input cell (a shell may indent
                // continuation lines, and its arrows take the cursor to where the input starts).
                var start = cols;
                if (isCursorRow)
                {
                    start = cursorCol + 1;
                }
                else
                {
                    for (var x = 0; x < cols; x++)
                    {
                        if (cells.Content(row, x) == CellSemanticContent.Input)
                        {
                            start = x;
                            break;
                        }
                    }
                }

                for (var x = start; x < cols; x++)
                {
                    if (Counts(row, x))
                    {
                        after.Add((row, x, cells.IsBlank(row, x)));
                    }
                }

                if (!cells.Wraps(row))
                {
                    break;
                }
            }

            // Line editors erase with spaces written as input (zsh clearing an autosuggestion), so the
            // text ends at its last non-blank cell: a click on blanks after it, or past the line,
            // goes to the end of the text and no further (one more arrow there could accept a suggestion).
            // Some prompts also draw after marking input (powerlevel10k's frame corner "─╯" at the far
            // right): text across a wide gap is decoration, as when reading a command's text back.
            var lastText = -1;
            var previous = cursorRow * cols + cursorCol;
            for (var i = 0; i < after.Count; i++)
            {
                if (after[i].Blank)
                {
                    continue;
                }

                var at = after[i].Row * cols + after[i].Col;
                if (at - previous > DecorationGap)
                {
                    break;
                }

                lastText = i;
                previous = at;
            }

            var clicked = after.FindIndex(c => c.Row == clickRow && c.Col == clickCol);
            if (clicked >= 0 && clicked <= lastText)
            {
                return (0, clicked + 1);
            }

            // To just past the last character: one more than the cells up to it if the cursor is on
            // the text (there's text after it, or it's on a character itself).
            var cursorOnText = lastText >= 0 ||
                (cells.Content(cursorRow, cursorCol) == CellSemanticContent.Input && !cells.IsBlank(cursorRow, cursorCol));
            return (0, lastText + 1 + (cursorOnText ? 1 : 0));
        }

        var left = 0;
        for (var row = cursorRow; row >= clickRow && row >= 0; row--)
        {
            var end = row == cursorRow ? cursorCol : cols;
            for (var x = end - 1; x >= 0; x--)
            {
                if (!Counts(row, x))
                {
                    continue;
                }

                left++;
                if (row == clickRow && x == clickCol)
                {
                    return (left, 0);
                }
            }

            if (!cells.Continues(row))
            {
                break;
            }
        }

        return (left, 0);
    }
}
