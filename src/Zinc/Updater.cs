using System.Reflection;
using Microsoft.UI.Dispatching;
using Velopack;
using Velopack.Sources;

namespace Zinc;

/// <summary>What the updater is doing, for About's card and the windows' notice.</summary>
internal enum UpdateStatus
{
    /// <summary>
    /// This copy can't update, because the installer didn't install it (a dev build, or a folder published by hand) or
    /// it has no update address.
    /// </summary>
    NotInstalled,

    /// <summary>Hasn't checked yet.</summary>
    Idle,

    Checking,
    Downloading,

    /// <summary>A newer version is downloaded, and it installs on restart or when Zinc quits.</summary>
    Ready,

    UpToDate,

    /// <summary>The last check or download failed (no network, the address gone), and the next one tries again.</summary>
    Failed,
}

/// <summary>
/// Updates for copies the installer put in place (Velopack). Zinc checks a minute after startup and every day after
/// that (<c>[updates] check</c>), or when asked (About), and a newer version downloads in the background and waits. It
/// installs when Zinc quits, or at once through <see cref="RestartNow"/> (App's quit, with its confirmation and the
/// session saved), never by surprise. The address is built into release builds (<c>ZincUpdateUrl</c>, release.ps1).
/// It names the GitHub repository whose releases hold the updates, and <c>ZINC_UPDATE_URL</c> overrides it for tests
/// with a folder or a web address that serves the release files themselves. Everything here runs on the UI thread.
/// </summary>
internal static class Updater
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1), Interval = TimeSpan.FromDays(1);

    public static string? Source { get; } =
        Environment.GetEnvironmentVariable("ZINC_UPDATE_URL") is { Length: > 0 } url ? url
        : typeof(Updater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ZincUpdateUrl")?.Value is { Length: > 0 } builtIn ? builtIn : null;

    public static UpdateStatus Status
    {
        get
        {
            _ = Manager; // decides NotInstalled
            return _status;
        }
    }

    private static UpdateStatus _status = UpdateStatus.Idle;

    /// <summary>The version downloading or ready.</summary>
    public static string? NewVersion { get; private set; }

    /// <summary>The new version's release notes (CHANGELOG.md's entry, Markdown), "" when it has none.</summary>
    public static string NewNotes { get; private set; } = "";

    /// <summary>Download progress, 0-100.</summary>
    public static int Progress { get; private set; }

    public static DateTime? LastChecked { get; private set; }

    public static string? Error { get; private set; }

    /// <summary>
    /// The ready update's notice was put off ("Install when I quit"), so windows don't show it again this run.
    /// </summary>
    public static bool NoticeDismissed { get; private set; }

    /// <summary>
    /// Handles "Install when I quit" on the notice. Every window hides the notice, and the update still installs when
    /// Zinc quits.
    /// </summary>
    public static void DismissNotice()
    {
        NoticeDismissed = true;
        Changed?.Invoke();
    }

    /// <summary>Something above changed (on the UI thread).</summary>
    public static event Action? Changed;

    private static UpdateManager? _manager;
    private static bool _managerTried;
    private static UpdateInfo? _ready;
    private static DispatcherQueueTimer? _timer;

    /// <summary>The installer's update manager, or null when this copy can't update.</summary>
    private static UpdateManager? Manager
    {
        get
        {
            if (!_managerTried)
            {
                _managerTried = true;
                try
                {
                    if (Source is { } source && ManagerFor(source) is { IsInstalled: true } manager)
                    {
                        _manager = manager;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write($"update: no update manager ({ex.Message})");
                }

                if (_manager == null)
                {
                    _status = UpdateStatus.NotInstalled;
                }
            }

            return _manager;
        }
    }

    /// <summary>
    /// The update manager for an address. A GitHub repository's updates are its releases. The ones marked as
    /// pre-releases count only when this build is a pre-release itself, so a tester on a release candidate gets the
    /// next candidate and then the release, and everyone else gets releases only. Any other address is a folder or a
    /// web address that serves the release files themselves.
    /// </summary>
    private static UpdateManager ManagerFor(string source) =>
        UpdateFeed.GitHubRepository(source) is { } repository
            ? new UpdateManager(new GithubSource(repository, accessToken: null, prerelease: UpdateFeed.IsPreRelease(OwnVersion)))
            : new UpdateManager(source);

    /// <summary>This build's version, as the build worked it out ("0.1.0-rc.1+a1b2c3d").</summary>
    private static string OwnVersion =>
        typeof(Updater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    /// <summary>The installed version (null when this copy can't update).</summary>
    public static string? CurrentVersion => Manager?.CurrentVersion?.ToString();

    /// <summary>
    /// Starts or stops the daily check (<c>[updates] check</c>). Call it at startup and on every config reload.
    /// </summary>
    public static void Schedule(bool automatic)
    {
        if (Manager == null)
        {
            return;
        }

        if (!automatic)
        {
            _timer?.Stop();
            return;
        }

        if (_timer == null)
        {
            // A field, not a local, because a timer only a local holds is collected and stops firing.
            _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _timer.Interval = FirstCheck;
            _timer.OnTick(timer =>
            {
                timer.Interval = Interval;
                _ = CheckAsync();
            });
        }

        if (!_timer.IsRunning)
        {
            _timer.Start();
        }
    }

    /// <summary>Checks, and downloads a newer version. It does nothing while busy or once one is ready.</summary>
    public static async Task CheckAsync()
    {
        if (Manager is not { } manager || Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Ready)
        {
            return;
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        Set(UpdateStatus.Checking);
        try
        {
            var update = await manager.CheckForUpdatesAsync();
            LastChecked = DateTime.Now;
            if (update == null)
            {
                Log.Write($"update: up to date ({manager.CurrentVersion})");
                Set(UpdateStatus.UpToDate);
                return;
            }

            NewVersion = update.TargetFullRelease.Version.ToString().Split('+')[0]; // without the commit hash
            NewNotes = update.TargetFullRelease.NotesMarkdown ?? "";
            Progress = 0;
            Log.Write($"update: {manager.CurrentVersion} -> {NewVersion}, downloading");
            Set(UpdateStatus.Downloading);
            await manager.DownloadUpdatesAsync(update, percent => queue.SafeEnqueue(() =>
            {
                if (Status == UpdateStatus.Downloading)
                {
                    Progress = percent;
                    Changed?.Invoke();
                }
            }));

            _ready = update;
            Log.Write($"update: {NewVersion} downloaded; installs on restart or when Zinc quits");
            Set(UpdateStatus.Ready);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Write($"update: failed: {ex.Message}");
            Set(UpdateStatus.Failed);
        }
    }

    /// <summary>
    /// Called last thing as Zinc quits, and hands a ready update to the installer, which waits for this process to end,
    /// installs it, and starts the new version if <paramref name="restart"/>.
    /// </summary>
    public static void ApplyOnExit(bool restart)
    {
        if (_ready is not { } update || Manager is not { } manager)
        {
            return;
        }

        try
        {
            Log.Write($"update: installing {NewVersion} after exit{(restart ? ", then restarting" : "")}");
            manager.WaitExitThenApplyUpdates(update, silent: true, restart: restart);
        }
        catch (Exception ex)
        {
            Log.Write($"update: couldn't hand the update over: {ex.Message}");
        }
    }

    /// <summary>Restarts into the ready version through the normal quit (asks first if commands are running).</summary>
    public static void RestartNow() => App.Instance.AskQuit(fresh: false, restartForUpdate: true);

    /// <summary>One line for About's card.</summary>
    public static string Describe() => Status switch
    {
        UpdateStatus.NotInstalled => "Updates come with Zinc's installer: this copy wasn't installed by it.",
        UpdateStatus.Idle => $"Version {CurrentVersion}.",
        UpdateStatus.Checking => "Checking for updates…",
        // No number until there is one, because a server that doesn't say how big a file is (compressed on the fly)
        // leaves Velopack at 0 until the end.
        UpdateStatus.Downloading => Progress > 0 ? $"Downloading {NewVersion}: {Progress}%" : $"Downloading {NewVersion}…",
        UpdateStatus.Ready => $"{NewVersion} is ready: it installs when you restart Zinc, or the next time you quit.",
        UpdateStatus.UpToDate => $"Up to date ({CurrentVersion}). Checked {LastChecked:t}.",
        UpdateStatus.Failed => $"Couldn't check for updates: {Error}",
        _ => "",
    };

    private static void Set(UpdateStatus status)
    {
        _status = status;
        Changed?.Invoke();
    }
}
