using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Phosphor.Input;
using Phosphor.Session;

namespace Phosphor;

public partial class App : Application
{
    private readonly List<MainWindow> _windows = []; // normal windows, most recently active last
    private ConfigFile _config = null!;
    private MessageWindow? _messageWindow;
    private HotkeyListener? _hotkey;
    private GlobalHotkey? _hotkeyChord;
    private TrayIcon? _tray;
    private MainWindow? _quick;
    private bool _quitting;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Write($"unhandled: {e.Exception}");
    }

    internal static App Instance => (App)Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Phosphor decides when to exit (see OnWindowClosed): closing the last window may
        // leave it running in the tray for the quick terminal.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _config = ConfigFile.Load();
        _messageWindow = new MessageWindow();
        var profile = _config.DefaultProfile;
        // A command given on our command line (or PHOSPHOR_SHELL) is asked for explicitly: open
        // it rather than the saved session. Otherwise reopen what was open last time.
        var explicitCommand = Environment.GetCommandLineArgs().Length > 1 ||
                              Environment.GetEnvironmentVariable("PHOSPHOR_SHELL") is { Length: > 0 };
        if (explicitCommand || !RestoreSession())
        {
            // Our own arguments / PHOSPHOR_SHELL override the first tab's command only.
            OpenWindow(profile, ShellResolver.Resolve(profile.Config));
        }

        StartSavingSession();
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

    internal void OpenWindow(Profile profile, string? command = null) =>
        Track(new MainWindow(_config, Keybindings.Default, profile, command ?? ShellResolver.ForProfile(profile.Config)));

    /// <summary>Opens a window for a workspace moved out of another one.</summary>
    internal MainWindow OpenWindow(MainWindow.WorkspaceTransfer transfer) =>
        Track(new MainWindow(_config, Keybindings.Default, transfer));

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
            window.Close();
        }

        _quick?.Close();
        _tray?.Dispose();
        _hotkey?.Dispose();
        _messageWindow?.Dispose();
        Exit();
    }

    // --------------------------------------------------------------- session

    private readonly string? _sessionPath = OwnSession(SessionStore.DefaultPath());
    private string? _savedSession;
    private static Mutex? _sessionOwner;

    /// <summary>
    /// The session file this process may restore and save: only the first Phosphor process running
    /// gets it. Phosphor isn't single-instance yet, so a second launch would otherwise reopen
    /// every window again and the two would overwrite each other's saves.
    /// </summary>
    private static string? OwnSession(string? path)
    {
        if (path == null)
        {
            return null;
        }

        var name = @"Local\Phosphor.Session." + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..16];
        _sessionOwner = new Mutex(initiallyOwned: true, name, out var first);
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
                var window = new MainWindow(_config, Keybindings.Default, saved);
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

        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) => SaveSession();
        timer.Start();
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

            var json = SessionStore.Serialize(new SessionState(SessionState.CurrentVersion, windows));
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
                null,
                new TrayIcon.MenuItem("Quit Phosphor", Quit),
            ];
        };
    }

    /// <summary>Tray click: bring back the most recently used window, or the saved ones, or open one.</summary>
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
            _quick = new MainWindow(_config, Keybindings.Default, profile, ShellResolver.ForProfile(profile.Config), quick: true);
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
