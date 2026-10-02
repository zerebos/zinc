using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Phosphor.Ghostty;
using Phosphor.Input;

namespace Phosphor.Terminal;

/// <summary>
/// One terminal pane: a <see cref="TerminalSession"/> shown through a
/// <see cref="TerminalRenderer"/>, with keyboard (TerminalControl.Keyboard.cs)
/// and mouse (TerminalControl.Mouse.cs) input.
/// </summary>
public sealed partial class TerminalControl : UserControl
{
    private const float MinFontSize = 6, MaxFontSize = 72;

    private Config _config;
    private readonly Keybindings _keybindings;
    private readonly string _commandLine;
    private readonly string? _workingDirectory;
    private CanvasControl _canvas;
    private readonly Grid _root; // canvas, scrollbar, find box
    private readonly TerminalRenderer _renderer;
    private readonly DispatcherTimer _blinkTimer;

    private TerminalSession? _session;
    private SelectionGesture? _selection;
    private ushort _cols, _rows;
    private int _invalidatePending;
    private bool _blinkOn = true;
    private bool _focused;
    private bool _exited;

    // The window root whose scale changes we follow. Panes can move to another window.
    private XamlRoot? _xamlRoot;

    public event Action<string>? TitleChanged;

    /// <summary>The shell reported a new working directory (<see cref="ReportedDirectory"/>); on the UI thread.</summary>
    public event Action? DirectoryChanged;

    /// <summary>
    /// Handles bound actions the pane doesn't own (tabs, windows). Returns false to let
    /// the key through to the app.
    /// </summary>
    public Func<ActionInvocation, bool>? ActionHandler { get; set; }

    /// <summary>The last title the app set (OSC 0/2), or "" if none.</summary>
    /// <summary>The title the program set, or "" (also while <c>program-titles</c> is off).</summary>
    public string Title => _config.ProgramTitles ? _programTitle : "";

    private string _programTitle = "";

    /// <summary>The working directory the shell last reported, as sent (see <see cref="TerminalSession.ReportedDirectory"/>).</summary>
    public string? ReportedDirectory => _session?.ReportedDirectory;

    /// <summary>The terminal is done: the child exited cleanly, or the user dismissed a failure.</summary>
    public event Action? CloseRequested;

    public TerminalControl(Config config, string commandLine, string? workingDirectory)
        : this(config, Keybindings.Default, commandLine, workingDirectory)
    {
    }

    /// <summary>For a preview: what it shows instead of running a program (text with VT sequences).</summary>
    private string? _previewScript;

    /// <summary>
    /// A terminal that runs nothing and shows <paramref name="script"/> (the settings' preview):
    /// drawn like any pane, cursor as if focused, not interactive. <see cref="ApplyConfig"/> updates it.
    /// </summary>
    public static TerminalControl Preview(Config config, string script) => new(config, "", null)
    {
        _previewScript = script,
        IsTabStop = false,
        AllowDrop = false,
        IsHitTestVisible = false,
    };

    public TerminalControl(Config config, Keybindings keybindings, string commandLine, string? workingDirectory)
    {
        _config = config;
        _keybindings = keybindings;
        _commandLine = commandLine;
        _workingDirectory = workingDirectory;
        _renderer = new TerminalRenderer(config);

        IsTabStop = true;
        UseSystemFocusVisuals = false;
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        _arrowCursor = !config.TextPointer;
        _cursorShape = _arrowCursor ? InputSystemCursorShape.Arrow : InputSystemCursorShape.IBeam;
        ProtectedCursor = InputSystemCursor.Create(_cursorShape);
        // The scrollbar handles its own pointer events; proximity tracking must see them too.
        AddHandler(PointerMovedEvent, new PointerEventHandler(TrackScrollBarProximity), handledEventsToo: true);

        _canvas = CreateCanvas();
        EnableFileDrop();
        // The scrollbar overlays the canvas's right edge (inside the padding when idle).
        Content = _root = new Grid { Children = { _canvas, CreateScrollBar() } };

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            _canvas.Invalidate();
        };

        // Hosts like TabView unload hidden content and reload it when shown again, and
        // pane layouts re-parent panes, so the pane's lifetime is explicit (Close) rather
        // than tied to Unloaded, and focus is the host's decision (not taken on Loaded).
        Loaded += (_, _) =>
        {
            Log.Write($"pane: Loaded ({ActualWidth}x{ActualHeight})");
            if (XamlRoot != _xamlRoot)
            {
                // First load, or moved to another window (which may be on a monitor with a
                // different scale).
                if (_xamlRoot != null)
                {
                    _xamlRoot.Changed -= OnXamlRootChanged;
                }

                _xamlRoot = XamlRoot;
                _xamlRoot.Changed += OnXamlRootChanged;
                UpdateScale();
            }
        };
    }

    private float DisplayScale => (float)(XamlRoot?.RasterizationScale ?? 1.0);

    private CanvasControl CreateCanvas()
    {
        var canvas = CanvasLifetime.Create();
        canvas.ClearColor = ClearColor(_config);
        canvas.CreateResources += OnCreateResources;
        canvas.Draw += OnDraw;
        canvas.SizeChanged += OnCanvasSizeChanged;
        canvas.Loaded += OnCanvasLoaded;
        return canvas;
    }

    /// <summary>Done with a canvas: our handlers off (so it doesn't keep this pane alive), then retired.</summary>
    private void RetireCanvas(CanvasControl canvas)
    {
        canvas.CreateResources -= OnCreateResources;
        canvas.Draw -= OnDraw;
        canvas.SizeChanged -= OnCanvasSizeChanged;
        canvas.Loaded -= OnCanvasLoaded;
        CanvasLifetime.Retire(canvas);
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateGrid();

    /// <summary>
    /// Win2D draws a canvas only while its Loaded count exceeds its Unloaded count. XAML can
    /// report one removal twice (e.g. a terminal taken out of a pane that was itself just
    /// detached), after which the canvas never draws again — the pane looks frozen, even
    /// through resizes. If a Loaded leaves the count at zero or below, swap in a fresh canvas.
    /// The session, scrollback and everything else stay; only the drawing surface is replaced.
    /// The old canvas can't simply be dropped: see <see cref="CanvasLifetime"/>.
    /// </summary>
    private void OnCanvasLoaded(object sender, RoutedEventArgs e)
    {
        var loads = CanvasLifetime.LoadCount(_canvas);
        if (!ReferenceEquals(sender, _canvas) || loads > 0)
        {
            return;
        }

        Log.Write($"pane: canvas unloaded more often than loaded ({loads}); replacing it");
        var old = _canvas;
        _canvas = CreateCanvas();
        var index = _root.Children.IndexOf(old);
        _root.Children.RemoveAt(index);
        _root.Children.Insert(index, _canvas);
        old.RemoveFromVisualTree(); // releases its device resources
        RetireCanvas(old);
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateScale();

    // ---------------------------------------------------------------- setup

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        Log.Write($"pane: CreateResources ({args.Reason})");
        _renderer.CreateResources(sender);
        var size = _renderer.FontSize > 0 ? _renderer.FontSize : Math.Clamp(_config.FontSizeDip, MinFontSize, MaxFontSize);
        _renderer.SetFont(sender, size, DisplayScale);
        UpdateGrid();
    }

    private void UpdateScale()
    {
        if (_renderer.HasResources && Math.Abs(DisplayScale - _renderer.Metrics.Scale) > 0.001f)
        {
            _renderer.SetFont(_canvas, _renderer.FontSize, DisplayScale);
            UpdateGrid();
        }
    }

    private void SetFontSize(float size)
    {
        _renderer.SetFont(_canvas, Math.Clamp(size, MinFontSize, MaxFontSize), DisplayScale);
        UpdateGrid();
    }

    private void UpdateGrid()
    {
        var m = _renderer.Metrics;
        if (m.Width <= 0 || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0)
        {
            Log.Write($"pane: UpdateGrid skipped (cell {m.Width}, canvas {_canvas.ActualWidth}x{_canvas.ActualHeight})");
            return;
        }

        var cols = (ushort)Math.Clamp((int)((_canvas.ActualWidth - _config.PaddingLeft - _config.PaddingRight) / m.Width), 2, 1000);
        var rows = (ushort)Math.Clamp((int)((_canvas.ActualHeight - _config.PaddingTop - _config.PaddingBottom) / m.Height), 1, 1000);
        _cols = cols;
        _rows = rows;
        _renderer.EnsureColumns(cols);

        if (_session == null)
        {
            StartSession(cols, rows);
        }
        else
        {
            // Resize even when the grid is unchanged: the cell pixel size
            // (reported to apps via XTWINOPS/mouse pixels) may have changed.
            _session.Resize(cols, rows, m.WidthPx, m.HeightPx);
        }

        _canvas.Invalidate();
    }

    /// <summary>The canvas's color before its first frame: the background, as see-through as it's drawn.</summary>
    private static Windows.UI.Color ClearColor(Config config)
    {
        var c = TerminalRenderer.ToColor(config.Theme.Background);
        return Windows.UI.Color.FromArgb((byte)(255 * config.BackgroundOpacity), c.R, c.G, c.B);
    }

    /// <summary>A console Windows handed over (default terminal): attached instead of starting a program.</summary>
    public Phosphor.Pty.ConPty? HandedOver { get; set; }

    private void StartSession(ushort cols, ushort rows)
    {
        Log.Write($"pane: start {cols}x{rows}: {_commandLine}");
        var m = _renderer.Metrics;
        var session = new TerminalSession(cols, rows, _config.Theme);
        session.SetColorScheme(_config.IsDark);
        session.SetScrollback(_config.ScrollbackLines);
        session.SetCursorDefaults(_config.CursorStyle, _config.CursorBlink, _config.CursorStyleAtPrompt);
        session.CatchScreenFlash = _config.ScreenFlashBell;
        session.Resize(cols, rows, m.WidthPx, m.HeightPx);
        _selection = new SelectionGesture(session);
        _selection.SetWordBoundaries(SelectionGesture.Boundaries(_config.WordBoundaries));

        var dq = DispatcherQueue;
        session.OutputProcessed += RequestRedraw;
        // Synchronized output: keep the frame from when a program starts drawing one; if it never
        // finishes, redraw once the renderer's timeout has passed (no more output would ask).
        session.RenderHoldBegan = () => _renderer.CaptureHeldFrame(session);
        session.RenderHoldChanged += held => dq.SafeEnqueue(() =>
        {
            _holdTimer ??= CreateHoldTimer();
            _holdTimer.Stop();
            if (held)
            {
                _holdTimer.Start();
            }
        });
        // One notice until the title is taken (TerminalSession.TakeTitle): a program retitling itself on every
        // line costs one read per UI tick, not one per change.
        session.TitleChanged += () => dq.SafeEnqueue(() =>
        {
            if (session.TakeTitle() is not { } title)
            {
                return;
            }

            _programTitle = title;
            if (_config.ProgramTitles)
            {
                TitleChanged?.Invoke(title);
            }
        });
        session.Bell += () => dq.SafeEnqueue(OnBell);
        session.DirectoryChanged += () => dq.SafeEnqueue(() => DirectoryChanged?.Invoke());
        session.CommandsChanged += () => dq.SafeEnqueue(OnCommandsChanged);
        ForwardProgramNotifications(session);
        session.ProcessExited += code => dq.SafeEnqueue(() => OnProcessExited(code));
        ApplyClipboardPermissions(session);
        _session = session;
        if (_previewScript is { } script)
        {
            session.WriteToScreen(script);
            return;
        }

        if (HandedOver is { } handedOver)
        {
            HandedOver = null;
            Log.Write($"pane: a console handed over: {_commandLine} (pid {handedOver.ProcessId})");
            session.Attach(handedOver);
            session.Resize(cols, rows, m.WidthPx, m.HeightPx); // its program learns the pane's size
            return;
        }

        try
        {
            var launch = new ShellLaunch(_commandLine, new Dictionary<string, string?>());
            var scripts = Path.Combine(AppContext.BaseDirectory, ShellIntegration.FolderName);
            if (_config.ShellIntegration == ShellIntegrationMode.Auto && Directory.Exists(scripts))
            {
                launch = ShellIntegration.Prepare(_commandLine, scripts, Environment.GetEnvironmentVariable);
                if (launch.CommandLine != _commandLine || launch.Environment.Count > 0)
                {
                    Log.Write($"pane: shell integration: {launch.CommandLine} (+{launch.Environment.Count} variables)");
                }
            }

            session.Start(launch.CommandLine, _workingDirectory, cols, rows, launch.Environment);
            TypeStartupInputWhenReady(session);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // Nothing to run (a shell builtin like `dir`, a typo, a missing program): say so in the
            // tab, which then closes on a key like a failed session, rather than take the app down.
            Log.Write($"pane: couldn't start {_commandLine}: {ex.Message}");
            _exited = true;
            session.WriteToScreen($"\x1b[0;31mCouldn't start {_commandLine}\x1b[0m\r\n{ex.Message}\r\n\r\n" +
                "\x1b[0;2mShell commands (dir, ls, cd) need a shell: run the shell and type them there. " +
                "Press any key to close.\x1b[0m");
        }
    }

    /// <summary>OSC 52 access per the settings (null = denied).</summary>
    private void ApplyClipboardPermissions(TerminalSession session)
    {
        // Writes arrive mid-parse on the reader thread: set the clipboard on the UI thread.
        var dq = DispatcherQueue;
        session.ClipboardWriter = _config.ClipboardWrite ? text => dq.SafeEnqueue(() => ClipboardText.Set(text)) : null;
        session.ClipboardReader = _config.ClipboardRead != ClipboardAccess.Deny ? ClipboardText.ReadNow : null;
        session.ClipboardConsent = _config.ClipboardRead == ClipboardAccess.Ask ? AskToReadClipboard : null;
    }

    /// <summary>
    /// Takes new settings (a config reload) without restarting the shell: colors, font,
    /// padding, pointer and clipboard permissions. A font size changed in the config replaces
    /// the current zoom; otherwise the zoom (Ctrl+plus/minus) stays.
    /// </summary>
    public void ApplyConfig(Config config)
    {
        var old = _config;
        _config = config;
        _renderer.Config = config;
        _canvas.ClearColor = ClearColor(config);
        if (_session is { } session)
        {
            session.SetTheme(config.Theme);
            session.SetColorScheme(config.IsDark); // tells programs that asked to hear of it
            ApplyClipboardPermissions(session);
            session.CatchScreenFlash = config.ScreenFlashBell;
            if (old.ScrollbackLines != config.ScrollbackLines)
            {
                session.SetScrollback(config.ScrollbackLines);
            }

            if (old.CursorStyle != config.CursorStyle || old.CursorBlink != config.CursorBlink || old.CursorStyleAtPrompt != config.CursorStyleAtPrompt)
            {
                session.SetCursorDefaults(config.CursorStyle, config.CursorBlink, config.CursorStyleAtPrompt);
            }
        }

        if (old.ProgramTitles != config.ProgramTitles && _programTitle.Length > 0)
        {
            TitleChanged?.Invoke(Title);
        }

        if (old.WordBoundaries != config.WordBoundaries)
        {
            _selection?.SetWordBoundaries(SelectionGesture.Boundaries(config.WordBoundaries));
        }

        if (_renderer.HasResources)
        {
            if (old.FontFamily != config.FontFamily || old.FontSize != config.FontSize)
            {
                var size = old.FontSize != config.FontSize ? ZoomedFontSize() : _renderer.FontSize;
                _renderer.SetFont(_canvas, size, DisplayScale);
            }

            UpdateGrid(); // padding may have changed
        }

        _canvas.Invalidate();
    }

    private void OnProcessExited(uint code)
    {
        if (_session == null)
        {
            // Already closed (e.g. the window was closed, which killed the child).
            return;
        }

        if (_config.CloseOnExit == CloseOnExit.Always || (code == 0 && _config.CloseOnExit == CloseOnExit.Success))
        {
            CloseRequested?.Invoke();
            return;
        }

        // Like Windows Terminal: keep a failed session on screen so its output can be read.
        _exited = true;
        _session.WriteToScreen(code == 0
            ? "\r\n\x1b[0;2m[process exited; press any key to close]\x1b[0m"
            : $"\r\n\x1b[0;2m[process exited with code {code} (0x{code:X8}); press any key to close]\x1b[0m");
    }

    // Held in a field: a timer only a local references is collected and stops firing.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _holdTimer;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateHoldTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(TerminalRenderer.RenderHoldTimeoutMs + 50);
        timer.IsRepeating = false;
        timer.OnTick(_ => _canvas.Invalidate());
        return timer;
    }

    private void RequestRedraw()
    {
        // Coalesce bursts of PTY output into a single invalidate per UI tick.
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 0)
        {
            DispatcherQueue.SafeEnqueue(() =>
            {
                Volatile.Write(ref _invalidatePending, 0);
                _canvas.Invalidate();
                ScheduleSearchRefresh();
            });
        }
    }

    /// <summary>The whole screen including scrollback as plain text ("" before the session starts).</summary>
    public string ReadText() => _session?.ReadScreenText() ?? "";

    /// <summary>Ends the session (killing the child if still running) and frees resources.</summary>
    public void Close()
    {
        _closing.Cancel(); // a clipboard question still open: the answer is no
        _blinkTimer.Stop();
        var session = _session;
        if (session == null)
        {
            return;
        }

        _session = null;
        // The gesture holds references into the terminal: free it first.
        _selection?.Dispose();
        _selection = null;
        session.Dispose();
        _renderer.Dispose();
        // Still in the tree here (its Unloaded comes later); CanvasLifetime holds it until
        // Win2D has let go of the window.
        RetireCanvas(_canvas);
    }

    // ------------------------------------------------------------ rendering

    private FramePerf? _perf;

    /// <summary>
    /// A terminal for the drawing benchmark: runs nothing (its workloads arrive through
    /// <see cref="WriteForBenchmark"/>), measures every frame, draws with the processor when
    /// <paramref name="software"/>.
    /// </summary>
    public static TerminalControl Benchmark(Config config, bool software)
    {
        var shared = CanvasLifetime.SoftwareRendering;
        CanvasLifetime.SoftwareRendering = software; // read once, by the canvas the constructor makes
        try
        {
            var control = Preview(config, "");
            control._perf = new FramePerf();
            return control;
        }
        finally
        {
            CanvasLifetime.SoftwareRendering = shared;
        }
    }

    /// <summary>The benchmark's grid once its session has started (columns, rows), else null.</summary>
    public (int Cols, int Rows)? BenchmarkGrid => _session != null ? (_cols, _rows) : null;

    /// <summary>The benchmark pane's frame timer.</summary>
    internal FramePerf? BenchmarkPerf => _perf;

    /// <summary>Output for the benchmark pane, as its program would write it (any thread).</summary>
    public void WriteForBenchmark(ReadOnlySpan<byte> output) => _session?.WriteToScreen(output);

    /// <summary>Frames that failed to draw (only the first is logged).</summary>
    private int _drawFailures;

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        // An exception leaving the Draw event leaves Win2D's drawing session open, and every frame
        // after fails ("The last drawing session ... must be disposed"): a pane that never draws
        // again. Catching it costs this frame only.
        try
        {
            DrawFrame(args);
        }
        catch (Exception ex)
        {
            if (_drawFailures++ == 0)
            {
                Log.Crash("draw", ex);
            }
        }
    }

    private void DrawFrame(CanvasDrawEventArgs args)
    {
        if (_session is not { } session)
        {
            return;
        }

        // Test captures draw every cursor as focused: whether a new window gets focus varies.
        var presentation = new CursorPresentation(_focused || TestHooks.Deterministic || _previewScript != null, _blinkOn);
        var perf = _perf ??= FramePerf.LogEnabled ? new FramePerf { LogsEverySecond = true } : null;
        _renderer.Perf = perf;
        var started = perf != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var blinking = _renderer.Draw(args.DrawingSession, session, _rows, presentation);
        PlaceInputBox(); // follows the cursor
        DrawCommandMarks(args.DrawingSession, session);
        DrawSearchTicks(args.DrawingSession, session);
        if (perf != null)
        {
            var drawn = System.Diagnostics.Stopwatch.GetTimestamp();
            args.DrawingSession.Flush(); // rasterize now, to time it (else it happens after this returns)
            perf.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started, drawn).TotalMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(drawn).TotalMilliseconds);
        }
        UpdateBlinkTimer(blinking);
        UpdateScrollBar(session);
    }

    private void UpdateBlinkTimer(bool blinking)
    {
        if (blinking && _focused && !TestHooks.Deterministic)
        {
            if (!_blinkTimer.IsEnabled)
            {
                _blinkOn = true;
                _blinkTimer.Start();
            }
        }
        else if (_blinkTimer.IsEnabled)
        {
            _blinkTimer.Stop();
            _blinkOn = true;
        }
    }

    private void ResetBlink()
    {
        _blinkOn = true;
        if (_blinkTimer.IsEnabled)
        {
            _blinkTimer.Stop();
            _blinkTimer.Start();
        }
    }

    // ---------------------------------------------------------------- focus

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        // With input methods on, typing goes through the pane's input box: focus lands there.
        if (ReferenceEquals(e.OriginalSource, this) && FocusInputMethodBox())
        {
            return;
        }

        NoteForeignKeys(); // a key held as focus arrives (Enter in a popup, Escape in the find box)
        if (_focused)
        {
            return; // from the pane to its input box: still the same focus for the program
        }

        _focused = true;
        ResetBlink();
        SendFocusEvent(true);
        _canvas.Invalidate();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        if (FocusIsWithin())
        {
            return; // still inside the pane: not a focus change for the program
        }

        _focused = false;
        CancelSequence();
        SendFocusEvent(false);
        _canvas.Invalidate();
    }

    /// <summary>Whether keyboard focus is on the pane itself or its input box (the find box counts as elsewhere, as before).</summary>
    private bool FocusIsWithin() =>
        XamlRoot != null && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is { } focused &&
        (ReferenceEquals(focused, this) || ReferenceEquals(focused, _inputBox));

    private void SendFocusEvent(bool focused)
    {
        if (_session is { HasExited: false } session && session.GetMode(GhosttyMode.FocusEvent))
        {
            session.SendInput(focused ? "\x1b[I"u8 : "\x1b[O"u8, scrollToBottom: false);
        }
    }
}
