using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Phantom.Ghostty;
using Phantom.Input;

namespace Phantom.Terminal;

/// <summary>
/// One terminal pane: a <see cref="TerminalSession"/> shown through a
/// <see cref="TerminalRenderer"/>, with keyboard (TerminalControl.Keyboard.cs)
/// and mouse (TerminalControl.Mouse.cs) input.
/// </summary>
public sealed partial class TerminalControl : UserControl
{
    private const float MinFontSize = 6, MaxFontSize = 72;

    private readonly Config _config;
    private readonly Keybindings _keybindings;
    private readonly string _commandLine;
    private readonly string? _workingDirectory;
    private readonly CanvasControl _canvas;
    private readonly TerminalRenderer _renderer;
    private readonly DispatcherTimer _blinkTimer;

    private TerminalSession? _session;
    private SelectionGesture? _selection;
    private ushort _cols, _rows;
    private int _invalidatePending;
    private bool _blinkOn = true;
    private bool _focused;
    private bool _exited;

    private bool _subscribedToXamlRoot;

    public event Action<string>? TitleChanged;

    /// <summary>
    /// Handles bound actions the pane doesn't own (tabs, windows). Returns false to let
    /// the key through to the app.
    /// </summary>
    public Func<ActionInvocation, bool>? ActionHandler { get; set; }

    /// <summary>The last title the app set (OSC 0/2), or "" if none.</summary>
    public string Title { get; private set; } = "";

    /// <summary>The terminal is done: the child exited cleanly, or the user dismissed a failure.</summary>
    public event Action? CloseRequested;

    public TerminalControl(Config config, string commandLine, string? workingDirectory)
        : this(config, Keybindings.Default, commandLine, workingDirectory)
    {
    }

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

        _canvas = new CanvasControl { ClearColor = TerminalRenderer.ToColor(config.Theme.Background) };
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        _canvas.SizeChanged += (_, _) => UpdateGrid();
        // The scrollbar overlays the canvas's right edge (inside the padding when idle).
        Content = new Grid { Children = { _canvas, CreateScrollBar() } };

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
            if (!_subscribedToXamlRoot)
            {
                _subscribedToXamlRoot = true;
                XamlRoot.Changed += (_, _) => UpdateScale();
            }
        };
    }

    private float DisplayScale => (float)(XamlRoot?.RasterizationScale ?? 1.0);

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

    private void StartSession(ushort cols, ushort rows)
    {
        Log.Write($"pane: start {cols}x{rows}: {_commandLine}");
        var m = _renderer.Metrics;
        var session = new TerminalSession(cols, rows, _config.Theme);
        session.Resize(cols, rows, m.WidthPx, m.HeightPx);
        _selection = new SelectionGesture(session);

        var dq = DispatcherQueue;
        session.OutputProcessed += RequestRedraw;
        session.TitleChanged += title => dq.TryEnqueue(() =>
        {
            Title = title;
            TitleChanged?.Invoke(title);
        });
        session.ProcessExited += code => dq.TryEnqueue(() => OnProcessExited(code));
        // OSC 52. Writes arrive mid-parse on the reader thread: set the clipboard on the UI thread.
        if (_config.ClipboardWrite)
        {
            session.ClipboardWriter = text => dq.TryEnqueue(() => ClipboardText.Set(text));
        }

        if (_config.ClipboardRead)
        {
            session.ClipboardReader = ClipboardText.ReadNow;
        }

        _session = session;
        session.Start(_commandLine, _workingDirectory, cols, rows);
    }

    private void OnProcessExited(uint code)
    {
        if (_session == null)
        {
            // Already closed (e.g. the window was closed, which killed the child).
            return;
        }

        if (code == 0)
        {
            CloseRequested?.Invoke();
            return;
        }

        // Like Windows Terminal: keep a failed session on screen so its output can be read.
        _exited = true;
        _session.WriteToScreen($"\r\n\x1b[0;2m[process exited with code {code} (0x{code:X8}); press any key to close]\x1b[0m");
    }

    private void RequestRedraw()
    {
        // Coalesce bursts of PTY output into a single invalidate per UI tick.
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Volatile.Write(ref _invalidatePending, 0);
                _canvas.Invalidate();
            });
        }
    }

    /// <summary>The whole screen including scrollback as plain text ("" before the session starts).</summary>
    public string ReadText() => _session?.ReadScreenText() ?? "";

    /// <summary>Ends the session (killing the child if still running) and frees resources.</summary>
    public void Close()
    {
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
    }

    // ------------------------------------------------------------ rendering

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_session is not { } session)
        {
            return;
        }

        var blinking = _renderer.Draw(args.DrawingSession, session, _rows, new CursorPresentation(_focused, _blinkOn));
        UpdateBlinkTimer(blinking);
        UpdateScrollBar(session);
    }

    private void UpdateBlinkTimer(bool blinking)
    {
        if (blinking && _focused)
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
        _focused = true;
        ResetBlink();
        SendFocusEvent(true);
        _canvas.Invalidate();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        _focused = false;
        SendFocusEvent(false);
        _canvas.Invalidate();
    }

    private void SendFocusEvent(bool focused)
    {
        if (_session is { HasExited: false } session && session.GetMode(GhosttyMode.FocusEvent))
        {
            session.SendInput(focused ? "\x1b[I"u8 : "\x1b[O"u8, scrollToBottom: false);
        }
    }
}
