using Zinc.Ghostty;

namespace Zinc.Terminal;

/// <summary>
/// A command as the shell marked it with OSC 133. Rows are screen rows (0 = the top of the
/// scrollback; the scrollbar's units), valid until the terminal next changes.
/// </summary>
/// <param name="PromptRow">Where its prompt starts.</param>
/// <param name="OutputRow">Where its output starts (null until it runs).</param>
/// <param name="LastRow">Its last row: the one before the next prompt, or the cursor's while it runs.</param>
/// <param name="Text">The command line, as typed.</param>
/// <param name="ExitCode">How it ended, if the shell said.</param>
/// <param name="Duration">How long it ran, once it has finished.</param>
public sealed record ShellCommand(long PromptRow, long? OutputRow, long LastRow, string? Text, int? ExitCode,
    DateTime? Started, TimeSpan? Duration, bool Running)
{
    /// <summary>
    /// Something was typed and run. Shells report the last exit code at every prompt, and some
    /// (powerlevel10k) mark an empty Enter as a command that "ran" too.
    /// </summary>
    public bool Ran => OutputRow != null && Text != null;

    /// <summary>It ran and reported a nonzero exit code.</summary>
    public bool Failed => Ran && ExitCode is { } code && code != 0;
}

// Shell integration: the commands the shell marks with OSC 133 (A prompt, B command line, C output,
// D finished + exit code), each pinned to tracked grid refs so they follow scrolling, pruning and
// reflow. libghostty marks prompt rows and cells itself but keeps no history of commands.
public sealed unsafe partial class TerminalSession
{
    /// <summary>How many commands are remembered (older ones are forgotten first).</summary>
    private const int MaxCommands = 2000;

    private readonly Osc133Scanner _markScanner = new();
    private readonly List<CommandRefs> _commands = [];
    private bool _commandsChanged;
    private List<ShellCommand>? _commandSnapshot;

    /// <summary>A command started or finished (or a prompt appeared). Raised after the write, outside the lock.</summary>
    public event Action? CommandsChanged;

    /// <summary>The time commands are stamped with (tests replace it).</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private sealed class CommandRefs
    {
        public nint Prompt, Input, Output, End;
        public DateTime? Started, Ended;
        public int? Exit;
        public string? Text;

        /// <summary>Its prompt row was erased: left out of snapshots, and freed at the next prune.</summary>
        public bool Erased;
        public bool Closed;

        public void Free()
        {
            foreach (var r in new[] { Prompt, Input, Output, End })
            {
                if (r != 0)
                {
                    Native.TrackedGridRefFree(r);
                }
            }

            Prompt = Input = Output = End = 0;
        }
    }

    private readonly EraseWatch _eraseWatch = new();

    /// <summary>
    /// Rows were erased: finished commands whose prompt went with them are gone, failure bars and all. Checked now,
    /// before the next prompt (often on the same row: <c>clear</c> puts it at the top) flags that row again. A wiped
    /// scrollback forgets every finished command (their rows' places aren't reliable after it). Ones still running stay
    /// (the <c>clear</c> itself, a nested shell's host): their D and the next prompt close them.
    /// </summary>
    private void NoteErased(Erased erased)
    {
        if (erased.HasFlag(Erased.Scrollback))
        {
            ForgetFinishedCommands();
            return;
        }

        // Erasing part of a row (from the cursor) clears its cells but not its prompt flag: the cursor's row counts as
        // erased when nothing is left on it. Only that row: an old prompt's first row can be empty by design (themes
        // that start with a blank line), and only after a clear is an old prompt ever on the cursor's row.
        nuint history = 0;
        ushort cursorY = 0, cols = 0;
        Native.TerminalGet(_terminal, TerminalData.ScrollbackRows, &history);
        Native.TerminalGet(_terminal, TerminalData.CursorY, &cursorY);
        Native.TerminalGet(_terminal, TerminalData.Cols, &cols);
        var cursorRow = (long)history + cursorY;
        foreach (var command in _commands)
        {
            if (command.Closed && !command.Erased && TryScreen(command.Prompt, out var prompt) &&
                (!IsPromptRow(prompt.Y) ||
                 prompt.Y == cursorRow && string.IsNullOrWhiteSpace(RowText.Read(_terminal, PointTag.Screen, (uint)prompt.Y, cols).Text)))
            {
                command.Erased = true;
                _commandSnapshot = null;
                _commandsChanged = true;
            }
        }
    }

    private void ForgetFinishedCommands()
    {
        var kept = 0;
        for (var i = 0; i < _commands.Count; i++)
        {
            var command = _commands[i];
            if (command.Closed)
            {
                command.Free();
            }
            else
            {
                _commands[kept++] = command;
            }
        }

        _commands.RemoveRange(kept, _commands.Count - kept);
        _commandSnapshot = null;
        _commandsChanged = true; // announced after the write, outside the lock
    }

    /// <summary>Writes PTY output, noting the cursor at each OSC 133 mark. Hold the lock.</summary>
    private void WriteWithMarks(ReadOnlySpan<byte> data)
    {
        while (true)
        {
            var end = _markScanner.Next(data, out var mark);
            var chunk = end < 0 ? data : data[..end];
            if (end >= 0 && mark.Kind is 'A' or 'N')
            {
                WritePromptStart(chunk);
            }
            else
            {
                VtWrite(chunk);
            }

            if (_eraseWatch.Seen(chunk) is var erased and not Erased.None)
            {
                NoteErased(erased);
            }

            if (end < 0)
            {
                break;
            }

            OnShellMark(mark);
            data = data[end..];
        }

        // Screen rows shift as the scrollback fills and prunes.
        _commandSnapshot = null;
    }

    private void VtWrite(ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            Native.TerminalVtWrite(_terminal, p, (nuint)data.Length);
        }
    }

    private static ReadOnlySpan<byte> MarkPrefix => "\e]133;"u8;
    private static ReadOnlySpan<byte> PromptStartOnly => "\e]133;P\a"u8;

    /// <summary>
    /// Writes output ending in a prompt-start mark (A or N), minus its fresh line. Per the spec (FinalTerm, kitty),
    /// libghostty starts a new line for one that arrives mid-line; under ConPTY that's a move ConPTY never hears of, so
    /// the two screens disagree from there on: a tester's prompt, drawn with Write-Host before PowerShell's marks, left
    /// the cursor (and the typing PSReadLine draws) a line below the prompt. Windows Terminal does no fresh line either.
    /// Mid-line, libghostty gets a plain prompt start (P) instead; Zinc's own tracking reads the mark as sent.
    /// </summary>
    private void WritePromptStart(ReadOnlySpan<byte> chunk)
    {
        var at = chunk.LastIndexOf(MarkPrefix);
        if (at < 0)
        {
            VtWrite(chunk); // the mark began in an earlier read, already written
            return;
        }

        VtWrite(chunk[..at]);
        ushort x = 0;
        Native.TerminalGet(_terminal, TerminalData.CursorX, &x);
        VtWrite(x == 0 ? chunk[at..] : PromptStartOnly);
    }

    private void OnShellMark(ShellMark mark)
    {
        int screen = 0;
        Native.TerminalGet(_terminal, TerminalData.ActiveScreen, &screen);
        if (screen != 0)
        {
            return; // full-screen apps' marks aren't commands
        }

        NotePromptState(mark);

        var last = _commands.Count > 0 && !_commands[^1].Closed ? _commands[^1] : null;
        var lastD = _lastD;
        _lastD = null;
        switch (mark.Kind)
        {
            case 'A':
                var signature = mark.Options ?? "";
                _outerSignature ??= signature;
                if (signature != Signature(_nested.Count) && OuterLevel(signature) is { } outer)
                {
                    // Back at a shell further out: the nested one has ended.
                    LeaveNested(outer, lastD);
                    last = _commands.Count > 0 && !_commands[^1].Closed ? _commands[^1] : null;
                }
                else if (last is { Started: not null } && signature != Signature(_nested.Count) && _nested.Count < MaxNesting)
                {
                    // Another shell's prompt while a command runs: the command started that shell
                    // (fish from zsh). It runs until that shell ends; the new shell's commands are its own.
                    _nested.Add((signature, last));
                    last = null;
                }

                if (last != null)
                {
                    // No D: it ended anyway (the shell doesn't send one, or it was interrupted).
                    last.Closed = true;
                    last.Ended ??= last.Started != null ? Clock() : null;
                }

                if (TrackCursor(atLineStart: true) is { } prompt)
                {
                    _commands.Add(new CommandRefs { Prompt = prompt });
                    Prune();
                }

                break;
            case 'B' when last is { Input: 0 }:
                last.Input = TrackCursor(atLineStart: false) ?? 0;
                break;
            case 'C' when last is { Started: null }:
                last.Output = TrackCursor(atLineStart: false) ?? 0;
                last.Started = Clock();
                last.Text = string.IsNullOrWhiteSpace(mark.CommandLine) ? ReadCommandLine(last) : mark.CommandLine.Trim();
                break;
            case 'D' when last != null:
                last.End = TrackCursor(atLineStart: false) ?? 0;
                last.Exit = mark.ExitCode;
                last.Ended = last.Started != null ? Clock() : null;
                last.Closed = true;
                _lastD = (last, mark.ExitCode);
                break;
            case 'D' when _nested.Count > 0:
                _lastD = (null, mark.ExitCode); // the shell around a nested one, after it ended
                return;
            default:
                return;
        }

        _commandsChanged = true;
        _commandSnapshot = null;
    }

    /// <summary>Shells this deep inside each other are taken as one (a shell that never sends D would otherwise nest forever).</summary>
    private const int MaxNesting = 8;

    /// <summary>
    /// Shells started from a command of the shell around them (fish from zsh), innermost last: each
    /// one's prompt-mark options, which tell it from the others, and the command it runs in. Two shells
    /// whose prompts look alike (zsh in zsh) can't be told apart; Zinc's own scripts send their
    /// process id (aid) so theirs always differ.
    /// </summary>
    private readonly List<(string Signature, CommandRefs Host)> _nested = [];

    /// <summary>The outermost shell's prompt-mark options (from its first prompt).</summary>
    private string? _outerSignature;

    /// <summary>The D mark just before this one, and the command it closed: an ending nested shell's, if an outer prompt follows.</summary>
    private (CommandRefs? Closed, int? Exit)? _lastD;

    /// <summary>The prompt-mark options of the shell <paramref name="level"/> deep (0: the outermost).</summary>
    private string? Signature(int level) => level == 0 ? _outerSignature : _nested[level - 1].Signature;

    /// <summary>The innermost shell further out than the current one whose prompts look like <paramref name="signature"/>.</summary>
    private int? OuterLevel(string signature)
    {
        for (var level = _nested.Count - 1; level >= 0; level--)
        {
            if (Signature(level) == signature)
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>
    /// Back at shell <paramref name="level"/>: every shell inside it has ended, and so have the
    /// commands they ran in. A D just before is that shell's report on its command's exit: if it
    /// closed a command of the nested shell (its "exit"), that was the wrong one.
    /// </summary>
    private void LeaveNested(int level, (CommandRefs? Closed, int? Exit)? lastD)
    {
        if (_commands.Count > 0 && !_commands[^1].Closed)
        {
            var open = _commands[^1];
            open.Closed = true;
            open.Ended ??= open.Started != null ? Clock() : null;
        }

        for (var i = _nested.Count - 1; i >= level; i--)
        {
            var host = _nested[i].Host;
            host.Closed = true;
            host.Ended = Clock();
            // The D comes from the shell we're back at, about its own command: the outermost one ending.
            if (i == level && lastD is var (closed, exit))
            {
                host.Exit = exit;
                if (closed is { Started: not null })
                {
                    closed.Exit = null;
                }
            }

            // No End: the command's rows stop where the nested shell's first prompt starts.
        }

        _nested.RemoveRange(level, _nested.Count - level);
    }

    private nint? TrackCursor(bool atLineStart)
    {
        ushort x = 0, y = 0;
        Native.TerminalGet(_terminal, TerminalData.CursorX, &x);
        Native.TerminalGet(_terminal, TerminalData.CursorY, &y);
        var point = new GhosttyPoint { Tag = PointTag.Active, X = atLineStart ? (ushort)0 : x, Y = y };
        return Native.TerminalGridRefTrack(_terminal, point, out var tracked) == GhosttyResult.Success ? tracked : null;
    }

    /// <summary>Whether screen row <paramref name="y"/> still holds a prompt (libghostty flags the row a prompt mark lands on; erasing it clears that).</summary>
    private bool IsPromptRow(long y)
    {
        var gridRef = GhosttyGridRef.Init();
        var point = new GhosttyPoint { Tag = PointTag.Screen, X = 0, Y = (uint)y };
        int semantic;
        return Native.TerminalGridRef(_terminal, point, &gridRef) == GhosttyResult.Success &&
               Native.GridRefRow(&gridRef, out var row) == GhosttyResult.Success &&
               Native.ScreenRowGet(row, RowData.SemanticPrompt, &semantic) == GhosttyResult.Success &&
               semantic != (int)RowSemanticPrompt.None;
    }

    private static bool TryScreen(nint tracked, out GhosttyPointCoordinate point)
    {
        point = default;
        return tracked != 0 && Native.TrackedGridRefPoint(tracked, PointTag.Screen, out point) == GhosttyResult.Success;
    }

    /// <summary>The command line: from where input started to where the output starts.</summary>
    private string? ReadCommandLine(CommandRefs command)
    {
        if (!TryScreen(command.Input, out var from) || !TryScreen(command.Output, out var to))
        {
            return null;
        }

        ushort cols = 0;
        Native.TerminalGet(_terminal, TerminalData.Cols, &cols);
        var last = to.X == 0 && to.Y > from.Y ? to.Y - 1 : to.Y;

        // Some prompts draw decoration after marking input (powerlevel10k's frame corner "─╯" at
        // the far right). Typing starts where the prompt ends, so text far from there, or across
        // a wide gap from the command (which commands rarely have), is decoration. Row by row: a
        // command of several lines (bash's "\" continuations, an open quote) has the rest of each
        // row blank, which is no gap in the command.
        const string Gap = "        ";
        var text = new System.Text.StringBuilder();
        var wrapped = false;
        for (var row = from.Y; row <= last && row - from.Y < 16; row++)
        {
            // Input cells only (a right-hand prompt, zsh's RPROMPT, shares the row; marked
            // continuation prompts, bash's "> ", are prompt), from where typing started.
            var read = RowText.Read(_terminal, PointTag.Screen, row, cols, inputOnly: true);
            var part = row == from.Y ? read.Text[read.IndexOf(Math.Min((int)from.X, cols))..] : read.Text;
            if (row == from.Y && part.StartsWith(Gap, StringComparison.Ordinal))
            {
                return null;
            }

            if (!wrapped)
            {
                part = part.TrimStart();
            }

            var cut = part.IndexOf(Gap, StringComparison.Ordinal);
            if (cut >= 0)
            {
                part = part[..cut];
            }

            // Across a soft wrap the row goes on; across a line break, a space.
            if (!wrapped && text.Length > 0 && part.Length > 0)
            {
                text.Append(' ');
            }

            wrapped = IsWrapped(row);
            text.Append(wrapped ? part : part.TrimEnd());
        }

        var result = text.ToString().Trim();
        return result.Length == 0 ? null : result.Length > 300 ? result[..300] : result;
    }

    /// <summary>Forgets commands that scrolled out of the scrollback, and the oldest beyond the limit.</summary>
    private void Prune()
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            if (_commands[i].Erased || !TryScreen(_commands[i].Prompt, out _))
            {
                _commands[i].Free();
                _commands.RemoveAt(i);
            }
        }

        while (_commands.Count > MaxCommands)
        {
            _commands[0].Free();
            _commands.RemoveAt(0);
        }
    }

    /// <summary>The shell's commands, oldest first (none if the shell doesn't mark them).</summary>
    public IReadOnlyList<ShellCommand> Commands()
    {
        lock (Lock)
        {
            return _disposed ? [] : _commandSnapshot ??= SnapshotCommands();
        }
    }

    private List<ShellCommand> SnapshotCommands()
    {
        var list = new List<ShellCommand>(_commands.Count);
        ushort cursorY = 0;
        Native.TerminalGet(_terminal, TerminalData.CursorY, &cursorY);
        nuint history = 0;
        Native.TerminalGet(_terminal, TerminalData.ScrollbackRows, &history);
        var cursorRow = (long)history + cursorY;

        for (var i = 0; i < _commands.Count; i++)
        {
            var c = _commands[i];
            if (!TryScreen(c.Prompt, out var prompt))
            {
                continue;
            }

            if (c.Erased)
            {
                continue; // its rows were erased: nothing of it to show
            }

            long? output = TryScreen(c.Output, out var o) ? o.Y : null;
            long lastRow;
            if (TryScreen(c.End, out var end))
            {
                lastRow = end.X == 0 && end.Y > prompt.Y ? end.Y - 1 : end.Y;
            }
            else if (i + 1 < _commands.Count && TryScreen(_commands[i + 1].Prompt, out var next))
            {
                lastRow = Math.Max(prompt.Y, (long)next.Y - 1);
            }
            else
            {
                lastRow = cursorRow;
            }

            var running = !c.Closed && c.Started != null;
            list.Add(new ShellCommand(prompt.Y, output, lastRow, c.Text, c.Exit, c.Started,
                c.Ended - c.Started, running));
        }

        return list;
    }

    /// <summary>
    /// Scrolls so the previous (<paramref name="direction"/> &lt; 0) or next prompt is at the top.
    /// False when the shell marks no prompts (the caller can scroll a line instead).
    /// </summary>
    public bool JumpToPrompt(int direction)
    {
        var commands = Commands();
        if (commands.Count == 0)
        {
            return false;
        }

        var offset = (long)GetScrollbar().Offset;
        long? target = direction < 0
            ? commands.Where(c => c.PromptRow < offset).Select(c => (long?)c.PromptRow).LastOrDefault()
            : commands.Where(c => c.PromptRow > offset).Select(c => (long?)c.PromptRow).FirstOrDefault();
        if (target is { } row)
        {
            ScrollToRow((ulong)row);
        }
        else if (direction > 0)
        {
            ScrollViewport(new GhosttyTerminalScrollViewport { Tag = ScrollViewportTag.Bottom });
        }

        return true;
    }

    /// <summary>
    /// The command to act on: the one at the top of the view when scrolled back, else the last
    /// one that ran.
    /// </summary>
    public ShellCommand? CommandInView()
    {
        var commands = Commands();
        var sb = GetScrollbar();
        var atBottom = sb.Offset + sb.Len >= sb.Total;
        if (!atBottom && commands.LastOrDefault(c => c.PromptRow <= (long)sb.Offset) is { } top)
        {
            return top;
        }

        return commands.LastOrDefault(c => c.Ran);
    }

    /// <summary>Selects a command's output (trailing blank rows left out); false if it has none.</summary>
    public bool SelectOutput(ShellCommand command)
    {
        if (command.OutputRow is not { } first || command.LastRow < first)
        {
            return false;
        }

        lock (Lock)
        {
            if (_disposed)
            {
                return false;
            }

            ushort cols = 0;
            Native.TerminalGet(_terminal, TerminalData.Cols, &cols);
            var last = command.LastRow;
            while (last >= first && RowText.Read(_terminal, PointTag.Screen, (uint)last, cols).Text.Trim().Length == 0)
            {
                last--;
            }

            if (last < first)
            {
                return false;
            }

            var selection = GhosttySelection.Init();
            selection.Start = GhosttyGridRef.Init();
            selection.End = GhosttyGridRef.Init();
            var start = new GhosttyPoint { Tag = PointTag.Screen, X = 0, Y = (uint)first };
            var stop = new GhosttyPoint { Tag = PointTag.Screen, X = (ushort)(cols - 1), Y = (uint)last };
            if (Native.TerminalGridRef(_terminal, start, &selection.Start) != GhosttyResult.Success ||
                Native.TerminalGridRef(_terminal, stop, &selection.End) != GhosttyResult.Success)
            {
                return false;
            }

            Native.TerminalSet(_terminal, TerminalOption.Selection, &selection);
            return true;
        }
    }

    /// <summary>Raises <see cref="CommandsChanged"/> if a mark arrived; call after the write, outside the lock.</summary>
    private void AnnounceCommands(bool changed)
    {
        if (changed)
        {
            CommandsChanged?.Invoke();
        }
    }

    private void FreeCommands()
    {
        foreach (var c in _commands)
        {
            c.Free();
        }

        _commands.Clear();
        _nested.Clear();
        _lastD = null;
    }
}
