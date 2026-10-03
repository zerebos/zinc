using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Velopack;
using Velopack.Locators;
using Zinc.Session;

namespace Zinc;

/// <summary>
/// Entry point (replaces WinUI's generated Main; DISABLE_XAML_GENERATED_MAIN). Zinc runs once
/// per session file: a second launch hands its activation — command line included — to the
/// running instance, which opens a window for it (see <see cref="App.OnRedirectedLaunch"/>), and
/// exits. Runs without a session (tests: ZINC_CONFIG set) stay separate processes.
/// </summary>
public static partial class Program
{
    [STAThread]
    private static int Main()
    {
        // First thing (Velopack's rule): the installer starts zinc.exe with its own arguments after install,
        // before uninstall and around updates; Run handles those and exits. Normal launches go on.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => BeforeUninstall())
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance())
        {
            return 0;
        }

        // Only in the process that runs (not a launch handed to a running Zinc, which may still be writing
        // them), and not in test runs.
        if (Environment.GetEnvironmentVariable("ZINC_CONFIG") is not { Length: > 0 })
        {
            MoveOldLocalData();
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }

    /// <summary>Logs, crash log and icon cache used to live in %LOCALAPPDATA%\Zinc, now the installer's folder (AppFolders).</summary>
    private static void MoveOldLocalData()
    {
        try
        {
            AppFolders.MoveOldLocalData(AppFolders.OldLocal, AppFolders.Data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next start.
        }
    }

    /// <summary>
    /// The installer is about to remove this install (it gives us 30 seconds): take back the sign-in entry and the
    /// default-terminal registration where they point into it. Another copy's (the dev install's) stay.
    /// </summary>
    private static void BeforeUninstall()
    {
        try
        {
            // The install root: the locator's, else the folder above this exe's ("current").
            var root = (VelopackLocator.IsCurrentSet ? VelopackLocator.Current.RootAppDir : null)
                ?? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!;
            Log.Write($"uninstall: taking back what points into {root}");
            foreach (var step in Zinc.Pty.InstallCleanup.Uninstall(Microsoft.Win32.Registry.CurrentUser, root))
            {
                Log.Write($"uninstall: {step}");
            }
        }
        catch (Exception ex)
        {
            Log.Crash("uninstall", ex);
        }
    }

    /// <summary>
    /// Registers this process as its session's instance, or — when another already is — passes
    /// this launch to it. True when this process should exit.
    /// </summary>
    private static bool RedirectToRunningInstance()
    {
        if (SessionStore.DefaultPath() is not { } sessionPath)
        {
            return false;
        }

        try
        {
            var instance = AppInstance.FindOrRegisterForKey(SessionStore.InstanceKey(sessionPath));
            if (instance.IsCurrent)
            {
                // Later launches arrive here, on a background thread; App marshals them.
                instance.Activated += (_, args) => App.OnRedirectedLaunch(args);
                return false;
            }

            // Let the running instance bring its new window to the front (we have foreground
            // rights as the process just launched; it doesn't).
            AllowSetForegroundWindow(instance.ProcessId);
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();

            // Waiting on the redirect must keep pumping COM on this STA thread (a plain Wait can
            // deadlock), hence a worker thread and CoWaitForMultipleObjects, as the SDK docs do.
            using var done = new ManualResetEvent(false);
            _ = Task.Run(() =>
            {
                try
                {
                    instance.RedirectActivationToAsync(args).AsTask().Wait();
                }
                finally
                {
                    done.Set();
                }
            });
            CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done.SafeWaitHandle.DangerousGetHandle()], out _);
            return true;
        }
        catch (Exception ex)
        {
            // Run on our own (App's session lock still keeps the session with the first process).
            Log.Write($"single instance: {ex.Message}");
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("ole32.dll")]
    private static partial int CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);
}
