using Microsoft.Graphics.Canvas;
using Zinc.Input;
using Windows.UI;

namespace Zinc.Terminal;

// Shell integration in the pane: jumping between prompts, selecting a command's output, and the
// marks (scrollbar ticks for prompts, a bar beside failed commands). The session tracks the
// commands (TerminalSession.Commands.cs).
public sealed partial class TerminalControl
{
    // The marks sit on the terminal's own background, so they follow its lightness (a light scheme gets dark
    // ticks and the brand's darker red and green), not the window's.
    private bool LightTerminal => Contrast.IsLight(TerminalRenderer.ToColor(_config.Theme.Background));
    private Color PromptTickColor => LightTerminal ? Color.FromArgb(0x60, 0, 0, 0) : Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF);
    private Color FailedColor => Brand.ErrorFor(!LightTerminal);
    private Color SucceededColor
    {
        get
        {
            var green = Brand.Attention(!LightTerminal);
            return Color.FromArgb(0x90, green.R, green.G, green.B);
        }
    }

    /// <summary>The shell started or finished a command (or showed a prompt). Raised on the UI thread.</summary>
    public event Action? CommandsChanged;

    /// <summary>A command that was typed and run has finished (once per command). Raised on the UI thread.</summary>
    public event Action<ShellCommand>? CommandFinished;

    /// <summary>A program asked for a desktop notification (OSC 9 / 777): title (maybe empty), body. UI thread.</summary>
    public event Action<string, string>? Notified;

    /// <summary>A program's progress (OSC 9;4) changed; see <see cref="Progress"/>. UI thread.</summary>
    public event Action? ProgressChanged;

    /// <summary>What a program last reported as its progress.</summary>
    public TerminalProgress Progress => _session?.Progress ?? TerminalProgress.None;

    private void ForwardProgramNotifications(TerminalSession session)
    {
        var dq = DispatcherQueue;
        session.Notified += (title, body) => dq.SafeEnqueue(() =>
        {
            Log.Write($"program notification: '{title}' '{body}'");
            Notified?.Invoke(title, body);
        });
        session.ProgressChanged += progress => dq.SafeEnqueue(() => ProgressChanged?.Invoke());
    }

    /// <summary>
    /// A command line typed into the shell once it's ready ("New tab running…"), as if by hand:
    /// builtins, aliases and WSL programs all work, and the tab stays at the shell afterwards.
    /// Set before the pane starts.
    /// </summary>
    public string? StartupInput { get; set; }

    /// <summary>Adds variables to the program's environment as it starts (zn's: the terminal's id and the pipe).</summary>
    public Action<IDictionary<string, string?>>? LaunchEnvironment { get; set; }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _startupInputTimer;
    private long _lastOutputTicks, _startedTicks;

    /// <summary>
    /// Types <see cref="StartupInput"/> when the shell is ready: at its first prompt when it marks
    /// prompts (shell integration), else once its startup output has been quiet for a moment, or
    /// after a few seconds regardless. Shells keep what's typed early for when they read input.
    /// </summary>
    private void TypeStartupInputWhenReady(TerminalSession session)
    {
        if (StartupInput is not { Length: > 0 } line)
        {
            return;
        }

        StartupInput = null;
        _startedTicks = Environment.TickCount64;
        session.OutputProcessed += () => Interlocked.Exchange(ref _lastOutputTicks, Environment.TickCount64);
        _startupInputTimer = DispatcherQueue.CreateTimer();
        _startupInputTimer.Interval = TimeSpan.FromMilliseconds(150);
        _startupInputTimer.OnTick(timer =>
        {
            var now = Environment.TickCount64;
            var lastOutput = Interlocked.Read(ref _lastOutputTicks);
            var prompted = session.Commands().Count > 0;
            var quiet = lastOutput > 0 && now - lastOutput > 600;
            if (!prompted && !quiet && now - _startedTicks < 6000)
            {
                return;
            }

            timer.Stop();
            _startupInputTimer = null;
            if (_session == session && !session.HasExited)
            {
                Log.Write($"pane: typing the startup command ({(prompted ? "at the prompt" : quiet ? "output went quiet" : "timed out")})");
                session.SendInput(line + "\r");
            }
        });
        _startupInputTimer.Start();
    }

    /// <summary>The command running in this pane, if the shell says one is.</summary>
    public ShellCommand? RunningCommand => _session?.Commands() is { Count: > 0 } commands && commands[^1].Running ? commands[^1] : null;

    /// <summary>When each command announced as finished started (how they're told apart).</summary>
    private readonly HashSet<DateTime> _announcedCommands = [];

    private void OnCommandsChanged()
    {
        // Each finished command once, when the next prompt has started: only then is a D known to be
        // the command's own (the shell around a nested one reports how that one ended with a D its
        // "exit" would otherwise get), and a nested shell's command ends with the outer shell's prompt.
        if (_session?.Commands() is { Count: > 1 } commands)
        {
            for (var i = 0; i < commands.Count - 1; i++)
            {
                if (commands[i] is { Ran: true, Running: false, Started: { } started } done && _announcedCommands.Add(started))
                {
                    // ZINC_LOG: what shell integration saw.
                    Log.Write($"command: '{done.Text}' exit {done.ExitCode?.ToString() ?? "?"} in {done.Duration?.TotalSeconds:0.0}s, rows {done.PromptRow}-{done.LastRow}");
                    CommandFinished?.Invoke(done);
                }
            }

            if (_announcedCommands.Count > 4 * commands.Count + 100)
            {
                // Forget commands that scrolled away.
                _announcedCommands.IntersectWith(commands.Select(c => c.Started).OfType<DateTime>());
            }
        }

        CommandsChanged?.Invoke();
        Redraw();
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

                Redraw();
                return true;
            case TerminalAction.SelectOutput or TerminalAction.CopyOutput:
                if (session.CommandInView() is { } command && session.SelectOutput(command))
                {
                    if (invocation.Action == TerminalAction.CopyOutput)
                    {
                        CopySelection();
                    }

                    Redraw();
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
            var height = (float)Surface.ActualHeight;
            var x = (float)Surface.ActualWidth - 7;
            foreach (var command in commands)
            {
                var y = MathF.Round(height * command.PromptRow / total);
                ds.FillRectangle(x, y, 4, command.Failed ? 2 : 1, command.Failed ? FailedColor : PromptTickColor);
            }
        }

        var m = _renderer.Metrics;
        var top = (long)sb.Offset;
        var bottom = top + (long)sb.Len - 1;
        var barX = MathF.Max(0, (_gridLeft - 2) / 2);
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

            ds.FillRectangle(barX, _gridTop + (first - top) * m.Height, 2, (last - first + 1) * m.Height,
                command.Failed ? FailedColor : SucceededColor);
        }
    }
}
