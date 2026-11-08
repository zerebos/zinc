using Zinc.Ghostty;

namespace Zinc.Terminal;

public enum GesturePhase
{
    Press,
    Drag,
    Release,
}

/// <summary>
/// Mouse selection for one terminal (drag, double-click word, triple-click line),
/// driven by libghostty's selection gesture state machine.
/// </summary>
public sealed unsafe class SelectionGesture : IDisposable
{
    /// <summary>Max interval between clicks of a double/triple click.</summary>
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(500);

    private readonly TerminalSession _session;
    private readonly nint _gesture, _press, _drag, _release;
    private bool _disposed;

    public SelectionGesture(TerminalSession session)
    {
        _session = session;
        Check(Native.SelectionGestureNew(0, out _gesture));
        Check(Native.SelectionGestureEventNew(0, out _press, SelectionGestureEventType.Press));
        Check(Native.SelectionGestureEventNew(0, out _drag, SelectionGestureEventType.Drag));
        Check(Native.SelectionGestureEventNew(0, out _release, SelectionGestureEventType.Release));
    }

    /// <summary>Ghostty's own word boundaries (its <c>selection-word-chars</c> default), spelled out so "back to the default" is exact.</summary>
    public const string GhosttyWordBoundaries = "\t '\"│`|:;,()[]{}<>$";

    /// <summary>
    /// The characters that end a word on double-click for config <c>word-boundaries</c>: space
    /// and tab, and each of <paramref name="text"/>'s; Ghostty's own set when it's null.
    /// </summary>
    public static uint[] Boundaries(string? text) =>
        [.. (text == null ? GhosttyWordBoundaries : " \t" + text).EnumerateRunes().Select(r => (uint)r.Value).Distinct()];

    /// <summary>Sets the characters that end a word on double-click (<see cref="Boundaries"/>).</summary>
    public void SetWordBoundaries(uint[] codepoints)
    {
        lock (_session.Lock)
        {
            if (_disposed)
            {
                return;
            }

            // Copied by libghostty into each event (word selection happens on press and drag).
            fixed (uint* p = codepoints)
            {
                var list = new GhosttyCodepoints { Ptr = p, Len = (nuint)codepoints.Length };
                Native.SelectionGestureEventSet(_press, SelectionGestureEventOption.WordBoundaryCodepoints, &list);
                Native.SelectionGestureEventSet(_drag, SelectionGestureEventOption.WordBoundaryCodepoints, &list);
            }
        }
    }

    /// <summary>
    /// Feeds one pointer event at viewport cell (<paramref name="col"/>, <paramref name="row"/>)
    /// and applies the resulting selection to the terminal.
    /// </summary>
    /// <param name="xPx">Pointer position in surface pixels (including padding).</param>
    /// <param name="timestampNs">Monotonic time; enables multi-click detection on press.</param>
    /// <param name="repeatDistancePx">Max pointer travel between clicks of a multi-click.</param>
    public void Handle(GesturePhase phase, ushort col, uint row, double xPx, double yPx,
        GhosttySelectionGestureGeometry geometry, ulong timestampNs, double repeatDistancePx)
    {
        var ev = phase switch
        {
            GesturePhase.Press => _press,
            GesturePhase.Drag => _drag,
            _ => _release,
        };

        lock (_session.Lock)
        {
            if (_disposed || _session.IsDisposed)
            {
                return;
            }

            var terminal = _session.Handle;

            // Grid refs are untracked and only valid until the next terminal
            // mutation, so resolve, run the gesture and apply it under one lock.
            var gridRef = GhosttyGridRef.Init();
            var point = new GhosttyPoint { Tag = PointTag.Viewport, X = col, Y = row };
            if (Native.TerminalGridRef(terminal, point, &gridRef) != GhosttyResult.Success)
            {
                return;
            }

            Native.SelectionGestureEventSet(ev, SelectionGestureEventOption.Ref, &gridRef);
            var position = new GhosttySurfacePosition { X = xPx, Y = yPx };
            Native.SelectionGestureEventSet(ev, SelectionGestureEventOption.Position, &position);
            Native.SelectionGestureEventSet(ev, SelectionGestureEventOption.Geometry, &geometry);

            if (phase == GesturePhase.Press)
            {
                // Timestamps enable double-click (word) and triple-click (line) selection.
                Native.SelectionGestureEventSet(ev, SelectionGestureEventOption.TimeNs, &timestampNs);
                var intervalNs = (ulong)(RepeatInterval.Ticks * 100);
                Native.SelectionGestureEventSet(ev, SelectionGestureEventOption.RepeatIntervalNs, &intervalNs);
                Native.SelectionGestureEventSet(ev, SelectionGestureEventOption.RepeatDistance, &repeatDistancePx);
            }

            var selection = GhosttySelection.Init();
            var result = Native.SelectionGestureEvent(_gesture, terminal, ev, &selection);
            if (result == GhosttyResult.Success)
            {
                Native.TerminalSet(terminal, TerminalOption.Selection, &selection);
            }
            else if (phase == GesturePhase.Press && result == GhosttyResult.NoValue)
            {
                // A plain click clears any existing selection.
                Native.TerminalSet(terminal, TerminalOption.Selection, null);
            }
        }
    }

    public void Dispose()
    {
        lock (_session.Lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Native.SelectionGestureEventFree(_press);
            Native.SelectionGestureEventFree(_drag);
            Native.SelectionGestureEventFree(_release);
            Native.SelectionGestureFree(_gesture, _session.Handle);
        }
    }

    private static void Check(GhosttyResult result)
    {
        if (result != GhosttyResult.Success)
        {
            throw new InvalidOperationException($"libghostty-vt call failed: {result}");
        }
    }
}
