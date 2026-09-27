using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;

namespace Phosphor.Terminal;

/// <summary>
/// Keeps retired CanvasControls alive while Win2D still has them registered with a window.
/// <para>
/// Win2D (BaseControl.h) registers a canvas for its XamlRoot's Changed event on <em>every</em>
/// Loaded and unregisters it only when an Unloaded brings its load count to exactly zero;
/// nothing unregisters it when the canvas is destroyed. XAML sometimes reports a removal twice
/// (see TerminalControl's canvas replacement), after which the count never returns to zero, and
/// a canvas freed in that state leaves a dangling handler: the window's next XamlRoot change
/// (minimize, restore, DPI) locks freed memory, which hung the app (2026-09-27 dump).
/// </para>
/// <para>
/// So every canvas is created here, its events are mirrored with Win2D's rules, and a retired
/// canvas is held until an Unloaded unregisters it — for good, if that never happens (a small,
/// rare leak: its device resources are released and its owner's handlers are removed).
/// All on the UI thread.
/// </para>
/// </summary>
internal static class CanvasLifetime
{
    private sealed class State
    {
        public int Loads;
        public bool Registered;
        public bool Retired;
    }

    private static readonly Dictionary<CanvasControl, State> States = new(ReferenceEqualityComparer.Instance);

    /// <summary>Canvases retired but still registered (kept alive by <see cref="States"/>).</summary>
    public static int Held => States.Values.Count(s => s.Retired);

    public static CanvasControl Create()
    {
        var canvas = new CanvasControl();
        States[canvas] = new State();
        // Subscribed before the owner's handlers, so the owner sees the updated count.
        canvas.Loaded += OnLoaded;
        canvas.Unloaded += OnUnloaded;
        return canvas;
    }

    /// <summary>Win2D's load count for <paramref name="canvas"/> (it draws only while it's above zero).</summary>
    public static int LoadCount(CanvasControl canvas) => States.TryGetValue(canvas, out var s) ? s.Loads : 0;

    /// <summary>
    /// Done with <paramref name="canvas"/> (its owner removed its own handlers): forget it once
    /// Win2D has unregistered it, which may be now or at a pending Unloaded.
    /// </summary>
    public static void Retire(CanvasControl canvas)
    {
        if (!States.TryGetValue(canvas, out var s))
        {
            return;
        }

        s.Retired = true;
        ForgetIfDone(canvas, s);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var canvas = (CanvasControl)sender;
        if (States.TryGetValue(canvas, out var s))
        {
            s.Loads++;
            s.Registered = true; // OnLoaded always re-registers
            Note(canvas, s, "Loaded");
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var canvas = (CanvasControl)sender;
        if (!States.TryGetValue(canvas, out var s))
        {
            return;
        }

        if (--s.Loads == 0)
        {
            s.Registered = false; // OnUnloaded unregisters only on reaching zero
        }

        Note(canvas, s, "Unloaded");

        ForgetIfDone(canvas, s);
    }

    /// <summary>Logs counts outside 0..1: XAML's events got unbalanced (worth knowing which path did it).</summary>
    private static void Note(CanvasControl canvas, State s, string what)
    {
        if (s.Loads is < 0 or > 1)
        {
            Log.Write($"canvas {canvas.GetHashCode():x}: {what} left its load count at {s.Loads}{(s.Retired ? " (retired)" : "")}");
        }
    }

    private static void ForgetIfDone(CanvasControl canvas, State s)
    {
        if (s.Retired && !s.Registered)
        {
            canvas.Loaded -= OnLoaded;
            canvas.Unloaded -= OnUnloaded;
            States.Remove(canvas);
        }
        else if (s.Retired)
        {
            Log.Write($"canvas: retired while still registered with its window (loads {s.Loads}); keeping it alive ({Held} held)");
        }
    }
}
