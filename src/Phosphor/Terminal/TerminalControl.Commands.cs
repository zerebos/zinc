using Microsoft.Graphics.Canvas;
using Phosphor.Input;
using Windows.UI;

namespace Phosphor.Terminal;

// Shell integration in the pane: jumping between prompts, selecting a command's output, and the
// marks (scrollbar ticks for prompts, a bar beside failed commands). The session tracks the
// commands (TerminalSession.Commands.cs).
public sealed partial class TerminalControl
{
    private static readonly Color PromptTickColor = Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF);
    private static readonly Color FailedColor = Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B);
    private static readonly Color SucceededColor = Color.FromArgb(0x90, 0x3D, 0xDC, 0x97);

    /// <summary>The shell started or finished a command (or showed a prompt). Raised on the UI thread.</summary>
    public event Action? CommandsChanged;

    /// <summary>A command that was typed and run has finished (once per command). Raised on the UI thread.</summary>
    public event Action<ShellCommand>? CommandFinished;

    /// <summary>The command running in this pane, if the shell says one is.</summary>
    public ShellCommand? RunningCommand => _session?.Commands() is { Count: > 0 } commands && commands[^1].Running ? commands[^1] : null;

    private DateTime? _loggedCommand;

    private void OnCommandsChanged()
    {
        // PHOSPHOR_LOG: each finished command once (what shell integration saw).
        if (_session?.Commands() is { Count: > 0 } commands &&
            commands.LastOrDefault(c => c.Ran && !c.Running) is { Started: { } started } done && started != _loggedCommand)
        {
            _loggedCommand = started;
            Log.Write($"command: '{done.Text}' exit {done.ExitCode?.ToString() ?? "?"} in {done.Duration?.TotalSeconds:0.0}s, rows {done.PromptRow}-{done.LastRow}");
            CommandFinished?.Invoke(done);
        }

        CommandsChanged?.Invoke();
        _canvas.Invalidate();
    }

    private bool RunCommandAction(ActionInvocation invocation, TerminalSession session)
    {
        switch (invocation.Action)
        {
            case TerminalAction.JumpToPrompt:
                if (!session.JumpToPrompt(invocation.Index))
                {
                    session.ScrollLines(invocation.Index); // no shell integration: a line at a time
                }

                _canvas.Invalidate();
                return true;
            case TerminalAction.SelectOutput or TerminalAction.CopyOutput:
                if (session.CommandInView() is { } command && session.SelectOutput(command))
                {
                    if (invocation.Action == TerminalAction.CopyOutput)
                    {
                        CopySelection();
                    }

                    _canvas.Invalidate();
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>Scrollbar ticks at each prompt (red for failures) and bars beside failed commands.</summary>
    private void DrawCommandMarks(CanvasDrawingSession ds, TerminalSession session)
    {
        if (_config.CommandMarks == CommandMarks.None)
        {
            return;
        }

        var commands = session.Commands();
        if (commands.Count == 0)
        {
            return;
        }

        var sb = session.GetScrollbar();
        if (sb.Total > sb.Len)
        {
            var total = (float)sb.Total;
            var height = (float)_canvas.ActualHeight;
            var x = (float)_canvas.ActualWidth - 7;
            foreach (var command in commands)
            {
                var y = MathF.Round(height * command.PromptRow / total);
                ds.FillRectangle(x, y, 4, command.Failed ? 2 : 1, command.Failed ? FailedColor : PromptTickColor);
            }
        }

        var m = _renderer.Metrics;
        var top = (long)sb.Offset;
        var bottom = top + (long)sb.Len - 1;
        var barX = MathF.Max(0, (_config.PaddingLeft - 2) / 2);
        foreach (var command in commands)
        {
            var finished = command.Ran && !command.Running && command.ExitCode != null;
            if (!command.Failed && !(_config.CommandMarks == CommandMarks.All && finished))
            {
                continue;
            }

            var first = Math.Max(command.PromptRow, top);
            var last = Math.Min(command.LastRow, bottom);
            if (last < first)
            {
                continue;
            }

            ds.FillRectangle(barX, _config.PaddingTop + (first - top) * m.Height, 2, (last - first + 1) * m.Height,
                command.Failed ? FailedColor : SucceededColor);
        }
    }
}
