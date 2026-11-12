using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Zinc;

/// <summary>
/// Diagnostics for a pointer shape that flickers (the palette's "Trace the pointer (15 s)", with logging on). For a while
/// it logs, as "pointer: ...": every change of the cursor on screen (polled every 2 ms on a thread of its own, with the
/// window under it), every WM_SETCURSOR one of Zinc's windows gets (hit-test code, what it answered, the cursor after),
/// and which XAML element pointer moves reach in each window. Then it takes everything back.
/// </summary>
internal static unsafe partial class PointerTrace
{
    private static readonly List<nint> Subclassed = [];
    private static readonly List<(UIElement Root, PointerEventHandler Handler)> Roots = [];
    private static volatile bool _running;
    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _stop;
    private static readonly Dictionary<nint, string> Names = [];
    private static string _lastSource = "";

    public static bool Running => _running;


    public static void Start(IEnumerable<UIElement> roots, TimeSpan length)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        NameSystemCursors();
        Log.Write($"pointer: trace for {length.TotalSeconds:0} s");

        // Every window of ours on this thread (main windows, settings, popups) and their children.
        var thread = GetCurrentThreadId();
        var pid = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (GetWindowThreadProcessId(h, out var owner) == thread && owner == pid)
            {
                Subclass(h);
                EnumChildWindows(h, (c, _) =>
                {
                    Subclass(c);
                    return true;
                }, 0);
            }

            return true;
        }, 0);

        foreach (var root in roots)
        {
            PointerEventHandler handler = (_, e) => NoteSource(e);
            root.AddHandler(UIElement.PointerMovedEvent, handler, handledEventsToo: true);
            Roots.Add((root, handler));
        }

        new Thread(Poll) { IsBackground = true, Name = "pointer trace" }.Start();
        _stop ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _stop.IsRepeating = false;
        _stop.Interval = length;
        _stop.OnTick(_ => Stop());
        _stop.Start();
    }

    private static void Stop()
    {
        _running = false;
        foreach (var h in Subclassed)
        {
            RemoveWindowSubclass(h, &SubclassProc, SubclassId);
        }

        Subclassed.Clear();
        foreach (var (root, handler) in Roots)
        {
            root.RemoveHandler(UIElement.PointerMovedEvent, handler);
        }

        Roots.Clear();
        Log.Write("pointer: trace done");
        Log.Flush();
    }

    private static void Subclass(nint hwnd)
    {
        if (SetWindowSubclass(hwnd, &SubclassProc, SubclassId, 0))
        {
            Subclassed.Add(hwnd);
            Log.Write($"pointer: watching {hwnd:X} {ClassOf(hwnd)}");
        }
    }

    private static void NoteSource(PointerRoutedEventArgs e)
    {
        var source = e.OriginalSource is FrameworkElement f
            ? f.GetType().Name + (f.Name != "" ? "#" + f.Name : "")
            : e.OriginalSource?.GetType().Name ?? "null";
        if (source != _lastSource)
        {
            _lastSource = source;
            Log.Write($"pointer: xaml move -> {source}");
        }
    }

    private const nuint SubclassId = 0x50545243; // "PTRC"
    private static nint _lastHwnd;
    private static uint _lastMsg;

    private static string? MessageName(uint msg) => msg switch
    {
        0x0200 => "WM_MOUSEMOVE", 0x0201 => "WM_LBUTTONDOWN", 0x0202 => "WM_LBUTTONUP", 0x0203 => "WM_LBUTTONDBLCLK",
        0x0215 => "WM_CAPTURECHANGED", 0x02A1 => "WM_MOUSEHOVER", 0x02A3 => "WM_MOUSELEAVE",
        0x00A0 => "WM_NCMOUSEMOVE", 0x00A1 => "WM_NCLBUTTONDOWN",
        0x0241 => "WM_POINTERENTER", 0x0242 => "WM_POINTERLEAVE", 0x0245 => "WM_POINTERUPDATE", 0x0246 => "WM_POINTERDOWN",
        0x0247 => "WM_POINTERUP", 0x0249 => "WM_POINTERENTER(nc)", 0x024C => "WM_POINTERCAPTURECHANGED",
        0x024D => "WM_POINTERACTIVATE",
        _ => null,
    };

    [LibraryImport("user32.dll")]
    private static partial nint GetCapture();

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (MessageName(msg) is { } input)
        {
            // Mouse and pointer messages: which window gets them, and who holds the capture (repeats skipped).
            if (hwnd != _lastHwnd || msg != _lastMsg)
            {
                _lastHwnd = hwnd;
                _lastMsg = msg;
                try
                {
                    Log.Write($"pointer: {input} -> {hwnd:X} {ClassOf(hwnd)} (capture {GetCapture():X})");
                }
                catch
                {
                }
            }
        }

        if (msg != 0x0020 /* WM_SETCURSOR */)
        {
            return DefSubclassProc(hwnd, msg, wParam, lParam);
        }

        var result = DefSubclassProc(hwnd, msg, wParam, lParam);
        try
        {
            Log.Write($"pointer: WM_SETCURSOR {hwnd:X} {ClassOf(hwnd)} hit {(short)(lParam & 0xFFFF)} mouse msg {(lParam >> 16) & 0xFFFF:X} -> {result} cursor {Name(GetCursor())}");
        }
        catch
        {
        }

        return result;
    }

    private static void Poll()
    {
        nint last = 0;
        while (_running)
        {
            var info = new CursorInfo { Size = sizeof(CursorInfo) };
            if (GetCursorInfo(ref info) && info.Cursor != last)
            {
                last = info.Cursor;
                var under = WindowFromPoint(new Pt { X = info.X, Y = info.Y });
                Log.Write($"pointer: shown {Name(info.Cursor)} at {info.X},{info.Y} over {under:X} {ClassOf(under)}{(info.Flags == 0 ? " (hidden)" : "")}");
            }

            Thread.Sleep(2);
        }
    }

    private static void NameSystemCursors()
    {
        foreach (var (id, name) in new[]
        {
            (32512, "arrow"), (32513, "ibeam"), (32514, "wait"), (32515, "cross"), (32642, "size-nwse"), (32643, "size-nesw"),
            (32644, "size-we"), (32645, "size-ns"), (32646, "size-all"), (32649, "hand"), (32650, "app-starting"),
        })
        {
            Names[LoadCursorW(0, id)] = name;
        }
    }

    private static string Name(nint cursor)
    {
        lock (Names)
        {
            return cursor == 0 ? "none" : Names.TryGetValue(cursor, out var n) ? n : $"other {cursor:X}";
        }
    }

    private static string ClassOf(nint hwnd)
    {
        var buffer = stackalloc char[64];
        var n = GetClassNameW(hwnd, buffer, 64);
        return n > 0 ? new string(buffer, 0, n) : "?";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public nint Cursor;
        public int X, Y;
    }

    private delegate bool EnumProc(nint hwnd, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumProc proc, nint data);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassNameW(nint hwnd, char* buffer, int max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorInfo(ref CursorInfo info);

    [LibraryImport("user32.dll")]
    private static partial nint GetCursor();

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint name);

    [StructLayout(LayoutKind.Sequential)]
    private struct Pt
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(Pt point);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
}
