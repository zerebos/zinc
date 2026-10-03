using System.Diagnostics;
using System.Runtime.InteropServices;
using Zinc.Input;

namespace Zinc.Terminal;

// Typing's timings for the debug log (Core InputLatency): only while the log is on, so typing pays nothing otherwise.
public sealed partial class TerminalControl
{
    private InputLatency? _inputLatency;

    /// <summary>The pane's typing trace while the debug log is on; null otherwise.</summary>
    private InputLatency? InputTrace => Log.Enabled ? _inputLatency ??= new InputLatency() : null;

    /// <summary>Milliseconds on a monotonic clock.</summary>
    private static double TraceNow => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// How long the message being handled (the key press) waited before the UI thread took it: Windows stamps each
    /// message when it's queued (GetMessageTime). -1 if that stamp isn't usable.
    /// </summary>
    private static double QueuedFor()
    {
        var waited = unchecked(Environment.TickCount - GetMessageTime());
        return waited is >= 0 and < 60_000 ? waited : -1;
    }

    /// <summary>Writes a stretch of typing's summary to the log once it's over.</summary>
    private void TraceSummary()
    {
        if (_inputLatency?.Summary(TraceNow) is { } line)
        {
            Log.Write(line);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial int GetMessageTime();
}
