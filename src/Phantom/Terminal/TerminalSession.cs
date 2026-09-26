using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Phantom.Ghostty;
using Phantom.Pty;

namespace Phantom.Terminal;

/// <summary>
/// Binds a libghostty-vt terminal to a ConPTY child process.
///
/// Threading: PTY output is parsed on the ConPTY reader thread while the UI
/// thread renders and encodes input. libghostty-vt handles are not thread
/// safe, so every call touching <see cref="Handle"/> must hold <see cref="Lock"/>.
/// </summary>
public sealed unsafe class TerminalSession : IDisposable
{
    public readonly object Lock = new();

    private readonly nint _terminal;
    private readonly nint _keyEncoder;
    private readonly nint _keyEvent;
    private readonly nint _mouseEncoder;
    private readonly nint _mouseEvent;
    private GCHandle _self;
    private ConPty? _pty;
    private bool _titleDirty;
    private bool _bell;
    private bool _disposed;

    /// <summary>New output was parsed; the view should redraw. Raised on the reader thread.</summary>
    public event Action? OutputProcessed;

    /// <summary>The OSC 0/2 title changed. Raised on the reader thread.</summary>
    public event Action<string>? TitleChanged;

    /// <summary>BEL received. Raised on the reader thread.</summary>
    public event Action? Bell;

    /// <summary>The child process exited. Raised on a background thread.</summary>
    public event Action<uint>? ProcessExited;

    public TerminalSession(ushort cols, ushort rows, TerminalTheme theme)
    {
        Check(Native.TerminalNew(0, out _terminal, cols, rows));
        _self = GCHandle.Alloc(this);

        Native.TerminalSet(_terminal, TerminalOption.Userdata, (void*)GCHandle.ToIntPtr(_self));
        Native.TerminalSet(_terminal, TerminalOption.WritePty,
            (delegate* unmanaged[Cdecl]<nint, nint, byte*, nuint, void>)&OnWritePty);
        Native.TerminalSet(_terminal, TerminalOption.TitleChanged,
            (delegate* unmanaged[Cdecl]<nint, nint, void>)&OnTitleChanged);
        Native.TerminalSet(_terminal, TerminalOption.Bell,
            (delegate* unmanaged[Cdecl]<nint, nint, void>)&OnBell);

        var fg = theme.Foreground;
        var bg = theme.Background;
        var cursor = theme.Cursor;
        Native.TerminalSet(_terminal, TerminalOption.ColorForeground, &fg);
        Native.TerminalSet(_terminal, TerminalOption.ColorBackground, &bg);
        Native.TerminalSet(_terminal, TerminalOption.ColorCursor, &cursor);

        if (ConPty.UsesBundledConpty)
        {
            // The bundled ConPTY measures widths by grapheme cluster; do the same
            // so emoji sequences (ZWJ, flags, skin tones) occupy matching cells.
            var grapheme = new GhosttyTerminalModeConfig { Mode = GhosttyMode.GraphemeCluster, Value = 1 };
            Native.TerminalSet(_terminal, TerminalOption.ModeDefault, &grapheme);
        }

        nuint scrollback = 10_000;
        Native.TerminalSet(_terminal, TerminalOption.ScrollbackMaxLines, &scrollback);

        Check(Native.KeyEncoderNew(0, out _keyEncoder));
        Check(Native.KeyEventNew(0, out _keyEvent));
        Check(Native.MouseEncoderNew(0, out _mouseEncoder));
        Check(Native.MouseEventNew(0, out _mouseEvent));
    }

    /// <summary>The raw GhosttyTerminal handle. Only use while holding <see cref="Lock"/>.</summary>
    public nint Handle => _terminal;

    public bool HasExited { get; private set; }

    public void Start(string commandLine, string? workingDirectory, ushort cols, ushort rows)
    {
        // Children inherit our environment; advertise what the terminal supports.
        Environment.SetEnvironmentVariable("COLORTERM", "truecolor");
        Environment.SetEnvironmentVariable("TERM_PROGRAM", "phantom");

        _pty = new ConPty(commandLine, workingDirectory, (short)cols, (short)rows);
        _pty.OutputReceived += OnPtyOutput;
        _pty.Exited += code =>
        {
            HasExited = true;
            ProcessExited?.Invoke(code);
        };
    }

    // Set PHANTOM_TRACE=<file> to record raw PTY output for debugging.
    private readonly FileStream? _trace = Environment.GetEnvironmentVariable("PHANTOM_TRACE") is { Length: > 0 } path
        ? new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)
        : null;

    private void OnPtyOutput(ReadOnlySpan<byte> data)
    {
        if (_trace != null)
        {
            _trace.Write(data);
            _trace.Flush();
        }

        string? title = null;
        bool bell;
        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            fixed (byte* p = data)
            {
                Native.TerminalVtWrite(_terminal, p, (nuint)data.Length);
            }

            // Callbacks fire mid-parse, so read derived state only once the
            // write has finished.
            if (_titleDirty)
            {
                _titleDirty = false;
                title = ReadTitle();
            }

            bell = _bell;
            _bell = false;
        }

        if (title != null)
        {
            TitleChanged?.Invoke(title);
        }

        if (bell)
        {
            Bell?.Invoke();
        }

        OutputProcessed?.Invoke();
    }

    private string ReadTitle()
    {
        GhosttyString s;
        if (Native.TerminalGet(_terminal, TerminalData.Title, &s) != GhosttyResult.Success || s.Len == 0)
        {
            return "";
        }

        return Encoding.UTF8.GetString((byte*)s.Ptr, (int)s.Len);
    }

    /// <summary>Writes VT data straight to the screen, as if the child had printed it.</summary>
    public void WriteToScreen(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            fixed (byte* p = bytes)
            {
                Native.TerminalVtWrite(_terminal, p, (nuint)bytes.Length);
            }
        }

        OutputProcessed?.Invoke();
    }

    /// <summary>Sends user input to the child, snapping the viewport back to the live screen.</summary>
    public void SendInput(ReadOnlySpan<byte> data, bool scrollToBottom = true)
    {
        if (data.IsEmpty)
        {
            return;
        }

        if (scrollToBottom)
        {
            ScrollViewport(new GhosttyTerminalScrollViewport { Tag = ScrollViewportTag.Bottom });
        }

        _pty?.Write(data);
    }

    public void SendInput(string text) => SendInput(Encoding.UTF8.GetBytes(text));

    public void Resize(ushort cols, ushort rows, uint cellWidthPx, uint cellHeightPx)
    {
        lock (Lock)
        {
            Native.TerminalResize(_terminal, cols, rows, cellWidthPx, cellHeightPx);
        }

        _pty?.Resize((short)cols, (short)rows);
    }

    public void ScrollViewport(GhosttyTerminalScrollViewport behavior)
    {
        lock (Lock)
        {
            Native.TerminalScrollViewport(_terminal, behavior);
        }
    }

    public void ScrollLines(int delta) =>
        ScrollViewport(new GhosttyTerminalScrollViewport { Tag = ScrollViewportTag.Delta, Value = delta });

    public GhosttyTerminalScrollbar GetScrollbar()
    {
        GhosttyTerminalScrollbar sb;
        lock (Lock)
        {
            Native.TerminalGet(_terminal, TerminalData.Scrollbar, &sb);
        }

        return sb;
    }

    public bool GetMode(ushort mode)
    {
        var cfg = new GhosttyTerminalModeConfig { Mode = mode };
        lock (Lock)
        {
            Native.TerminalGet(_terminal, TerminalData.Mode, &cfg);
        }

        return cfg.Value != 0;
    }

    public bool IsMouseTracking
    {
        get
        {
            byte tracking;
            lock (Lock)
            {
                Native.TerminalGet(_terminal, TerminalData.MouseTracking, &tracking);
            }

            return tracking != 0;
        }
    }

    public byte KittyKeyboardFlags
    {
        get
        {
            byte flags;
            lock (Lock)
            {
                Native.TerminalGet(_terminal, TerminalData.KittyKeyboardFlags, &flags);
            }

            return flags;
        }
    }

    /// <summary>
    /// Encodes a key event using the terminal's current keyboard modes
    /// (cursor key mode, kitty flags, ...). Returns an empty array when the
    /// key produces no input.
    /// </summary>
    public byte[] EncodeKey(KeyAction action, GhosttyKey key, GhosttyMods mods, GhosttyMods consumedMods,
        string? text, uint unshiftedCodepoint)
    {
        Span<byte> utf8 = stackalloc byte[32];
        var utf8Len = 0;
        if (!string.IsNullOrEmpty(text))
        {
            utf8Len = Encoding.UTF8.GetBytes(text, utf8);
        }

        Span<byte> output = stackalloc byte[128];
        nuint written;
        GhosttyResult result;
        lock (Lock)
        {
            Native.KeyEncoderSetoptFromTerminal(_keyEncoder, _terminal);
            Native.KeyEventSetAction(_keyEvent, action);
            Native.KeyEventSetKey(_keyEvent, key);
            Native.KeyEventSetMods(_keyEvent, mods);
            Native.KeyEventSetConsumedMods(_keyEvent, consumedMods);
            Native.KeyEventSetUnshiftedCodepoint(_keyEvent, unshiftedCodepoint);

            fixed (byte* pText = utf8)
            fixed (byte* pOut = output)
            {
                // The event borrows the text pointer, so encode inside the same fixed scope.
                Native.KeyEventSetUtf8(_keyEvent, utf8Len > 0 ? pText : null, (nuint)utf8Len);
                result = Native.KeyEncoderEncode(_keyEncoder, _keyEvent, pOut, (nuint)output.Length, out written);
                Native.KeyEventSetUtf8(_keyEvent, null, 0);
            }
        }

        return result == GhosttyResult.Success ? output[..(int)written].ToArray() : [];
    }

    /// <summary>Encodes a mouse event for the application's active mouse reporting mode.</summary>
    public byte[] EncodeMouse(MouseAction action, MouseButton? button, GhosttyMods mods, float xPx, float yPx,
        GhosttyMouseEncoderSize size, bool anyButtonPressed)
    {
        Span<byte> output = stackalloc byte[64];
        nuint written;
        GhosttyResult result;
        lock (Lock)
        {
            Native.MouseEncoderSetoptFromTerminal(_mouseEncoder, _terminal);
            size.Size = (nuint)sizeof(GhosttyMouseEncoderSize);
            Native.MouseEncoderSetopt(_mouseEncoder, MouseEncoderOption.Size, &size);
            var pressed = (byte)(anyButtonPressed ? 1 : 0);
            Native.MouseEncoderSetopt(_mouseEncoder, MouseEncoderOption.AnyButtonPressed, &pressed);
            // Only report motion when the pointer crosses into a new cell.
            byte trackLastCell = 1;
            Native.MouseEncoderSetopt(_mouseEncoder, MouseEncoderOption.TrackLastCell, &trackLastCell);

            Native.MouseEventSetAction(_mouseEvent, action);
            if (button is { } b)
            {
                Native.MouseEventSetButton(_mouseEvent, b);
            }
            else
            {
                Native.MouseEventClearButton(_mouseEvent);
            }

            Native.MouseEventSetMods(_mouseEvent, mods);
            Native.MouseEventSetPosition(_mouseEvent, new GhosttyMousePosition { X = xPx, Y = yPx });

            fixed (byte* pOut = output)
            {
                result = Native.MouseEncoderEncode(_mouseEncoder, _mouseEvent, pOut, (nuint)output.Length, out written);
            }
        }

        return result == GhosttyResult.Success ? output[..(int)written].ToArray() : [];
    }

    /// <summary>Pastes text, honoring bracketed paste mode and stripping unsafe control bytes.</summary>
    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var bracketed = (byte)(GetMode(GhosttyMode.BracketedPaste) ? 1 : 0);
        var data = Encoding.UTF8.GetBytes(text);
        // Bracketing adds 12 bytes and newline conversion never grows the
        // data, but retry with the reported size rather than rely on that.
        var output = new byte[data.Length + 16];
        while (true)
        {
            nuint written;
            GhosttyResult result;
            fixed (byte* pData = data)
            fixed (byte* pOut = output)
            {
                result = Native.PasteEncode(pData, (nuint)data.Length, bracketed, pOut, (nuint)output.Length, out written);
            }

            if (result == GhosttyResult.OutOfSpace)
            {
                output = new byte[(int)written];
                continue;
            }

            if (result == GhosttyResult.Success)
            {
                SendInput(output.AsSpan(0, (int)written));
            }

            return;
        }
    }

    /// <summary>Returns the selected text, or null when nothing is selected.</summary>
    public string? GetSelectionText()
    {
        lock (Lock)
        {
            var opts = new GhosttyTerminalSelectionFormatOptions
            {
                Size = (nuint)sizeof(GhosttyTerminalSelectionFormatOptions),
                Emit = 0,
                Unwrap = 1,
                Trim = 1,
                Selection = 0,
            };

            if (Native.TerminalSelectionFormatAlloc(_terminal, 0, opts, out var ptr, out var len) != GhosttyResult.Success)
            {
                return null;
            }

            try
            {
                return len == 0 ? null : Encoding.UTF8.GetString(ptr, (int)len);
            }
            finally
            {
                Native.Free(0, ptr, len);
            }
        }
    }

    public void ClearSelection()
    {
        lock (Lock)
        {
            Native.TerminalSet(_terminal, TerminalOption.Selection, null);
        }
    }

    // ---- libghostty callbacks (invoked on whichever thread is calling into the terminal) ----

    private static TerminalSession FromUserdata(nint userdata) =>
        (TerminalSession)GCHandle.FromIntPtr(userdata).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWritePty(nint terminal, nint userdata, byte* data, nuint len)
    {
        // Replies to queries (DSR, DA, DECRQM...). ConPTY itself sends a DSR
        // cursor position query at startup and waits for this answer.
        FromUserdata(userdata)._pty?.Write(new ReadOnlySpan<byte>(data, (int)len));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnTitleChanged(nint terminal, nint userdata) => FromUserdata(userdata)._titleDirty = true;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnBell(nint terminal, nint userdata) => FromUserdata(userdata)._bell = true;

    private static void Check(GhosttyResult result)
    {
        if (result != GhosttyResult.Success)
        {
            throw new InvalidOperationException($"libghostty-vt call failed: {result}");
        }
    }

    public void Dispose()
    {
        _pty?.Dispose();
        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Native.KeyEventFree(_keyEvent);
            Native.KeyEncoderFree(_keyEncoder);
            Native.MouseEventFree(_mouseEvent);
            Native.MouseEncoderFree(_mouseEncoder);
            Native.TerminalFree(_terminal);
            _self.Free();
        }
    }
}
