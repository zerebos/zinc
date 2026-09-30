using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Phosphor.Input;
using Phosphor.Session;
using Phosphor.Terminal;

namespace Phosphor;

public partial class App : Application
{
    private readonly List<MainWindow> _windows = []; // normal windows, most recently active last
    private ConfigFile _config = null!;

    // One instance shared by every window and pane; a reload copies the new bindings into it.
    private Keybindings _keybindings = null!;
    private MessageWindow? _messageWindow;
    private HotkeyListener? _hotkey;
    private GlobalHotkey? _hotkeyChord;
    private TrayIcon? _tray;
    private MainWindow? _quick;
    private bool _quitting;

    public App()
    {
        InitializeComponent();

        // Every exception that escapes leaves a record (crash.log, even without PHOSPHOR_LOG).
        // One on the UI thread is survived: a crash would take every terminal with it, and
        // most such failures belong to one action (a menu item, a palette command). Background
        // threads and XAML's own fail-fasts can't be survived; they're at least recorded.
        UnhandledException += (_, e) =>
        {
            Log.Crash("UI thread", e.Exception);
            e.Handled = true;
            ShowSurvivedError(e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Crash("background thread", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Crash("async task", e.Exception);
            e.SetObserved();
        };
    }

    internal static App Instance => (App)Current;

    /// <summary>Tells the front window's user that something failed and Phosphor carried on.</summary>
    internal void ShowSurvivedError(Exception exception)
    {
        try
        {
            _windows.LastOrDefault()?.ShowError(exception);
        }
        catch (Exception ex)
        {
            Log.Crash("showing an error", ex); // never let the report become the next failure
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Phosphor decides when to exit (see OnWindowClosed): closing the last window may
        // leave it running in the tray for the quick terminal.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        WatchSystemScheme(); // before the first read: a light/dark theme pair picks its side from it
        _config = ConfigFile.Load();
        CanvasLifetime.SoftwareRendering = _config.Global.SoftwareRendering;
        LoadRecentCommands();
        _keybindings = _config.Keybindings;
        foreach (var warning in _config.Warnings)
        {
            Log.Write($"config: {warning}"); // shown in the window once settings reload on save
        }
        _messageWindow = new MessageWindow();
        var profile = _config.DefaultProfile;
        // A command given on our command line (or PHOSPHOR_SHELL) is asked for explicitly: open
        // it rather than the saved session. Otherwise reopen what was open last time.
        var explicitCommand = Environment.GetCommandLineArgs().Length > 1 ||
                              Environment.GetEnvironmentVariable("PHOSPHOR_SHELL") is { Length: > 0 };
        if (!explicitCommand && !File.Exists(_config.Path))
        {
            // First launch: the welcome page, then the first window, built the way it asks for.
            ShowWelcome(firstRun: true);
        }
        else if (explicitCommand || !RestoreSession())
        {
            // Our own arguments / PHOSPHOR_SHELL override the first tab's command only.
            OpenWindow(profile, ShellResolver.Resolve(profile.Config));
        }

        StartSavingSession();
        AcceptRedirectedLaunches();
        WatchConfig();
        try
        {
            SetUpQuickTerminal();
            SetUpTray();
        }
        catch (Exception ex)
        {
            // Background features failing must not take the open window down with them.
            Log.Write($"background setup failed: {ex}");
        }
    }

    private WelcomeWindow? _welcome;

    /// <summary>
    /// The welcome page (on first launch, and from the palette): writes its answers into
    /// config.toml, in place. On first launch it then opens the first window; later, the reload
    /// that follows the save applies colors, font and shell (the layout applies to new windows).
    /// </summary>
    private SettingsWindow? _settings;

    /// <summary>The settings window: opened, or brought forward if it's open.</summary>
    /// <summary>Opens (or brings back) the settings window, on <paramref name="section"/> if given.</summary>
    internal void ShowSettings(string? section = null)
    {
        if (_settings != null)
        {
            if (section != null)
            {
                _settings.ShowSection(section);
            }

            _settings.Activate();
            return;
        }

        // It comes back where it was (saved with the session), else over the window you're in;
        // during a run, on the section it was left on.
        var settings = _settings = new SettingsWindow(_config, section ?? _settingsSection);
        if (!WindowPlacement.Restore(settings, _settingsBounds))
        {
            if (_windows.LastOrDefault(w => w.AppWindow is { IsVisible: true }) is { } owner)
            {
                WindowPlacement.CenterOver(settings, owner, 1040, 780);
            }
            else
            {
                WindowPlacement.Resize(settings, 1040, 780);
            }
        }

        settings.AppWindow.Changed += (window, e) =>
        {
            if ((e.DidPositionChange || e.DidSizeChange) && WindowPlacement.Bounds(window) is { } bounds)
            {
                _settingsBounds = bounds;
            }
        };
        settings.Closed += (_, _) =>
        {
            _settingsSection = settings.Section;
            _settings = null;
        };
        // Its writes reach the windows through the config watcher (started if the folder is new).
        settings.Wrote += WatchConfig;
        settings.Activate();
    }

    internal void ShowWelcome(bool firstRun)
    {
        if (_welcome != null)
        {
            _welcome.Activate();
            return;
        }

        _welcome = new WelcomeWindow(_config, firstRun);
        // Centered on the screen: the main one on first run, else the one your window is on.
        WindowPlacement.CenterOnScreen(_welcome, 940, 720, _windows.LastOrDefault(w => w.AppWindow is { IsVisible: true }));
        _welcome.Finished += choices =>
        {
            _welcome = null;
            var path = _config.Path;
            try
            {
                var existing = File.Exists(path) ? File.ReadAllText(path) : null;
                // Skipped on first launch: an almost empty config, so the page doesn't come back.
                var text = choices == null ? existing ?? FirstRun.Template : FirstRun.Apply(existing, choices, existing != null ? _config : null);
                if (text != existing)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, text);
                    Log.Write($"welcome: wrote {path}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Crash("saving the welcome page's choices", ex);
            }

            if (firstRun)
            {
                _config = ConfigFile.Load(path);
                CanvasLifetime.SoftwareRendering = _config.Global.SoftwareRendering;
                _keybindings.CopyFrom(_config.Keybindings);
                OpenWindow(_config.DefaultProfile);
                WatchConfig(); // the folder exists now
            }
            else
            {
                ReloadConfig();
            }
        };
        _welcome.Activate();
    }

    internal void OpenWindow(Profile profile, string? command = null) =>
        Track(new MainWindow(_config, _keybindings, profile, command ?? ShellResolver.ForProfile(profile.Config)));

    /// <summary>Opens a window for a workspace moved out of another one.</summary>
    internal MainWindow OpenWindow(MainWindow.WorkspaceTransfer transfer) =>
        Track(new MainWindow(_config, _keybindings, transfer));

    /// <summary>The tab being dragged, if any (windows hand it over directly).</summary>
    internal MainWindow.TabDrag? Drag
    {
        get => _drag;
        set
        {
            _drag = value;
            // While a pane's tab is dragged, workspace tabs must stay put: TabView's reordering
            // slides them apart under any drag (moving the one being rested on to switch to it)
            // and overrides the drop's caption with its own "Move".
            var reorder = value?.Tab is not MainWindow.TerminalTab;
            foreach (var window in _quick != null ? _windows.Append(_quick) : _windows)
            {
                window.SetTabReordering(reorder);
            }
        }
    }

    private MainWindow.TabDrag? _drag;

    /// <summary>Whether a screen point is inside one of Phosphor's visible windows.</summary>
    internal bool IsOverWindow(Windows.Graphics.PointInt32 point)
    {
        foreach (var window in _quick is { IsQuickShown: true } ? _windows.Append(_quick) : _windows)
        {
            var (pos, size) = (window.AppWindow.Position, window.AppWindow.Size);
            if (window.AppWindow.IsVisible && point.X >= pos.X && point.X < pos.X + size.Width &&
                point.Y >= pos.Y && point.Y < pos.Y + size.Height)
            {
                return true;
            }
        }

        return false;
    }

    private MainWindow Track(MainWindow window)
    {
        _windows.Add(window);
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            OnWindowClosed();
        };
        window.Activate();
        return window;
    }

    /// <summary>A normal window was activated: it's the one the tray icon brings back first.</summary>
    internal void NoteActivated(MainWindow window)
    {
        if (_windows.Remove(window))
        {
            _windows.Add(window);
        }
    }

    /// <summary>
    /// After a window closes: with no normal windows left, quit, unless the quick terminal
    /// can still be summoned or is showing (quit-after-last-window-closed overrides).
    /// </summary>
    private void OnWindowClosed()
    {
        if (_quitting || _windows.Count > 0)
        {
            return;
        }

        var stay = _config.Global.QuitAfterLastWindowClosed is { } quit
            ? !quit
            : _hotkey?.IsRegistered == true || _quick?.IsQuickShown == true;
        Log.Write($"last window closed: {(stay ? "staying in the tray" : "quitting")}");
        if (!stay)
        {
            Quit();
        }
    }

    /// <summary>Closes every window (ending their shells) and exits.</summary>
    /// <summary>Quits when you ask to: first asks, in the front window, if commands are running.</summary>
    internal async void AskQuit()
    {
        if (_windows.LastOrDefault() is { } front && _windows.Any(w => w.RunningCommands().Any()))
        {
            front.Activate();
            if (!await front.ConfirmQuit(_windows.ToList()))
            {
                return;
            }
        }

        Ui.Guard("quitting", Quit);
    }

    internal void Quit()
    {
        if (_quitting)
        {
            return;
        }

        SaveSession(); // before the windows start closing (each close would drop one)
        _quitting = true;
        foreach (var window in _windows.ToList())
        {
            window.CloseWithoutAsking();
        }

        _quick?.Close();
        _tray?.Dispose();
        _hotkey?.Dispose();
        _messageWindow?.Dispose();
        Exit();
    }

    // ------------------------------------------------------- single instance

    private static readonly List<string[]> PendingLaunches = [];
    private static Microsoft.UI.Dispatching.DispatcherQueue? _launchQueue;

    /// <summary>
    /// Another launch of Phosphor was handed to this process (Program): open what it asked
    /// for. Called on a background thread, possibly before startup has finished.
    /// </summary>
    internal static void OnRedirectedLaunch(Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        var commandLine = (args.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments ?? "";
        Log.Write($"single instance: {args.Kind} redirected here: [{commandLine}]");
        var argv = LaunchArguments(commandLine);
        lock (PendingLaunches)
        {
            if (_launchQueue == null)
            {
                PendingLaunches.Add(argv);
                return;
            }
        }

        _launchQueue.SafeEnqueue(() => Instance.OpenForLaunch(argv));
    }

    /// <summary>Starts handling redirected launches on the UI thread, including any that came during startup.</summary>
    private void AcceptRedirectedLaunches()
    {
        lock (PendingLaunches)
        {
            _launchQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            foreach (var argv in PendingLaunches)
            {
                _launchQueue.SafeEnqueue(() => OpenForLaunch(argv));
            }

            PendingLaunches.Clear();
        }
    }

    /// <summary>
    /// What a later launch opens: its command, if it gave one; otherwise the saved windows when
    /// none is open (Phosphor was in the tray), else a new window.
    /// </summary>
    private void OpenForLaunch(string[] argv)
    {
        if (_quitting)
        {
            return;
        }

        var profile = _config.DefaultProfile;
        if (argv.Length > 0)
        {
            // Its PHOSPHOR_SHELL stayed with the other process; only its arguments came over.
            OpenWindow(profile, ShellResolver.Resolve(profile.Config, argv,
                name => name == "PHOSPHOR_SHELL" ? null : Environment.GetEnvironmentVariable(name)));
        }
        else if (_windows.Count > 0 || !RestoreSession())
        {
            OpenWindow(profile);
        }
    }

    /// <summary>A launch's arguments, without the executable (which unpackaged activations may include).</summary>
    private static string[] LaunchArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var argv = SplitCommandLine(commandLine);
        var self = Path.GetFileName(Environment.ProcessPath ?? "Phosphor.exe");
        return argv.Length > 0 && Path.GetFileName(argv[0]).Equals(self, StringComparison.OrdinalIgnoreCase) ? argv[1..] : argv;
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        var ptr = CommandLineToArgvW(commandLine, out var count);
        if (ptr == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(ptr, i * IntPtr.Size))!;
            }

            return result;
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    [System.Runtime.InteropServices.LibraryImport("shell32.dll", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial IntPtr CommandLineToArgvW(string commandLine, out int count);

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);

    // ---------------------------------------------------------------- config

    private FileSystemWatcher? _configWatcher;

    // In a field: an unreferenced DispatcherQueueTimer is collected and stops firing.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _reloadTimer;

    /// <summary>
    /// Reloads the config shortly after it (an included file in its folder, or a scheme in its
    /// themes folder) is saved. Editors often write a file several times per save, and the
    /// watcher reports each one, so changes settle for a moment first.
    /// </summary>
    private void WatchConfig()
    {
        if (_configWatcher != null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(_config.Path);
        if (folder == null || !Directory.Exists(folder))
        {
            return; // no config folder yet: nothing to watch until Phosphor is restarted
        }

        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _reloadTimer = queue.CreateTimer();
        _reloadTimer.Interval = TimeSpan.FromMilliseconds(300);
        _reloadTimer.IsRepeating = false;
        _reloadTimer.OnTick(_ => ReloadConfig());

        var themes = Path.Combine(folder, "themes") + Path.DirectorySeparatorChar;
        void OnChange(object sender, FileSystemEventArgs e)
        {
            // The folder also holds session.json, rewritten as windows change: ignore it.
            var path = e.FullPath;
            if (path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) || path.StartsWith(themes, StringComparison.OrdinalIgnoreCase))
            {
                queue.SafeEnqueue(() =>
                {
                    _reloadTimer.Stop();
                    _reloadTimer.Start();
                });
            }
        }

        _configWatcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _configWatcher.Changed += OnChange;
        _configWatcher.Created += OnChange;
        _configWatcher.Deleted += OnChange;
        _configWatcher.Renamed += (s, e) => OnChange(s, e);
        _configWatcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Reads the config again and applies it to every window. A file that isn't valid TOML (a
    /// save mid-edit) is only reported: the settings in force stay.
    /// </summary>
    // Held here: it's what raises ColorValuesChanged.
    private Windows.UI.ViewManagement.UISettings? _uiSettings;

    /// <summary>
    /// Follows Windows' app light/dark mode (Settings > Personalization > Colors): a
    /// <c>light:…,dark:…</c> theme switches sides when it flips, through a config reload.
    /// </summary>
    private void WatchSystemScheme()
    {
        _uiSettings = new Windows.UI.ViewManagement.UISettings();
        ConfigFile.SystemScheme = CurrentSystemScheme();
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _uiSettings.ColorValuesChanged += (_, _) => dispatcher.SafeEnqueue(() => SetSystemScheme(CurrentSystemScheme()));
    }

    /// <summary>Windows is light or dark now (or a test says so: test:appearance-light / -dark).</summary>
    internal void SetSystemScheme(Ghostty.ColorScheme scheme)
    {
        if (scheme == ConfigFile.SystemScheme)
        {
            return; // an accent color change, say
        }

        ConfigFile.SystemScheme = scheme;
        Log.Write($"appearance: Windows is now {scheme.ToString().ToLowerInvariant()}{(_config.FollowsAppearance ? "; reloading the config" : "")}");
        if (_config.FollowsAppearance)
        {
            ReloadConfig();
        }
    }

    /// <summary>Light or dark apps, by Windows' own app background.</summary>
    private Ghostty.ColorScheme CurrentSystemScheme()
    {
        var background = _uiSettings!.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        return background.R + background.G + background.B > 3 * 128 ? Ghostty.ColorScheme.Light : Ghostty.ColorScheme.Dark;
    }

    internal void ReloadConfig()
    {
        if (_quitting)
        {
            return;
        }

        var fresh = ConfigFile.Load(_config.Path);
        Log.Write($"config: reloaded ({fresh.Warnings.Count} warning(s){(fresh.HasErrors ? ", not valid TOML: not applied" : "")})");
        foreach (var warning in fresh.Warnings)
        {
            Log.Write($"config: {warning}");
        }

        var windows = _quick != null ? _windows.Append(_quick).ToList() : _windows.ToList();
        _settings?.Refresh(fresh);
        if (fresh.HasErrors)
        {
            windows.ForEach(w => w.ShowConfigProblems(fresh, applied: false));
            return;
        }

        var hotkeyChanged = fresh.Global.QuickTerminal.Hotkey != _config.Global.QuickTerminal.Hotkey;
        _config = fresh;
        CanvasLifetime.SoftwareRendering = fresh.Global.SoftwareRendering; // terminals opened from now on
        _keybindings.CopyFrom(fresh.Keybindings);
        windows.ForEach(w => w.ApplyConfig(fresh));
        if (hotkeyChanged)
        {
            _hotkey?.Dispose();
            _hotkey = null;
            SetUpQuickTerminal();
        }
    }

    // --------------------------------------------------------------- session

    private readonly string? _sessionPath = OwnSession(SessionStore.DefaultPath());

    // The settings window's place (saved with the session) and, for this run, its section.
    private WindowBounds? _settingsBounds;
    private string? _settingsSection;
    private string? _savedSession;

    /// <summary>The command palette's recently run commands, shared by every window; saved with the session.</summary>
    internal Phosphor.Commands.RecentCommands RecentCommands { get; private set; } = new();

    /// <summary>Recent commands come back even when the windows don't (a launch with a command).</summary>
    private void LoadRecentCommands()
    {
        var saved = _sessionPath != null && _config.Global.RestoreSession ? SessionStore.Load(_sessionPath) : null;
        if (saved?.RecentCommands is { } titles)
        {
            RecentCommands = new Phosphor.Commands.RecentCommands(titles);
        }

        _settingsBounds = saved?.Settings;
    }

    // Held here: a DispatcherQueueTimer nothing references is garbage-collected and stops
    // firing (saves silently stopped after the first GC, 2026-09-27).
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sessionTimer;
    private static Mutex? _sessionOwner;

    /// <summary>
    /// The session file this process may restore and save: only the first Phosphor process running
    /// gets it. Normally a second launch never gets this far (Program redirects it to the running
    /// one); this is the backstop if that fails, so two processes can't both reopen every window
    /// and overwrite each other's saves.
    /// </summary>
    private static string? OwnSession(string? path)
    {
        if (path == null)
        {
            return null;
        }

        _sessionOwner = new Mutex(initiallyOwned: true, @"Local\" + SessionStore.InstanceKey(path) + ".Session", out var first);
        if (!first)
        {
            Log.Write("session: another Phosphor process owns it; not restoring or saving here");
            _sessionOwner.Dispose();
            _sessionOwner = null;
            return null;
        }

        return path;
    }

    /// <summary>Reopens the windows saved last time; false if there were none (or none reopened).</summary>
    private bool RestoreSession()
    {
        if (_sessionPath == null || !_config.Global.RestoreSession || SessionStore.Load(_sessionPath) is not { } session)
        {
            return false;
        }

        var opened = 0;
        foreach (var saved in session.Windows)
        {
            try
            {
                var window = new MainWindow(_config, _keybindings, saved);
                if (window.HasWorkspaces)
                {
                    Track(window);
                    opened++;
                }
                else
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                Log.Write($"session: couldn't reopen a window: {ex}");
            }
        }

        Log.Write($"session: reopened {opened} of {session.Windows.Count} window(s)");
        return opened > 0;
    }

    /// <summary>
    /// Saves the open windows every couple of seconds when they changed (cheap: a few dozen
    /// tabs), so a crash or power loss keeps the layout too. Quit saves once more first.
    /// </summary>
    private void StartSavingSession()
    {
        if (_sessionPath == null || !_config.Global.RestoreSession)
        {
            return;
        }

        _sessionTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromSeconds(2);
        _sessionTimer.OnTick(_ => SaveSession());
        _sessionTimer.Start();
    }

    /// <summary>
    /// Writes the open windows to session.json if they changed. With no windows open (the last
    /// one was closed, the app stays in the tray) the previous save stands, so that window comes back.
    /// </summary>
    private void SaveSession()
    {
        if (_sessionPath == null || _quitting || !_config.Global.RestoreSession)
        {
            return;
        }

        try
        {
            var windows = _windows.Select(w => w.CaptureState()).OfType<WindowState>().ToList();
            if (windows.Count == 0)
            {
                return;
            }

            var json = SessionStore.Serialize(new SessionState(SessionState.CurrentVersion, windows, [.. RecentCommands.Titles], _settingsBounds));
            if (json != _savedSession)
            {
                SessionStore.Save(_sessionPath, json);
                _savedSession = json;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"session: save failed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ tray

    private void SetUpTray()
    {
        _tray = new TrayIcon(_messageWindow!, Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"));
        _tray.Activated += ShowWindows;
        _tray.NotificationClicked += () => _notificationClicked?.Invoke();
        _tray.BuildMenu = () =>
        {
            var quickLabel = _quick?.IsQuickShown == true ? "Hide quick terminal" : "Show quick terminal";
            if (_hotkeyChord is { } chord)
            {
                quickLabel += "\t" + HotkeyName(chord);
            }

            return
            [
                new TrayIcon.MenuItem("New window", () => OpenWindow(_config.DefaultProfile)),
                new TrayIcon.MenuItem(quickLabel, ToggleQuickTerminalFromTray),
                new TrayIcon.MenuItem("Settings", () => ShowSettings()),
                null,
                new TrayIcon.MenuItem("Quit Phosphor", AskQuit),
            ];
        };
    }

    /// <summary>Tray click: bring back the most recently used window, or the saved ones, or open one.</summary>
    private Action? _notificationClicked;

    /// <summary>
    /// A Windows notification (through the tray icon); clicking it runs <paramref name="clicked"/>
    /// (the newest notification's, as Windows shows one at a time).
    /// </summary>
    internal void Notify(string title, string body, Action clicked)
    {
        if (_tray == null)
        {
            return;
        }

        _notificationClicked = clicked;
        _tray.ShowNotification(title, body);
        Log.Write($"notify: {title} / {body}");
    }

    private void ShowWindows()
    {
        if (_windows.Count == 0)
        {
            if (!RestoreSession())
            {
                OpenWindow(_config.DefaultProfile);
            }

            return;
        }

        var window = _windows[^1];
        if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        window.Activate();
    }

    private void ToggleQuickTerminalFromTray()
    {
        // The menu has focus, so the quick window is never "active" here: go by visibility.
        if (_quick?.IsQuickShown == true)
        {
            _quick.HideQuick();
        }
        else
        {
            ToggleQuickTerminal();
        }
    }

    // ------------------------------------------------------- quick terminal

    private void SetUpQuickTerminal()
    {
        try
        {
            _hotkeyChord = GlobalHotkey.Parse(_config.Global.QuickTerminal.Hotkey);
        }
        catch (FormatException ex)
        {
            Log.Write($"quick terminal: bad hotkey: {ex.Message}");
            return;
        }

        _hotkey = new HotkeyListener(_messageWindow!, _hotkeyChord.Value);
        _hotkey.Pressed += ToggleQuickTerminal;
        _hotkey.TryRegister();
    }

    /// <summary>
    /// Null when the quick terminal hotkey is registered (or disabled); otherwise its
    /// display name, e.g. "Win+`", so windows can say it's taken. Retries registration
    /// first: another app may have released it since.
    /// </summary>
    internal string? UnavailableQuickTerminalHotkey()
    {
        if (_hotkey == null || _hotkey.TryRegister())
        {
            return null;
        }

        return HotkeyName(_hotkeyChord!.Value);
    }

    internal void ToggleQuickTerminal()
    {
        if (_quick == null)
        {
            var profile = _config.DefaultProfile;
            _quick = new MainWindow(_config, _keybindings, profile, ShellResolver.ForProfile(profile.Config), quick: true);
            var quick = _quick;
            _quick.Closed += (_, _) =>
            {
                if (_quick == quick)
                {
                    _quick = null;
                }

                OnWindowClosed();
            };
            _quick.ShowQuick();
        }
        else if (_quick.IsQuickShownAndActive)
        {
            _quick.HideQuick();
        }
        else
        {
            _quick.ShowQuick();
        }
    }

    private static string HotkeyName(GlobalHotkey hotkey)
    {
        var name = new KeyChord(hotkey.Key,
            (hotkey.Modifiers & GlobalHotkey.Control) != 0,
            (hotkey.Modifiers & GlobalHotkey.Alt) != 0,
            (hotkey.Modifiers & GlobalHotkey.Shift) != 0).ToString();
        return (hotkey.Modifiers & GlobalHotkey.Win) != 0 ? "Win+" + name : name;
    }
}
