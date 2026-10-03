using Velopack;

namespace Zinc;

/// <summary>
/// Updates for copies the installer put in place (Velopack): check, download in the background, apply on restart.
/// A copy it didn't install (the dev install, a dev build, a zip) has no updates. The source is
/// <c>ZINC_UPDATE_URL</c> (tests: a local folder or a URL), else the address built into release builds.
/// </summary>
internal static class Updater
{
    /// <summary>Where release builds look for updates (set when hosting exists; empty: none).</summary>
    private const string BuiltInSource = "";

    public static string? Source =>
        Environment.GetEnvironmentVariable("ZINC_UPDATE_URL") is { Length: > 0 } url ? url
        : BuiltInSource.Length > 0 ? BuiltInSource : null;

    private static UpdateManager? _manager;

    /// <summary>The installer's update manager, or null when this copy can't update (not installed by it, or no source).</summary>
    public static UpdateManager? Manager
    {
        get
        {
            if (_manager == null && Source is { } source)
            {
                try
                {
                    var manager = new UpdateManager(source);
                    _manager = manager.IsInstalled ? manager : null;
                }
                catch (Exception ex)
                {
                    Log.Write($"update: no update manager ({ex.Message})");
                }
            }

            return _manager;
        }
    }

    /// <summary>The newer version, downloaded and ready to apply; null when there's none (or no updates here).</summary>
    public static async Task<UpdateInfo?> CheckAndDownloadAsync()
    {
        if (Manager is not { } manager)
        {
            Log.Write("update: this copy wasn't installed by the installer (or has no update source)");
            return null;
        }

        var update = await manager.CheckForUpdatesAsync();
        if (update == null)
        {
            Log.Write($"update: up to date ({manager.CurrentVersion})");
            return null;
        }

        Log.Write($"update: {manager.CurrentVersion} -> {update.TargetFullRelease.Version}, downloading");
        await manager.DownloadUpdatesAsync(update);
        Log.Write($"update: {update.TargetFullRelease.Version} downloaded");
        return update;
    }

    /// <summary>Exits, lets the installer swap the files, and starts the new version.</summary>
    public static void ApplyAndRestart(UpdateInfo update)
    {
        Log.Write($"update: applying {update.TargetFullRelease.Version} and restarting");
        Manager!.ApplyUpdatesAndRestart(update);
    }
}
