using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
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
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance())
        {
            return 0;
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
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
