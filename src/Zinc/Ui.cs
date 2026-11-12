using Microsoft.UI.Dispatching;

namespace Zinc;

/// <summary>
/// Running work where an exception mustn't end the app. A DispatcherQueue callback or a
/// DispatcherQueueTimer tick doesn't go through Application.UnhandledException, so an exception
/// escaping one ends the process on the spot, with nothing logged (test:throw shows this). Every
/// such callback goes through <see cref="Guard"/>, which records the exception (crash.log) and
/// shows it, and the app carries on.
/// </summary>
internal static class Ui
{
    public static void Guard(string where, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            Log.Crash(where, ex);
            App.Instance.ShowSurvivedError(ex);
        }
    }

    /// <summary><see cref="DispatcherQueue.TryEnqueue(DispatcherQueueHandler)"/>, guarded.</summary>
    public static bool SafeEnqueue(this DispatcherQueue queue, Action work) =>
        queue.TryEnqueue(() => Guard("queued work", work));

    /// <inheritdoc cref="SafeEnqueue(DispatcherQueue, Action)"/>
    public static bool SafeEnqueue(this DispatcherQueue queue, DispatcherQueuePriority priority, Action work) =>
        queue.TryEnqueue(priority, () => Guard("queued work", work));

    /// <summary>A timer's Tick, guarded.</summary>
    public static void OnTick(this DispatcherQueueTimer timer, Action<DispatcherQueueTimer> tick) =>
        timer.Tick += (sender, _) => Guard("timer", () => tick(sender));
}
