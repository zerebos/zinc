using Phosphor.Ghostty;

namespace Phosphor.Terminal;

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

    /// <summary>Writes PTY output, noting the cursor at each OSC 133 mark. Hold the lock.</summary>
    private void WriteWithMarks(ReadOnlySpan<byte> data)
    {
        while (true)
        {
            var end = _markScanner.Next(data, out var mark);
            var chunk = end < 0 ? data : data[..end];
            fixed (byte* p = chunk)
            {
                Native.TerminalVtWrite(_terminal, p, (nuint)chunk.Length);
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

    private void OnShellMark(ShellMark mark)
    {
        int screen = 0;
        Native.TerminalGet(_terminal, TerminalData.ActiveScreen, &screen);
        if (screen != 0)
        {
            return; // full-screen apps' marks aren't commands
        }

        var last = _commands.Count > 0 && !_commands[^1].Closed ? _commands[^1] : null;
        switch (mark.Kind)
        {
            case 'A':
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
                break;
            default:
                return;
        }

        _commandsChanged = true;
        _commandSnapshot = null;
    }

    private nint? TrackCursor(bool atLineStart)
    {
        ushort x = 0, y = 0;
        Native.TerminalGet(_terminal, TerminalData.CursorX, &x);
        Native.TerminalGet(_terminal, TerminalData.CursorY, &y);
        var point = new GhosttyPoint { Tag = PointTag.Active, X = atLineStart ? (ushort)0 : x, Y = y };
        return Native.TerminalGridRefTrack(_terminal, point, out var tracked) == GhosttyResult.Success ? tracked : null;
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
        var text = new System.Text.StringBuilder();
        for (var row = from.Y; row <= last && row - from.Y < 16; row++)
        {
            // Input cells only (a right-hand prompt, zsh's RPROMPT, shares the row), from where
            // typing started.
            var read = RowText.Read(_terminal, PointTag.Screen, row, cols, inputOnly: true);
            text.Append(row == from.Y ? read.Text[read.IndexOf(Math.Min((int)from.X, cols))..] : read.Text);
        }

        // Some prompts draw decoration after marking input (powerlevel10k's frame corner "─╯" at
        // the far right). Typing starts where the prompt ends, so text far from there, or across
        // a wide gap from the command (which commands rarely have), is decoration.
        const string Gap = "        ";
        var line = text.ToString();
        if (line.StartsWith(Gap, StringComparison.Ordinal))
        {
            return null;
        }

        var cut = line.IndexOf(Gap, StringComparison.Ordinal);
        var result = (cut < 0 ? line : line[..cut]).Trim();
        return result.Length == 0 ? null : result.Length > 300 ? result[..300] : result;
    }

    /// <summary>Forgets commands that scrolled out of the scrollback, and the oldest beyond the limit.</summary>
    private void Prune()
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            if (!TryScreen(_commands[i].Prompt, out _))
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
    }
}
