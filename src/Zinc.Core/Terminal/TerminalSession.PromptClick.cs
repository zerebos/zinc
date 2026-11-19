using System.Text;
using Zinc.Ghostty;

namespace Zinc.Terminal;

/// <summary>Click to move the cursor in a shell's command line (<see cref="PromptClick"/>).</summary>
public sealed unsafe partial class TerminalSession
{
    private enum PromptState
    {
        None,
        Prompt,
        Input,
        Running,
    }

    private PromptState _promptState;

    /// <summary>
    /// What the shell's last prompt mark asked for. Ghostty keeps it until another prompt says
    /// otherwise. Here each prompt decides, so leaving fish for zsh doesn't send zsh click events.
    /// </summary>
    private PromptClickMode? _announcedClick;

    /// <summary>A mark has been seen, so the shell marks its prompts (shell integration, its own or ours).</summary>
    private bool _sawPromptMark;

    /// <summary>
    /// Read from the marks. True while the shell waits at its prompt (or between a command's end and the next prompt),
    /// false while a command runs, and null if the shell has marked nothing.
    /// </summary>
    public bool? AtPrompt
    {
        get
        {
            lock (Lock)
            {
                return !_sawPromptMark ? null : _promptState != PromptState.Running;
            }
        }
    }

    private void NotePromptState(ShellMark mark)
    {
        _sawPromptMark = true;
        if (mark.Kind is 'A' or 'N')
        {
            _announcedClick = mark.Click;
        }

        var was = _promptState;
        _promptState = mark.Kind switch
        {
            'A' or 'N' or 'P' => PromptState.Prompt,
            'B' or 'I' => PromptState.Input,
            'C' => PromptState.Running,
            'D' => PromptState.None,
            _ => _promptState,
        };

        if ((was is PromptState.Prompt or PromptState.Input) != (_promptState is PromptState.Prompt or PromptState.Input) &&
            _cursorDefaults.AtPrompt != null)
        {
            ApplyCursorDefaults(); // at the prompt, or a command started
        }
    }

    /// <summary>
    /// What to send the shell for a plain click at viewport cell (<paramref name="col"/>,
    /// <paramref name="row"/>) to move its cursor there. That's arrow keys or a mouse press, as its prompt
    /// mark asked, or an empty array if the click is in the command line but needs no move. Null if
    /// it isn't a click-to-move (no prompt waiting for input, a full-screen program, or a click above
    /// the prompt).
    /// </summary>
    /// <param name="assumeArrows">
    /// A shell that marks where its input starts but doesn't say how to move gets arrow keys
    /// (powerlevel10k's own integration, Zinc's scripts, a hand-written PowerShell prompt).
    /// </param>
    public byte[]? ClickToMove(int col, int row, bool assumeArrows)
    {
        lock (Lock)
        {
            if (_disposed)
            {
                return null;
            }

            int screen = 0;
            Native.TerminalGet(_terminal, TerminalData.ActiveScreen, &screen);
            if (screen != 0 || _promptState is not (PromptState.Prompt or PromptState.Input))
            {
                return null;
            }

            var mode = _announcedClick ?? (assumeArrows && _promptState == PromptState.Input ? PromptClickMode.Arrows : PromptClickMode.None);
            if (mode == PromptClickMode.None)
            {
                return null;
            }

            // Where the prompt starts, in viewport rows. A prompt above the viewport stays before every click.
            var promptRow = int.MinValue;
            if (_commands.Count > 0 && _commands[^1] is { Closed: false, Prompt: not 0 } command &&
                Native.TrackedGridRefPoint(command.Prompt, PointTag.Viewport, out var prompt) == GhosttyResult.Success)
            {
                promptRow = (int)prompt.Y;
            }

            if (row < promptRow)
            {
                return null;
            }

            switch (mode)
            {
                case PromptClickMode.Events:
                case PromptClickMode.EventsRelative:
                    // A left press and release, aimed at the text if the click missed it. Ghostty and kitty send
                    // only the press, and nushell (reedline) then takes the button as held and ignores the next clicks.
                    col = PromptClick.EventColumn(new ViewportCells(_terminal, _size.Columns, _size.Rows), col, row);
                    var y = mode == PromptClickMode.Events ? row + 1 : row - Math.Max(promptRow, 0) + 1;
                    return Encoding.ASCII.GetBytes($"\e[<0;{col + 1};{y}M\e[<0;{col + 1};{y}m");
                default:
                    if (CursorInViewport() is not var (cursorCol, cursorRow))
                    {
                        return null;
                    }

                    var cells = new ViewportCells(_terminal, _size.Columns, _size.Rows);
                    var (left, right) = PromptClick.Moves(cells, cursorCol, cursorRow, col, row, _promptState == PromptState.Input);
                    var appCursor = GetMode(GhosttyMode.DecCkm);
                    var arrow = (left > 0, appCursor) switch
                    {
                        (true, true) => "\eOD",
                        (true, false) => "\e[D",
                        (false, true) => "\eOC",
                        (false, false) => "\e[C",
                    };
                    var keys = new StringBuilder();
                    for (var i = 0; i < left + right; i++)
                    {
                        keys.Append(arrow);
                    }

                    return Encoding.ASCII.GetBytes(keys.ToString());
            }
        }
    }

    /// <summary>The cursor's viewport cell, or null while it's scrolled out of view. Hold the lock.</summary>
    private (int Col, int Row)? CursorInViewport()
    {
        ushort x = 0, y = 0;
        Native.TerminalGet(_terminal, TerminalData.CursorX, &x);
        Native.TerminalGet(_terminal, TerminalData.CursorY, &y);
        GhosttyTerminalScrollbar sb;
        Native.TerminalGet(_terminal, TerminalData.Scrollbar, &sb);
        var row = (long)(sb.Total - sb.Len) + y - (long)sb.Offset;
        return row >= 0 && row < _size.Rows ? (x, (int)row) : null;
    }

    /// <summary>
    /// The viewport's cells through grid refs, resolved for each question, so use it under the lock with nothing
    /// mutating in between.
    /// </summary>
    private sealed class ViewportCells(nint terminal, ushort cols, ushort rows) : IPromptCells
    {
        public int Columns => cols;

        public int Rows => rows;

        public CellSemanticContent Content(int row, int col) =>
            Cell(row, col, CellData.SemanticContent) is { } value ? (CellSemanticContent)value : CellSemanticContent.Output;

        public bool IsSpacer(int row, int col) =>
            Cell(row, col, CellData.Wide) is (int)CellWide.SpacerTail or (int)CellWide.SpacerHead;

        public bool IsBlank(int row, int col)
        {
            var gridRef = default(GhosttyGridRef);
            var point = new GhosttyPoint { Tag = PointTag.Viewport, X = (ushort)col, Y = (uint)row };
            uint first = 0;
            return Native.TerminalGridRef(terminal, point, &gridRef) != GhosttyResult.Success ||
                   Native.GridRefGraphemes(&gridRef, &first, 1, out var len) is not (GhosttyResult.Success or GhosttyResult.OutOfSpace) ||
                   len == 0 || first == ' ';
        }

        public bool Wraps(int row) => Row<byte>(row, RowData.Wrap) is not (null or 0);

        public bool Continues(int row) => Row<byte>(row, RowData.WrapContinuation) is not (null or 0);

        public bool IsPromptContinuation(int row) =>
            Row<int>(row, RowData.SemanticPrompt) == (int)RowSemanticPrompt.PromptContinuation;

        private int? Cell(int row, int col, CellData data)
        {
            var gridRef = default(GhosttyGridRef);
            var point = new GhosttyPoint { Tag = PointTag.Viewport, X = (ushort)col, Y = (uint)row };
            int value;
            return Native.TerminalGridRef(terminal, point, &gridRef) == GhosttyResult.Success &&
                   Native.GridRefCell(&gridRef, out var cell) == GhosttyResult.Success &&
                   Native.CellGet(cell, data, &value) == GhosttyResult.Success
                ? value
                : null;
        }

        private T? Row<T>(int row, RowData data) where T : unmanaged
        {
            var gridRef = default(GhosttyGridRef);
            var point = new GhosttyPoint { Tag = PointTag.Viewport, X = 0, Y = (uint)row };
            T value;
            return Native.TerminalGridRef(terminal, point, &gridRef) == GhosttyResult.Success &&
                   Native.GridRefRow(&gridRef, out var raw) == GhosttyResult.Success &&
                   Native.ScreenRowGet(raw, data, &value) == GhosttyResult.Success
                ? value
                : null;
        }
    }
}
