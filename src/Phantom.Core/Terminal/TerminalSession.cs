using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly nint _placements;
    private GCHandle _self;
    private ConPty? _pty;
    private GhosttySizeReportSize _size;
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

    /// <summary>
    /// Receives text a program copies with OSC 52. Called on the reader thread while the
    /// terminal is locked, so hand the text off rather than touch the terminal. Null denies.
    /// </summary>
    public Action<string>? ClipboardWriter { get; set; }

    /// <summary>
    /// Answers OSC 52 clipboard reads with the clipboard's text (null when it can't be read).
    /// Called synchronously on the reader thread while the terminal is locked. Null denies.
    /// </summary>
    public Func<string?>? ClipboardReader { get; set; }

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
        // Without this, libghostty suppresses XTWINOPS size replies (CSI 14/16/18 t)
        // and the report sent when an app enables in-band resize (mode 2048).
        Native.TerminalSet(_terminal, TerminalOption.Size,
            (delegate* unmanaged[Cdecl]<nint, nint, GhosttySizeReportSize*, byte>)&OnSize);
        _size = new GhosttySizeReportSize { Columns = cols, Rows = rows };
        // OSC 52 (and OSC 1337 Copy / OSC 5522) clipboard access; see ClipboardWriter/Reader.
        Native.TerminalSet(_terminal, TerminalOption.ClipboardWrite,
            (delegate* unmanaged[Cdecl]<nint, nint, GhosttyClipboardWrite*, void>)&OnClipboardWrite);
        Native.TerminalSet(_terminal, TerminalOption.ClipboardRead,
            (delegate* unmanaged[Cdecl]<nint, nint, GhosttyClipboardRead*, void>)&OnClipboardRead);

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

        if (theme.Palette.Count > 0)
        {
            // libghostty takes the whole 256-color table: start from its defaults.
            var palette = new GhosttyColorRgb[256];
            fixed (GhosttyColorRgb* p = palette)
            {
                Native.TerminalGet(_terminal, TerminalData.ColorPaletteDefault, p);
                foreach (var (index, color) in theme.Palette)
                {
                    p[index] = color;
                }

                Native.TerminalSet(_terminal, TerminalOption.ColorPalette, p);
            }
        }

        nuint scrollback = 10_000;
        Native.TerminalSet(_terminal, TerminalOption.ScrollbackMaxLines, &scrollback);

        // Kitty graphics: on once storage is allowed. Besides inline data (t=d), programs may
        // name a file to load (t=f; fastfetch's kitty-direct) or a temp file we delete after
        // (t=t, only inside %TEMP%). A program can read those files itself anyway; paths from
        // WSL or a remote host simply fail to load. Shared memory stays off.
        KittyImages.InstallPngDecoder();
        var storageLimit = KittyImages.StorageLimit;
        Native.TerminalSet(_terminal, TerminalOption.KittyImageStorageLimit, &storageLimit);
        byte fileMedium = 1;
        Native.TerminalSet(_terminal, TerminalOption.KittyImageMediumFile, &fileMedium);
        var temp = Encoding.UTF8.GetBytes(Path.GetTempPath());
        fixed (byte* p = temp)
        {
            var tempDir = new GhosttyString { Ptr = (nint)p, Len = (nuint)temp.Length };
            Native.TerminalSet(_terminal, TerminalOption.KittyImageMediumTempFile, &tempDir);
        }
        Check(Native.KittyPlacementIteratorNew(0, out _placements));

        Check(Native.KeyEncoderNew(0, out _keyEncoder));
        Check(Native.KeyEventNew(0, out _keyEvent));
        Check(Native.MouseEncoderNew(0, out _mouseEncoder));
        Check(Native.MouseEventNew(0, out _mouseEvent));
    }

    /// <summary>The raw GhosttyTerminal handle. Only use while holding <see cref="Lock"/>.</summary>
    public nint Handle => _terminal;

    public bool HasExited { get; private set; }

    /// <summary>The child's process id, or 0 before <see cref="Start"/>.</summary>
    public int ProcessId => _pty?.ProcessId ?? 0;

    /// <summary>Test hook: observes every reply libghostty writes back to the PTY.</summary>
    internal Action<byte[]>? PtyReplyObserver;

    public void Start(string commandLine, string? workingDirectory, ushort cols, ushort rows)
    {
        // Children inherit our environment; advertise what the terminal supports.
        Environment.SetEnvironmentVariable("COLORTERM", "truecolor");
        Environment.SetEnvironmentVariable("TERM_PROGRAM", "phantom");
        // Launched from another terminal, we'd pass on its identity, and programs would pick
        // features for it (e.g. image protocols for Windows Terminal instead of kitty graphics).
        foreach (var name in (string[])["WT_SESSION", "WT_PROFILE_ID", "TERM_PROGRAM_VERSION", "KITTY_WINDOW_ID", "WEZTERM_PANE"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

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
            _size = new GhosttySizeReportSize { Columns = cols, Rows = rows, CellWidth = cellWidthPx, CellHeight = cellHeightPx };
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

    /// <summary>Scrolls the viewport so <paramref name="row"/> (the scrollbar's offset units) is on top.</summary>
    public void ScrollToRow(ulong row) =>
        ScrollViewport(new GhosttyTerminalScrollViewport { Tag = ScrollViewportTag.Row, Value = (nint)row });

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

    /// <summary>Formats the whole screen (including scrollback) as plain text.</summary>
    public string ReadScreenText()
    {
        lock (Lock)
        {
            return Format(GhosttyFormatterTerminalOptions.Init());
        }
    }

    /// <summary>
    /// Finds <paramref name="pattern"/> in the active screen and its scrollback, oldest first,
    /// matching within rows (a match can't span a line break, even a soft wrap). Rows are
    /// screen rows: 0 is the oldest scrollback row, in the scrollbar's units.
    /// </summary>
    public void Search(Regex pattern, List<SearchMatch> into, int limit = 10_000)
    {
        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            // Find candidate rows fast in the formatted text (one line per row: no unwrapping),
            // then map matches to columns cell by cell on those rows only. Rows are matched
            // without their trailing blanks, so "$" means the end of the text.
            var options = GhosttyFormatterTerminalOptions.Init();
            options.Trim = 1;
            var lines = Format(options).Split('\n');
            ushort cols;
            Native.TerminalGet(_terminal, TerminalData.Cols, &cols);
            for (var row = 0; row < lines.Length && into.Count < limit; row++)
            {
                if (!pattern.IsMatch(lines[row]))
                {
                    continue;
                }

                var rowText = RowText.Read(_terminal, PointTag.Screen, (uint)row, cols);
                foreach (Match m in pattern.Matches(rowText.Text.TrimEnd(' ')))
                {
                    if (m.Length > 0 && into.Count < limit)
                    {
                        var (first, last) = rowText.Columns(m.Index, m.Length);
                        into.Add(new SearchMatch((ulong)row, first, last));
                    }
                }
            }
        }
    }

    private string Format(GhosttyFormatterTerminalOptions options)
    {
        if (_disposed ||
            Native.FormatterTerminalNew(0, out var formatter, _terminal, options) != GhosttyResult.Success)
        {
            return "";
        }

        try
        {
            if (Native.FormatterFormatAlloc(formatter, 0, out var ptr, out var len) != GhosttyResult.Success)
            {
                return "";
            }

            // An empty screen comes back as (NULL, 0).
            if (ptr == null || len == 0)
            {
                return "";
            }

            try
            {
                return Encoding.UTF8.GetString(ptr, (int)len);
            }
            finally
            {
                Native.Free(0, ptr, len);
            }
        }
        finally
        {
            Native.FormatterFree(formatter);
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

    /// <summary>
    /// The link at viewport cell (<paramref name="col"/>, <paramref name="row"/>): an OSC 8
    /// hyperlink, else a URL in that row's text. Links are found within one row.
    /// </summary>
    public LinkMatch? LinkAt(ushort col, uint row)
    {
        lock (Lock)
        {
            if (_disposed)
            {
                return null;
            }

            ushort cols;
            Native.TerminalGet(_terminal, TerminalData.Cols, &cols);
            if (col >= cols)
            {
                return null;
            }

            if (HyperlinkAt(col, row) is { Length: > 0 } uri)
            {
                // The link's extent: neighbors carrying the same URI.
                var start = col;
                while (start > 0 && HyperlinkAt((ushort)(start - 1), row) == uri) start--;
                var end = col;
                while (end + 1 < cols && HyperlinkAt((ushort)(end + 1), row) == uri) end++;
                return new LinkMatch(uri, row, start, end, Explicit: true);
            }

            // Plain text: the row's URL covering this column.
            var rowText = RowText.Read(_terminal, PointTag.Viewport, row, cols);
            if (UrlDetector.Find(rowText.Text, rowText.IndexOf(col)) is not { } found)
            {
                return null;
            }

            var (s, l) = found;
            var (first, last) = rowText.Columns(s, l);
            return new LinkMatch(rowText.Text.Substring(s, l), row, first, last, Explicit: false);
        }
    }

    private string? HyperlinkAt(ushort col, uint row)
    {
        var gridRef = GhosttyGridRef.Init();
        var point = new GhosttyPoint { Tag = PointTag.Viewport, X = col, Y = row };
        if (Native.TerminalGridRef(_terminal, point, &gridRef) != GhosttyResult.Success)
        {
            return null;
        }

        Span<byte> buf = stackalloc byte[512];
        fixed (byte* p = buf)
        {
            var result = Native.GridRefHyperlinkUri(&gridRef, p, (nuint)buf.Length, out var len);
            if (result == GhosttyResult.Success)
            {
                return len == 0 ? null : Encoding.UTF8.GetString(p, (int)len);
            }

            if (result != GhosttyResult.OutOfSpace)
            {
                return null;
            }

            var big = new byte[(int)len];
            fixed (byte* pb = big)
            {
                return Native.GridRefHyperlinkUri(&gridRef, pb, (nuint)big.Length, out len) == GhosttyResult.Success
                    ? Encoding.UTF8.GetString(pb, (int)len)
                    : null;
            }
        }
    }

    /// <summary>
    /// Appends the kitty graphics placements to draw now (visible ones, and all virtual ones) to
    /// <paramref name="into"/>, copying an image's pixels (premultiplied RGBA) only when
    /// <paramref name="needsPixels"/>(id, generation) says the caller hasn't cached them.
    /// Take this snapshot under the same lock as the render state update so they agree.
    /// </summary>
    public void CollectImages(List<ImagePlacement> into, Func<uint, ulong, bool> needsPixels)
    {
        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            nint graphics;
            ulong storageGeneration = 0;
            if (Native.TerminalGet(_terminal, TerminalData.KittyGraphics, &graphics) != GhosttyResult.Success ||
                Native.KittyGraphicsGet(graphics, KittyGraphicsData.Generation, &storageGeneration) != GhosttyResult.Success ||
                storageGeneration == 0)
            {
                return; // nothing was ever stored on this screen
            }

            var iterator = _placements;
            if (Native.KittyGraphicsGet(graphics, KittyGraphicsData.PlacementIterator, &iterator) != GhosttyResult.Success)
            {
                return;
            }

            while (Native.KittyPlacementNext(_placements) != 0)
            {
                uint imageId;
                byte isVirtual;
                int z;
                uint offsetX, offsetY;
                Native.KittyPlacementGet(_placements, KittyGraphicsPlacementData.ImageId, &imageId);
                Native.KittyPlacementGet(_placements, KittyGraphicsPlacementData.IsVirtual, &isVirtual);
                Native.KittyPlacementGet(_placements, KittyGraphicsPlacementData.Z, &z);
                Native.KittyPlacementGet(_placements, KittyGraphicsPlacementData.XOffset, &offsetX);
                Native.KittyPlacementGet(_placements, KittyGraphicsPlacementData.YOffset, &offsetY);
                var image = Native.KittyGraphicsImage(graphics, imageId);
                var info = GhosttyKittyGraphicsPlacementRenderInfo.Init();
                uint placementId = 0, columns = 0, rows = 0;
                if (isVirtual != 0)
                {
                    // Shown by placeholder cells wherever they are; the renderer needs its grid.
                    Native.KittyPlacementGet(_placements, KittyGraphicsPlacementData.PlacementId, &placementId);
                    Native.KittyPlacementGet(_placements, KittyGraphicsPlacementData.Columns, &columns);
                    Native.KittyPlacementGet(_placements, KittyGraphicsPlacementData.Rows, &rows);
                }

                if (image == 0 ||
                    (isVirtual == 0 &&
                     (Native.KittyPlacementRenderInfo(_placements, image, _terminal, &info) != GhosttyResult.Success ||
                      info.ViewportVisible == 0)))
                {
                    continue;
                }

                uint width, height;
                ulong generation;
                Native.KittyImageGet(image, KittyGraphicsImageData.Width, &width);
                Native.KittyImageGet(image, KittyGraphicsImageData.Height, &height);
                Native.KittyImageGet(image, KittyGraphicsImageData.Generation, &generation);

                byte[]? pixels = null;
                if (needsPixels(imageId, generation))
                {
                    KittyImageFormat format;
                    byte* data;
                    nuint len;
                    if (Native.KittyImageGet(image, KittyGraphicsImageData.Format, &format) != GhosttyResult.Success ||
                        Native.KittyImageGet(image, KittyGraphicsImageData.DataPtr, &data) != GhosttyResult.Success ||
                        Native.KittyImageGet(image, KittyGraphicsImageData.DataLen, &len) != GhosttyResult.Success)
                    {
                        continue; // payload still pending
                    }

                    var count = (int)(width * height);
                    if ((long)count * KittyImages.BytesPerPixel(format) > (long)len)
                    {
                        continue;
                    }

                    pixels = KittyImages.ToPremultipliedRgba(format, new ReadOnlySpan<byte>(data, (int)len), count);
                }

                into.Add(new ImagePlacement(imageId, generation, z, info.ViewportCol, info.ViewportRow, offsetX, offsetY,
                    info.PixelWidth, info.PixelHeight, info.SourceX, info.SourceY, info.SourceWidth, info.SourceHeight,
                    (int)width, (int)height, pixels, isVirtual != 0, placementId, columns, rows));
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
        var session = FromUserdata(userdata);
        var bytes = new ReadOnlySpan<byte>(data, (int)len);
        session.PtyReplyObserver?.Invoke(bytes.ToArray());
        session._pty?.Write(bytes);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnTitleChanged(nint terminal, nint userdata) => FromUserdata(userdata)._titleDirty = true;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte OnSize(nint terminal, nint userdata, GhosttySizeReportSize* size)
    {
        // Called under Lock (from vt_write or resize), so _size is consistent.
        var s = FromUserdata(userdata)._size;
        if (s.CellWidth == 0)
        {
            return 0;
        }

        *size = s;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnBell(nint terminal, nint userdata) => FromUserdata(userdata)._bell = true;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardWrite(nint terminal, nint userdata, GhosttyClipboardWrite* write)
    {
        var result = ClipboardWriteResult.Denied;
        try
        {
            result = FromUserdata(userdata).WriteClipboard(write);
        }
        catch (Exception ex)
        {
            Log.Write($"clipboard write failed: {ex.Message}");
        }

        var reply = new GhosttyClipboardWriteReply { Size = (nuint)sizeof(GhosttyClipboardWriteReply), Result = result };
        ((delegate* unmanaged[Cdecl]<GhosttyClipboardWrite*, GhosttyClipboardWriteReply*, void>)write->Reply)(write, &reply);
    }

    private ClipboardWriteResult WriteClipboard(GhosttyClipboardWrite* write)
    {
        if (ClipboardWriter is not { } writer)
        {
            return ClipboardWriteResult.Denied;
        }

        // Windows has one clipboard; X11's selection/primary have no counterpart.
        if (write->Location != ClipboardLocation.Standard)
        {
            return ClipboardWriteResult.Unsupported;
        }

        var contents = (GhosttyClipboardContent*)write->Contents;
        for (nuint i = 0; i < write->ContentsLen; i++)
        {
            if (Utf8(contents[i].Mime).StartsWith("text/plain", StringComparison.Ordinal))
            {
                writer(Utf8(contents[i].Data));
                return ClipboardWriteResult.Success;
            }
        }

        // No text representation, or a request to clear: leave the clipboard alone.
        return write->ContentsLen == 0 ? ClipboardWriteResult.Success : ClipboardWriteResult.Unsupported;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardRead(nint terminal, nint userdata, GhosttyClipboardRead* read)
    {
        string? text = null;
        var result = ClipboardReadResult.Denied;
        try
        {
            var session = FromUserdata(userdata);
            if (session.ClipboardReader is { } reader)
            {
                text = read->Location == ClipboardLocation.Standard ? reader() : null;
                result = read->Location != ClipboardLocation.Standard ? ClipboardReadResult.Unsupported
                    : text == null ? ClipboardReadResult.Busy
                    : ClipboardReadResult.Success;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"clipboard read failed: {ex.Message}");
            result = ClipboardReadResult.IoError;
        }

        var data = Encoding.UTF8.GetBytes(text ?? "");
        var mime = "text/plain"u8;
        fixed (byte* pData = data)
        fixed (byte* pMime = mime)
        {
            var content = new GhosttyClipboardContent
            {
                Mime = new GhosttyString { Ptr = (nint)pMime, Len = (nuint)mime.Length },
                Data = new GhosttyString { Ptr = (nint)pData, Len = (nuint)data.Length },
            };
            var reply = new GhosttyClipboardReadReply
            {
                Size = (nuint)sizeof(GhosttyClipboardReadReply),
                Result = result,
                Contents = (nint)(&content),
                ContentsLen = result == ClipboardReadResult.Success ? 1u : 0u,
            };
            ((delegate* unmanaged[Cdecl]<GhosttyClipboardRead*, GhosttyClipboardReadReply*, void>)read->Reply)(read, &reply);
        }
    }

    private static string Utf8(GhosttyString s) =>
        s.Len == 0 ? "" : Encoding.UTF8.GetString((byte*)s.Ptr, (int)s.Len);

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
            Native.KittyPlacementIteratorFree(_placements);
            Native.TerminalFree(_terminal);
            _self.Free();
        }
    }
}
