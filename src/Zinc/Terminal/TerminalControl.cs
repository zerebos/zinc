using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Zinc.Ghostty;
using Zinc.Input;

namespace Zinc.Terminal;

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
    // What the pane draws on: a CanvasControl, or (low-latency-drawing) a swap chain of its own. One of the two.
    private CanvasControl? _canvas;
    private readonly SwapChainSurface? _swap;
    private readonly Grid _root; // canvas, scrollbar, find box

    /// <summary>The drawing surface as an element: its size, and where pointer positions are measured from.</summary>
    private FrameworkElement Surface => _swap?.Panel ?? (FrameworkElement)_canvas!;

    /// <summary>What device-bound resources (fonts, text layouts) are made with.</summary>
    private Microsoft.Graphics.Canvas.ICanvasResourceCreator Creator => _swap?.Device ?? (Microsoft.Graphics.Canvas.ICanvasResourceCreator)_canvas!;

    private Microsoft.UI.Xaml.Controls.Image? _still;

    /// <summary>
    /// Shows the terminal as a still picture (true) instead of its swap chain, until false. This is for a pane that another
    /// one grows over during maximize or restore, because XAML doesn't reliably order swap chains against each other, so a
    /// covered pane's terminal can draw over the growing one. A picture is ordinary XAML content. It's drawn once, shaders
    /// included (an animated one holds still meanwhile).
    /// </summary>
    public void ShowStill(bool still)
    {
        if (_swap is not { } swap)
        {
            return;
        }

        if (!still)
        {
            if (_still != null)
            {
                _root.Children.Remove(_still);
                _still = null;
                swap.Panel.Opacity = 1;
            }

            return;
        }

        // A pane coming back from behind a maximized one is still collapsed until layout runs, so it uses the size it
        // last had.
        var (width, height) = swap.Panel.ActualWidth > 0 && swap.Panel.ActualHeight > 0
            ? ((float)swap.Panel.ActualWidth, (float)swap.Panel.ActualHeight)
            : ((float)LaidOutSize.Width, (float)LaidOutSize.Height);
        if (_still != null || _closing.IsCancellationRequested || width <= 0 || height <= 0)
        {
            return;
        }

        try
        {
            var picture = new Microsoft.Graphics.Canvas.UI.Xaml.CanvasImageSource(swap.Device, width, height, 96 * DisplayScale,
                Microsoft.Graphics.Canvas.CanvasAlphaMode.Premultiplied);
            using (var ds = picture.CreateDrawingSession(Microsoft.UI.Colors.Transparent))
            using (var frame = swap.Picture())
            {
                if (frame != null)
                {
                    ds.DrawImage(frame, new Windows.Foundation.Rect(0, 0, width, height));
                }
                else
                {
                    ds.Clear(swap.ClearColor);
                    DrawSafely(ds, swap.Device);
                }
            }

            _still = new Microsoft.UI.Xaml.Controls.Image
            {
                Source = picture,
                Width = width,
                Height = height,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            };
            _root.Children.Insert(_root.Children.IndexOf(swap.Panel) + 1, _still);
            swap.Panel.Opacity = 0;
        }
        catch (Exception ex)
        {
            Log.Write($"pane: still picture failed ({ex.HResult:X8}): {ex.Message}");
        }
    }

    /// <summary>
    /// Its window was hidden or minimized (true), or shown again. While it's hidden, no frames are drawn, and the swap
    /// chain's memory is freed, as a hidden tab's is. So the cursor's blink and Kitty animations stop, and the first
    /// frame drawn after the window shows again starts them again.
    /// </summary>
    public void SetWindowHidden(bool hidden)
    {
        _swap?.SetWindowHidden(hidden);
        if (hidden)
        {
            _blinkTimer.Stop();
            _blinkOn = true;
            _animationTimer?.Stop();
        }
        else
        {
            Redraw();
        }
    }

    /// <summary>Asks for a new frame.</summary>
    private void Redraw()
    {
        if (_swap != null)
        {
            _swap.Invalidate();
        }
        else
        {
            _canvas!.Invalidate();
        }
    }
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

    /// <summary>
    /// The shell reported a new working directory (<see cref="ReportedDirectory"/>). Raised on the UI thread.
    /// </summary>
    public event Action? DirectoryChanged;

    /// <summary>
    /// Handles bound actions the pane doesn't own (tabs, windows). Returns false to let
    /// the key through to the app.
    /// </summary>
    public Func<ActionInvocation, bool>? ActionHandler { get; set; }

    /// <summary>
    /// What the tab is called (<see cref="ShellTitle"/>). With <c>shell-titles</c>, it's the folder at the prompt and the
    /// command while one runs. Otherwise it's the title the program set, or "" for the profile's name.
    /// </summary>
    public string Title => ShellTitle.Choose(_config.ShellTitles, _config.ProgramTitles, _session?.AtPrompt, CurrentFolder,
        RunningCommand?.Text, RunningCommand?.Started, DateTime.UtcNow, _programTitle, _programTitleSinceCommand, UserHome);

    /// <summary>Whether <see cref="Title"/> is the shell's folder, which a tab label then shortens from its start.</summary>
    public bool TitleIsFolder => ShellTitle.ChoosesFolder(_config.ShellTitles, _config.ProgramTitles, _session?.AtPrompt, CurrentFolder,
        RunningCommand?.Text, RunningCommand?.Started, DateTime.UtcNow, _programTitle, _programTitleSinceCommand);

    /// <summary>A one-tab pane header's title (<see cref="ShellTitle.Header"/>): what leads, and the quieter text after it.</summary>
    public HeaderTitle HeaderTitle(string profileName) => ShellTitle.Header(_config.ProgramTitles, _session?.AtPrompt, CurrentFolder,
        RunningCommand?.Text, RunningCommand?.Started, DateTime.UtcNow, _programTitle, _programTitleSinceCommand, UserHome, profileName);

    /// <summary>The shell's folder to show after <paramref name="title"/> (<see cref="ShellTitle.FolderAfter"/>), if any.</summary>
    public string? FolderAfter(string title) => CurrentFolder is { } folder ? ShellTitle.FolderAfter(title, folder, UserHome) : null;

    /// <summary><see cref="HeaderTitle"/> changed (an input, or a running command reaching the time it leads).</summary>
    public event Action? HeaderTitleChanged;

    private HeaderTitle? _shownHeaderTitle;

    // Held in a field, because a timer that only a local variable references is collected and stops firing.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _headerTimer;

    private static readonly string UserHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private string _programTitle = "";

    /// <summary>The program's title was set while the running command ran (vim's own name wins over the command's).</summary>
    private bool _programTitleSinceCommand;

    /// <summary>When the command the title knows of started (a new one forgets the program's title).</summary>
    private DateTime? _titledCommand;

    private string? _shownTitle;

    /// <summary>Tells the app when <see cref="Title"/> changed (any of its inputs: the program, the marks, the folder, settings).</summary>
    private void NoteTitle()
    {
        var running = RunningCommand?.Started;
        if (running != _titledCommand)
        {
            _titledCommand = running;
            _programTitleSinceCommand = false;
        }

        var title = Title;
        if (title != _shownTitle)
        {
            _shownTitle = title;
            TitleChanged?.Invoke(title);
        }

        NoteHeaderTitle(running);
    }

    /// <summary>
    /// Tells the app when <see cref="HeaderTitle"/> changed. Once a running command has run long enough to lead, it
    /// checks again through NoteTitle, so the tab's title and the header both update. Headers are compared without the
    /// profile's name, which only a config reload changes.
    /// </summary>
    private void NoteHeaderTitle(DateTime? running)
    {
        _headerTimer?.Stop();
        if (ShellTitle.UntilCommandLeads(running, DateTime.UtcNow) is { } left && left > TimeSpan.Zero)
        {
            if (_headerTimer == null)
            {
                _headerTimer = DispatcherQueue.CreateTimer();
                _headerTimer.IsRepeating = false;
                _headerTimer.OnTick(_ => NoteTitle());
            }

            _headerTimer.Interval = left + TimeSpan.FromMilliseconds(20); // past the mark, not just short of it
            _headerTimer.Start();
        }

        var header = HeaderTitle("");
        if (header != _shownHeaderTitle)
        {
            _shownHeaderTitle = header;
            HeaderTitleChanged?.Invoke();
        }
    }

    /// <summary>The working directory the shell last reported, as sent (see <see cref="TerminalSession.ReportedDirectory"/>).</summary>
    public string? ReportedDirectory => _session?.ReportedDirectory;

    /// <summary>
    /// Where the shell is, for showing (title, tab switcher, command center, zn). It's the reported folder, but null while
    /// the shell is on another machine (ssh). What decides where new terminals start uses
    /// <see cref="ReportedDirectory"/>, which keeps the folder you ran ssh from.
    /// </summary>
    public string? CurrentFolder => _session is { RemoteHost: null } session ? ShellDirectory.Parse(session.ReportedDirectory) : null;

    /// <summary>The terminal is done: the child exited cleanly, or the user dismissed a failure.</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// The program ended and the pane stays (close-on-exit kept it), with its exit code. Raised on the UI thread.
    /// </summary>
    public event Action<uint>? ExitedAndKept;

    /// <summary>
    /// The ended program's exit code while the pane stays, or null while it runs (or if it never started).
    /// </summary>
    public uint? ExitCode { get; private set; }

    public TerminalControl(Config config, string commandLine, string? workingDirectory)
        : this(config, Keybindings.Default, commandLine, workingDirectory)
    {
    }

    /// <summary>For a preview: what it shows instead of running a program (text with VT sequences).</summary>
    private string? _previewScript;

    /// <summary>
    /// A preview's script was laid out at its grid, with the height (DIPs) it needs to show all of it, padding included. It
    /// can't be scrolled, so taller rows (line height) or a narrow window, which wrap or push its lines up, tell the
    /// host to grow it.
    /// </summary>
    public event Action<double>? PreviewHeightNeeded;

    /// <summary>Shows the preview's script afresh at the grid's size (resizing reflowed it into the scrollback).</summary>
    private void ReplayPreview(TerminalSession session, string script)
    {
        session.WriteToScreen("\e[H\e[2J\e[3J"); // home, clear the screen, clear the scrollback
        session.WriteToScreen(script);
        var m = _renderer.Metrics;
        PreviewHeightNeeded?.Invoke(session.RowsUsed() * m.Height + _config.PaddingTop + _config.PaddingBottom + 1);
    }

    /// <summary>
    /// A terminal that runs nothing and shows <paramref name="script"/> (the settings' preview).
    /// It's drawn like any pane, with the cursor as if focused, and isn't interactive. <see cref="ApplyConfig"/> updates it.
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

        if (SwapChainSurface.Enabled)
        {
            _swap = new SwapChainSurface(CanvasLifetime.SoftwareRendering, creator => CreateResourcesOn(creator, "swap chain"),
                ds => DrawSafely(ds, _swap!.Device), () => DisplayScale,
                () => System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref _lastOutputAt)).TotalMilliseconds) { ClearColor = ClearColor(config) };
            _swap.Panel.SizeChanged += OnCanvasSizeChanged;
        }
        else
        {
            _canvas = CreateCanvas();
        }

        StartShaders();

        EnableFileDrop();
        // The scrollbar overlays the canvas's right edge (inside the padding when idle).
        Content = _root = new Grid { Children = { Surface, CreateScrollBar() } };
        if (_swap != null)
        {
            // Hit-testable for the pointer (a swap chain's panel can't have a background of its own).
            _root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            Redraw();
        };

        // Hosts like TabView unload hidden content and reload it when shown again, and
        // pane layouts re-parent panes, so the pane's lifetime is explicit (Close) rather
        // than tied to Unloaded, and focus is the host's decision (not taken on Loaded).
        Loaded += (_, _) =>
        {
            Log.Write($"pane: Loaded ({ActualWidth}x{ActualHeight})");
            if (_closing.IsCancellationRequested)
            {
                return;
            }

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
            }

            // Every load, not just a new window's, because a tab detached from the tree while its window changed
            // monitors misses the change. This is cheap when nothing changed.
            UpdateScale();
        };
    }

    // Out of the tree (a group's hidden tab) there's no XamlRoot, so the pane keeps the scale it has rather than falling
    // to 100%.
    private float DisplayScale => (float)(XamlRoot?.RasterizationScale ?? (_renderer.Metrics.Scale > 0 ? _renderer.Metrics.Scale : 1.0));

    private CanvasControl CreateCanvas()
    {
        var canvas = CanvasLifetime.Create();
        canvas.ClearColor = ClearColor(_config);
        canvas.CreateResources += OnCreateResources;
        canvas.Draw += OnDraw;
        canvas.SizeChanged += OnCanvasSizeChanged;
        canvas.Loaded += OnCanvasLoaded;
        canvas.Unloaded += OnCanvasUnloaded;
        return canvas;
    }

    /// <summary>
    /// Done with a canvas, so our handlers come off it (so it doesn't keep this pane alive), and then it's retired.
    /// </summary>
    private void RetireCanvas(CanvasControl canvas)
    {
        canvas.CreateResources -= OnCreateResources;
        canvas.Draw -= OnDraw;
        canvas.SizeChanged -= OnCanvasSizeChanged;
        canvas.Loaded -= OnCanvasLoaded;
        canvas.Unloaded -= OnCanvasUnloaded;
        CanvasLifetime.Retire(canvas);
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateGrid();

    /// <summary>
    /// Win2D draws a canvas only while its Loaded count exceeds its Unloaded count. XAML can
    /// report one removal twice (e.g. a terminal taken out of a pane that was itself just
    /// detached), after which the canvas never draws again, so the pane looks frozen, even
    /// through resizes. If a Loaded leaves the count at zero or below, swap in a fresh canvas.
    /// The session, scrollback and everything else stay, and only the drawing surface is replaced.
    /// The old canvas can't simply be dropped (see <see cref="CanvasLifetime"/>).
    /// </summary>
    private void OnCanvasLoaded(object sender, RoutedEventArgs e)
    {
        if (_canvas == null)
        {
            return;
        }

        var loads = CanvasLifetime.LoadCount(_canvas);
        if (!ReferenceEquals(sender, _canvas) || loads > 0)
        {
            return;
        }

        ReplaceCanvas($"canvas unloaded more often than loaded ({loads})");
    }

    /// <summary>
    /// The other way a canvas stops is an extra Unloaded while it stays on screen, so no Loaded follows to catch it above.
    /// For example, when the shown tab of a restored pane closes, the tab behind it (not drawn since the restore) can get
    /// a Loaded and an Unloaded as the pane is rebuilt. This is checked once the layout has settled, and a canvas still in
    /// the window with Win2D's count at zero is stuck.
    /// </summary>
    private void OnCanvasUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not CanvasControl canvas || !ReferenceEquals(canvas, _canvas) || CanvasLifetime.LoadCount(canvas) > 0)
        {
            return;
        }

        DispatcherQueue.SafeEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ReferenceEquals(canvas, _canvas) && _session != null && CanvasLifetime.LoadCount(canvas) <= 0 && InWindow(canvas))
            {
                ReplaceCanvas($"canvas unloaded while still shown ({CanvasLifetime.LoadCount(canvas)})");
            }
        });
    }

    /// <summary>Whether <paramref name="element"/> is in its window's live tree (its parents lead up to the window's content).</summary>
    private static bool InWindow(DependencyObject element)
    {
        if ((element as UIElement)?.XamlRoot?.Content is not { } top)
        {
            return false;
        }

        for (var node = element; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, top))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts a fresh canvas in the old one's place, and the session, scrollback and everything else stay.</summary>
    private void ReplaceCanvas(string why)
    {
        Log.Write($"pane: {why}; replacing it");
        var old = _canvas!;
        _canvas = CreateCanvas();
        var index = _root.Children.IndexOf(old);
        _root.Children.RemoveAt(index);
        _root.Children.Insert(index, _canvas);
        old.RemoveFromVisualTree(); // releases its device resources
        RetireCanvas(old);
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateScale();

    // ---------------------------------------------------------------- setup

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) => CreateResourcesOn(sender, args.Reason.ToString());

    /// <summary>Makes the device-bound resources, the first time and after a lost device.</summary>
    private void CreateResourcesOn(Microsoft.Graphics.Canvas.ICanvasResourceCreator creator, string reason)
    {
        Log.Write($"pane: CreateResources ({reason})");
        _renderer.CreateResources(creator);
        var size = _renderer.FontSize > 0 ? _renderer.FontSize : Math.Clamp(_config.FontSizeDip, MinFontSize, MaxFontSize);
        _renderer.SetFont(creator, size, DisplayScale);
        UpdateGrid();
    }

    private void UpdateScale()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        _swap?.Resize(); // its buffers follow the window's scale
        if (_renderer.HasResources && Math.Abs(DisplayScale - _renderer.Metrics.Scale) > 0.001f)
        {
            _renderer.SetFont(Creator, _renderer.FontSize, DisplayScale);
            UpdateGrid();
        }
    }

    private void SetFontSize(float size)
    {
        _renderer.SetFont(Creator, Math.Clamp(size, MinFontSize, MaxFontSize), DisplayScale);
        UpdateGrid();
    }

    // The grid's top-left corner in DIPs (the padding, balanced or not), which pointer positions are measured from.
    private float _gridLeft, _gridTop;

    /// <summary>The size the terminal last had its grid laid out at (kept while it's collapsed, which skips sizing).</summary>
    public Windows.Foundation.Size LaidOutSize { get; private set; }

    /// <summary>
    /// The next resize that changes the grid goes to the shell at once instead of once the size settles. A layout
    /// animation holds the terminal at one size, so there's only the one resize. Setting false drops a request that
    /// wasn't used.
    /// </summary>
    /// <summary>The profiles' ids and names, so an action title in the key sequence hint names a profile instead of its id.</summary>
    public Func<IEnumerable<(string Id, string Name)>>? ProfileNames { get; init; }

    public bool ResizeAtOnce
    {
        get => _resizeAtOnce;
        set => _resizeAtOnce = value;
    }

    private bool _resizeAtOnce;

    private void UpdateGrid()
    {
        if (_closing.IsCancellationRequested)
        {
            return; // the pane closed, so never (re)start a session or touch the disposed renderer
        }

        var m = _renderer.Metrics;
        if (m.Width <= 0 || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0)
        {
            Log.Write($"pane: UpdateGrid skipped (cell {m.Width}, canvas {Surface.ActualWidth}x{Surface.ActualHeight})");
            return;
        }

        LaidOutSize = new Windows.Foundation.Size(ActualWidth, ActualHeight);
        var place = GridLayout.Place(Surface.ActualWidth, Surface.ActualHeight, _config.PaddingLeft, _config.PaddingRight,
            _config.PaddingTop, _config.PaddingBottom, m.Width, m.Height, m.Scale, _config.PaddingBalance);
        var (cols, rows) = (place.Columns, place.Rows);
        var resized = cols != _cols || rows != _rows;
        _cols = cols;
        _rows = rows;
        (_gridLeft, _gridTop) = (place.Left, place.Top);
        _renderer.SetGridOrigin(place.Left, place.Top);
        _renderer.EnsureColumns(cols);

        if (_session == null)
        {
            StartSession(cols, rows);
        }
        else
        {
            // Resize even when the grid is unchanged, because the cell pixel size
            // (reported to apps via XTWINOPS/mouse pixels) may have changed.
            _session.Resize(cols, rows, m.WidthPx, m.HeightPx, atOnce: resized && _resizeAtOnce);
            _resizeAtOnce &= !resized;
            if (resized && _previewScript is { } script)
            {
                ReplayPreview(_session, script);
            }
        }

        Redraw();
    }

    /// <summary>The canvas's color before its first frame, which is the background, as see-through as it's drawn.</summary>
    private static Windows.UI.Color ClearColor(Config config)
    {
        var c = TerminalRenderer.ToColor(config.Theme.Background);
        return Windows.UI.Color.FromArgb((byte)(255 * config.BackgroundOpacity), c.R, c.G, c.B);
    }

    /// <summary>A console Windows handed over (default terminal), which is attached instead of starting a program.</summary>
    public Zinc.Pty.ConPty? HandedOver { get; set; }

    private void StartSession(ushort cols, ushort rows)
    {
        Log.Write($"pane: start {cols}x{rows}: {_commandLine}");
        var m = _renderer.Metrics;
        var session = new TerminalSession(cols, rows, _config.Theme, _config.PaletteGeneration);
        session.SetColorScheme(_config.IsDark);
        session.SetScrollback(_config.ScrollbackLines);
        session.SetCursorDefaults(_config.CursorStyle, _config.CursorBlink, _config.CursorStyleAtPrompt);
        session.CatchScreenFlash = _config.ScreenFlashBell;
        session.Resize(cols, rows, m.WidthPx, m.HeightPx);
        _selection = new SelectionGesture(session);
        _selection.SetWordBoundaries(SelectionGesture.Boundaries(_config.WordBoundaries));

        var dq = DispatcherQueue;
        session.OutputProcessed += RequestRedraw;
        // Synchronized output keeps the frame from when a program starts drawing one. If the program
        // never finishes, the pane redraws once the renderer's timeout has passed, because no more
        // output would ask for a frame.
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
        // One notice until the title is taken (TerminalSession.TakeTitle), so a program retitling itself on every
        // line costs one read per UI tick, not one per change.
        session.TitleChanged += () => dq.SafeEnqueue(() =>
        {
            if (session.TakeTitle() is not { } title)
            {
                return;
            }

            _programTitle = title;
            NoteTitle(); // a new command's start first, so the title it set counts as the command's own
            _programTitleSinceCommand = RunningCommand != null;
            NoteTitle();
        });
        session.Bell += () => dq.SafeEnqueue(OnBell);
        session.DirectoryChanged += () =>
        {
            // Another machine, or a WSL distro with a name of its own that this pane didn't start with wsl.exe.
            if (session.RemoteHost is { } host)
            {
                ShellEnvironment.WhenWslHost(host, session.RecheckReportedDirectory);
            }

            dq.SafeEnqueue(() =>
            {
                NoteTitle();
                DirectoryChanged?.Invoke();
            });
        };
        session.CommandsChanged += () => dq.SafeEnqueue(OnCommandsChanged);
        ForwardProgramNotifications(session);
        session.ProcessExited += code => dq.SafeEnqueue(() => OnProcessExited(code));
        ApplyClipboardPermissions(session);
        _session = session;
        if (_previewScript is { } script)
        {
            ReplayPreview(session, script);
            return;
        }

        if (ShellDirectory.IsWsl(_commandLine))
        {
            // The distro's wsl.conf is read now rather than at the first link, because a host name of its own makes
            // its folder reports count (LocalHosts). A folder report that came in before the host name was known is
            // taken now.
            System.Threading.Tasks.Task.Run(() =>
            {
                var context = ShellEnvironment.LinkContextFor(_commandLine);
                session.RecheckReportedDirectory();
                dq.SafeEnqueue(() => _linkContext ??= context);
            });
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
            // The program's environment is Zinc's own, then, with reload-environment on, Windows' variables as they are
            // now (a PATH changed since Zinc started), then the config's. Shell integration and zn add theirs on top,
            // each reading the variables as the program gets them.
            var ours = Zinc.Pty.ChildEnvironment.Ours();
            var environment = Zinc.Pty.ChildEnvironment.Changes(ours, _config.ReloadEnvironment ? Zinc.Pty.ChildEnvironment.Fresh() : null,
                _config.EnvironmentVariables);
            string? Inherited(string name) => environment.TryGetValue(name, out var set) ? set : ours.GetValueOrDefault(name);

            var launch = new ShellLaunch(_commandLine, new Dictionary<string, string?>());
            var scripts = Path.Combine(AppContext.BaseDirectory, ShellIntegration.FolderName);
            if (_config.ShellIntegration == ShellIntegrationMode.Auto && Directory.Exists(scripts))
            {
                launch = ShellIntegration.Prepare(_commandLine, scripts, Inherited);
                if (launch.CommandLine != _commandLine || launch.Environment.Count > 0)
                {
                    Log.Write($"pane: shell integration: {launch.CommandLine} (+{launch.Environment.Count} variables)");
                }
            }

            foreach (var (name, value) in launch.Environment)
            {
                environment[name] = value;
            }

            LaunchEnvironment?.Invoke(environment);
            session.Start(launch.CommandLine, _workingDirectory, cols, rows, environment);
            TypeStartupInputWhenReady(session);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // Nothing to run (a shell builtin like `dir`, a typo, a missing program): say so in the
            // tab, which then closes on a key like a failed session, rather than take the app down.
            Log.Write($"pane: couldn't start {_commandLine}: {ex.Message}");
            _exited = true;
            session.WriteToScreen($"\e[0;31mCouldn't start {_commandLine}\e[0m\r\n{ex.Message}\r\n\r\n" +
                "\e[0;2mShell commands (dir, ls, cd) need a shell, so run the shell and type them there. " +
                "Press any key to close.\e[0m");
        }
    }

    /// <summary>OSC 52 access per the settings (null = denied).</summary>
    private void ApplyClipboardPermissions(TerminalSession session)
    {
        // Writes arrive mid-parse on the reader thread, so the clipboard is set on the UI thread.
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
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        var old = _config;
        _config = config;
        _renderer.Config = config;
        if (_canvas != null)
        {
            _canvas.ClearColor = ClearColor(config);
        }
        else
        {
            _swap!.ClearColor = ClearColor(config);
        }
        if (_session is { } session)
        {
            session.SetTheme(config.Theme, config.PaletteGeneration);
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

        if (old.ProgramTitles != config.ProgramTitles || old.ShellTitles != config.ShellTitles)
        {
            NoteTitle();
        }

        if (old.WordBoundaries != config.WordBoundaries)
        {
            _selection?.SetWordBoundaries(SelectionGesture.Boundaries(config.WordBoundaries));
        }

        ApplyShaders(config);
        if (_renderer.HasResources)
        {
            // Anything SetFont reads, because the faces, formats and cell size are made there. That includes the
            // weights and fallbacks, or a weight changed live would show only after the next family change.
            if (old.FontFamily != config.FontFamily || old.FontSize != config.FontSize || old.AdjustCellHeight != config.AdjustCellHeight ||
                old.FontWeight != config.FontWeight || old.BoldWeight != config.BoldWeight || old.FontFallback != config.FontFallback)
            {
                var size = old.FontSize != config.FontSize ? ZoomedFontSize() : _renderer.FontSize;
                _renderer.SetFont(Creator, size, DisplayScale);
            }

            UpdateGrid(); // padding may have changed
        }

        Redraw();
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
        ExitCode = code;
        ExitedAndKept?.Invoke(code);
        _session.WriteToScreen(code == 0
            ? "\r\n\e[0;2m[Process exited. Press any key to close.]\e[0m"
            : $"\r\n\e[0;2m[Process exited with code {ExitCodes.Format(code)}. Press any key to close.]\e[0m");
    }

    // Held in a field, because a timer that only a local variable references is collected and stops firing.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _holdTimer;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateHoldTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(TerminalRenderer.RenderHoldTimeoutMs + 50);
        timer.IsRepeating = false;
        timer.OnTick(_ => Redraw());
        return timer;
    }

    // Held in a field, because a timer that only a local variable references is collected and stops firing.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _animationTimer;

    /// <summary>The shortest wait for an animation's next frame, about a frame at 120 Hz.</summary>
    private const int MinAnimationFrameMs = 8;

    /// <summary>
    /// Asks for one frame when a Kitty animation's next frame is due (<paramref name="due"/>, on
    /// <see cref="Environment.TickCount64"/>'s clock), or stops asking when nothing plays. A pane that can't be seen
    /// draws no frames, so the timer fires once at most and isn't set again until the pane draws.
    /// </summary>
    private void ScheduleAnimationFrame(long? due)
    {
        if (due is not { } at)
        {
            _animationTimer?.Stop();
            return;
        }

        if (_animationTimer == null)
        {
            _animationTimer = DispatcherQueue.CreateTimer();
            _animationTimer.IsRepeating = false;
            _animationTimer.OnTick(_ => Redraw());
        }

        _animationTimer.Stop();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(at - Environment.TickCount64, MinAnimationFrameMs));
        _animationTimer.Start();
    }

    /// <summary>
    /// When the program's output last arrived, in Stopwatch ticks. The swap chain waits for a pause in it.
    /// </summary>
    private long _lastOutputAt;

    private void RequestRedraw()
    {
        Volatile.Write(ref _lastOutputAt, System.Diagnostics.Stopwatch.GetTimestamp());
        _inputLatency?.Output(TraceNow); // on the reader thread, as the shell's output arrives

        // Coalesce bursts of PTY output into a single invalidate per UI tick.
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 0)
        {
            DispatcherQueue.SafeEnqueue(() =>
            {
                Volatile.Write(ref _invalidatePending, 0);
                Redraw();
                ScheduleSearchRefresh();
            });
        }
    }

    /// <summary>The whole screen including scrollback as plain text ("" before the session starts).</summary>
    public string ReadText() => _session?.ReadScreenText() ?? "";

    /// <summary>Ends the session (killing the child if still running) and frees resources.</summary>
    public void Close()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        _closing.Cancel(); // a clipboard question still open gets no for an answer, and UpdateGrid and UpdateScale stop
        StopShaders();
        _blinkTimer.Stop();
        _animationTimer?.Stop();
        // The window's events would otherwise reach a closed pane (a monitor change would rerun SetFont on the disposed
        // renderer) and keep it alive for the window's life.
        if (_xamlRoot != null)
        {
            _xamlRoot.Changed -= OnXamlRootChanged;
            _xamlRoot = null;
        }

        if (_swap != null)
        {
            _swap.Panel.SizeChanged -= OnCanvasSizeChanged;
        }

        // A pane that never started (a restored tab never shown) still has a renderer and a surface to free. Only the
        // session's own teardown depends on there being one.
        if (_session is { } session)
        {
            _session = null;
            // The gesture holds references into the terminal, so it's freed first.
            _selection?.Dispose();
            _selection = null;
            session.Dispose();
        }

        _renderer.Dispose();
        // The canvas is still in the tree here (its Unloaded comes later), so CanvasLifetime holds it
        // until Win2D has let go of the window.
        if (_canvas != null)
        {
            RetireCanvas(_canvas);
        }

        _swap?.Close();
    }

    // ------------------------------------------------------------ rendering

    private FramePerf? _perf;

    /// <summary>
    /// A terminal for the drawing benchmark. It runs nothing (its workloads arrive through
    /// <see cref="WriteForBenchmark"/>), measures every frame, and draws with the processor when
    /// <paramref name="software"/> is true.
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

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args) => DrawSafely(args.DrawingSession, sender.Device);

    private void DrawSafely(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, Microsoft.Graphics.Canvas.CanvasDevice device)
    {
        // An exception leaving the Draw event leaves Win2D's drawing session open, and every frame
        // after it fails ("The last drawing session ... must be disposed"), so the pane would never
        // draw again. Catching it costs this frame only.
        try
        {
            DrawFrame(ds);
        }
        catch (Exception ex)
        {
            if (device.IsDeviceLost(ex.HResult))
            {
                // Win2D recreates the device (CreateResources) when a lost device's exception leaves Draw. Caught
                // here, the exception doesn't leave, so Win2D has to be told (the swap chain surface makes its own new
                // device on the same event).
                Log.Write($"pane: device lost while drawing ({ex.HResult:X8})");
                device.RaiseDeviceLost();
                return;
            }

            if (_drawFailures++ == 0)
            {
                Log.Crash("draw", ex);
            }
        }
    }

    private void DrawFrame(Microsoft.Graphics.Canvas.CanvasDrawingSession ds)
    {
        if (_session is not { } session)
        {
            return;
        }

        // Test captures draw every cursor as focused, because whether a new window gets focus varies.
        var presentation = new CursorPresentation(_focused || TestHooks.Deterministic || _previewScript != null, _blinkOn);
        var perf = _perf ??= FramePerf.LogEnabled ? new FramePerf { LogsEverySecond = true } : null;
        _renderer.Perf = perf;
        var started = perf != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var blinking = _renderer.Draw(ds, session, _rows, presentation);
        PlaceInputBox(); // follows the cursor
        DrawCommandMarks(ds, session);
        DrawSearchTicks(ds, session);
        if (perf != null)
        {
            var drawn = System.Diagnostics.Stopwatch.GetTimestamp();
            ds.Flush(); // rasterize now, to time it (else it happens after this returns)
            perf.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started, drawn).TotalMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(drawn).TotalMilliseconds);
        }
        UpdateBlinkTimer(blinking);
        ScheduleAnimationFrame(_renderer.NextAnimationFrame);
        UpdateScrollBar(session);
        if (_inputLatency is { } trace)
        {
            trace.Drawn(TraceNow); // the frame is recorded, and it reaches the screen at the next refresh
            TraceSummary();
        }
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
        // With input methods on, typing goes through the pane's input box, so focus lands there.
        if (ReferenceEquals(e.OriginalSource, this) && FocusInputMethodBox())
        {
            return;
        }

        NoteForeignKeys(); // a key held as focus arrives (Enter in a popup, Escape in the find box)
        if (_focused)
        {
            return; // from the pane to its input box, which is still the same focus for the program
        }

        _focused = true;
        UpdateShaderAnimation();
        ResetBlink();
        SendFocusEvent(true);
        Redraw();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        if (FocusIsWithin())
        {
            return; // still inside the pane, so not a focus change for the program
        }

        _focused = false;
        UpdateShaderAnimation();
        CancelSequence();
        SendFocusEvent(false);
        Redraw();
    }

    /// <summary>Whether keyboard focus is on the pane itself or its input box (the find box counts as elsewhere).</summary>
    private bool FocusIsWithin() =>
        XamlRoot != null && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is { } focused &&
        (ReferenceEquals(focused, this) || ReferenceEquals(focused, _inputBox));

    private void SendFocusEvent(bool focused)
    {
        if (_session is { HasExited: false } session && session.GetMode(GhosttyMode.FocusEvent))
        {
            session.SendInput(focused ? "\e[I"u8 : "\e[O"u8, scrollToBottom: false);
        }
    }
}
