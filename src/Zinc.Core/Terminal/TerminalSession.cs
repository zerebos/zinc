using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Zinc.Ghostty;
using Zinc.Pty;

namespace Zinc.Terminal;

/// <summary>
/// Binds a libghostty-vt terminal to a ConPTY child process.
///
/// Threading: PTY output is parsed on the ConPTY reader thread while the UI
/// thread renders and encodes input. libghostty-vt handles are not thread
/// safe, so every call touching <see cref="Handle"/> must hold <see cref="Lock"/>.
/// </summary>
public sealed unsafe partial class TerminalSession : IDisposable
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
    private bool _pwdDirty;
    private bool _bell;
    private bool _disposed;

    /// <summary>New output was parsed; the view should redraw. Raised on the reader thread.</summary>
    public event Action? OutputProcessed;

    /// <summary>The OSC 0/2 title changed. Raised on the reader thread.</summary>
    public event Action? TitleChanged;

    /// <summary>
    /// The shell reported a different working directory (<see cref="ReportedDirectory"/>), or reports started or
    /// stopped coming from another machine (<see cref="RemoteHost"/>); raised after the write.
    /// </summary>
    public event Action? DirectoryChanged;

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

    /// <summary>
    /// With <c>clipboard-read = ask</c>: asked before each clipboard read a program sends, and it
    /// may block for the answer (the program's output waits; the terminal isn't locked). It's called
    /// on the thread feeding output. Null: <see cref="ClipboardReader"/> alone decides.
    /// </summary>
    public Func<bool>? ClipboardConsent { get; set; }

    /// <summary>The read being fed now was allowed (set around its bytes by <see cref="Feed"/>).</summary>
    private volatile bool _readConsented;

    /// <param name="generation">Whether colors 16-255 are made from the scheme's 16 (<c>palette-generate</c>).</param>
    public TerminalSession(ushort cols, ushort rows, TerminalTheme theme, PaletteGeneration generation = default)
    {
        _generation = generation;
        Check(Native.TerminalNew(0, out _terminal, cols, rows));
        _self = GCHandle.Alloc(this);

        Native.TerminalSet(_terminal, TerminalOption.Userdata, (void*)GCHandle.ToIntPtr(_self));
        Native.TerminalSet(_terminal, TerminalOption.WritePty,
            (delegate* unmanaged[Cdecl]<nint, nint, byte*, nuint, void>)&OnWritePty);
        Native.TerminalSet(_terminal, TerminalOption.TitleChanged,
            (delegate* unmanaged[Cdecl]<nint, nint, void>)&OnTitleChanged);
        // OSC 7 / OSC 9;9 / OSC 1337 CurrentDir: where the shell says it is (see ReportedDirectory).
        Native.TerminalSet(_terminal, TerminalOption.PwdChanged,
            (delegate* unmanaged[Cdecl]<nint, nint, void>)&OnPwdChanged);
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
        InstallNotificationCallbacks(); // OSC 9 / 777 notifications, OSC 9;4 progress

        // libghostty's own palette, before a theme changes it: reading its "default" palette
        // later returns whatever was last set, so a theme change couldn't restore entries.
        fixed (GhosttyColorRgb* p = _builtinPalette)
        {
            Native.TerminalGet(_terminal, TerminalData.ColorPaletteDefault, p);
        }

        ApplyTheme(theme);

        if (ConPty.UsesBundledConpty)
        {
            // The bundled ConPTY measures widths by grapheme cluster; do the same
            // so emoji sequences (ZWJ, flags, skin tones) occupy matching cells.
            var grapheme = new GhosttyTerminalModeConfig { Mode = GhosttyMode.GraphemeCluster, Value = 1 };
            Native.TerminalSet(_terminal, TerminalOption.ModeDefault, &grapheme);
        }

        nuint scrollback = Config.DefaultScrollbackLines;
        Native.TerminalSet(_terminal, TerminalOption.ScrollbackMaxLines, &scrollback);
        // libghostty prunes at whichever limit comes first, and its default byte limit kept
        // only ~700 rows of history at 120 columns. Lines are the limit users think in.
        Native.TerminalSet(_terminal, TerminalOption.ScrollbackMaxBytes, null);

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

        // ConPTY keeps its own screen without scrollback: a resize must not pull rows back out of
        // our scrollback, or the two disagree about what's on screen (libghostty names ConPTY as
        // the reason for this option).
        byte pullScrollback = 0;
        Native.TerminalSet(_terminal, TerminalOption.ResizePullScrollback, &pullScrollback);

        // Who we are, for programs that ask (XTVERSION, CSI > q): "zinc <version>", lowercase
        // like TERM_PROGRAM (and Ghostty's own "ghostty 1.x"). Default: "libghostty".
        Native.TerminalSet(_terminal, TerminalOption.Xtversion, (delegate* unmanaged[Cdecl]<nint, nint, GhosttyString>)&OnXtversion);

        // Light or dark, for programs that ask (CSI ? 996 n): see SetColorScheme.
        Native.TerminalSet(_terminal, TerminalOption.ColorScheme, (delegate* unmanaged[Cdecl]<nint, nint, ColorScheme*, byte>)&OnColorScheme);

        // Synchronized output (mode 2026): see RenderHold.
        Native.TerminalSet(_terminal, TerminalOption.RenderHold, (delegate* unmanaged[Cdecl]<nint, nint, byte, void>)&OnRenderHold);

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

    /// <param name="environment">Variables to set (or, null, remove) for this child only.</param>
    public void Start(string commandLine, string? workingDirectory, ushort cols, ushort rows,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        // Children inherit our environment; advertise what the terminal supports.
        Environment.SetEnvironmentVariable("COLORTERM", "truecolor");
        Environment.SetEnvironmentVariable("TERM_PROGRAM", "zinc");
        // Launched from another terminal, we'd pass on its identity, and programs would pick
        // features for it (e.g. image protocols for Windows Terminal instead of kitty graphics).
        foreach (var name in (string[])["WT_SESSION", "WT_PROFILE_ID", "TERM_PROGRAM_VERSION", "KITTY_WINDOW_ID", "WEZTERM_PANE"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Attach(new ConPty(commandLine, workingDirectory, (short)cols, (short)rows, environment));
        _ptySentSize = ((short)cols, (short)rows); // (a handed-over console's size is unknown: Attach leaves it unset)
    }

    /// <summary>
    /// Takes a console Zinc didn't start (one handed over as the default terminal) and starts
    /// reading it; resize the session after, so its program learns the pane's size.
    /// </summary>
    public void Attach(ConPty pty)
    {
        _pty = pty;
        _pty.OutputReceived += OnPtyOutput;
        _pty.Exited += code =>
        {
            HasExited = true;
            ProcessExited?.Invoke(code);
        };
        _pty.Begin();
    }

    // Set ZINC_TRACE=<file> to record raw PTY output for debugging.
    private readonly FileStream? _trace = Environment.GetEnvironmentVariable("ZINC_TRACE") is { Length: > 0 } path
        ? new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)
        : null;

    private void OnPtyOutput(ReadOnlySpan<byte> data)
    {
        if (_trace != null)
        {
            _trace.Write(data);
            _trace.Flush();
        }

        Feed(data);
    }

    /// <summary>Parses output, then acts on what it changed (title, directory, bell).</summary>
    /// <summary>
    /// Programs' screen flashes (reverse video on and straight off) ring the bell instead of
    /// inverting the screen (<see cref="ScreenFlashFilter"/>). Config: <c>screen-flash-bell</c>.
    /// </summary>
    public bool CatchScreenFlash { get; set; }

    private readonly ScreenFlashFilter _flash = new();

    /// <summary>Guards <see cref="_flash"/> and keeps output in order between reads and its expiry timer.</summary>
    private readonly object _flashLock = new();

    private Timer? _flashTimer;

    /// <summary>
    /// Output from the program (or ours): with <see cref="ClipboardConsent"/>, each clipboard read
    /// waits for its answer, and is then fed on its own, carrying it.
    /// </summary>
    private void Feed(ReadOnlySpan<byte> data)
    {
        while (ClipboardConsent is { } consent && ClipboardReadRequest.TryFind(data, out var start, out var end))
        {
            FeedFiltered(data[..start]);
            var allowed = false;
            try
            {
                allowed = consent();
            }
            catch (Exception ex)
            {
                Log.Write($"clipboard read: asking failed: {ex.Message}");
            }

            Log.Write($"clipboard read: {(allowed ? "allowed" : "denied")}");
            _readConsented = allowed;
            try
            {
                FeedFiltered(data[start..end]);
            }
            finally
            {
                _readConsented = false;
            }

            data = data[end..];
        }

        FeedFiltered(data);
    }

    private void FeedFiltered(ReadOnlySpan<byte> data)
    {
        lock (_flashLock)
        {
            // Nothing held and no flash sequence in sight (nearly every read): straight through, no copies.
            if (!_flash.Waiting && (!CatchScreenFlash || !ScreenFlashFilter.Concerns(data)))
            {
                Process(data, flashBell: false);
                return;
            }

            var now = Environment.TickCount64;
            var flashes = 0;
            // (Turned off while something is held: let it go first, then as usual.)
            var filtered = CatchScreenFlash ? _flash.Filter(data, now, out flashes) : [.. _flash.TakeExpired(long.MaxValue), .. data];
            Process(filtered, flashBell: flashes > 0);
            if (_flash.Waiting)
            {
                _flashTimer ??= new Timer(_ => ExpireFlash());
                _flashTimer.Change(ScreenFlashFilter.FlashMs + 10, Timeout.Infinite);
            }
        }
    }

    /// <summary>The flash filter's wait is over: apply what it held (reverse video after all, a partial sequence).</summary>
    private void ExpireFlash()
    {
        try
        {
            lock (_flashLock)
            {
                var due = _flash.TakeExpired(Environment.TickCount64);
                if (due.Length > 0 && !_disposed)
                {
                    Process(due, flashBell: false);
                }

                if (_flash.Waiting)
                {
                    _flashTimer?.Change(ScreenFlashFilter.FlashMs, Timeout.Infinite);
                }
            }
        }
        catch (Exception ex)
        {
            // A thread-pool callback: an escaped exception would end the process.
            Log.Write($"screen flash: {ex.Message}");
        }
    }

    private void Process(ReadOnlySpan<byte> data, bool flashBell)
    {
        var titleChanged = false;
        var directoryChanged = false;
        bool bell, commandsChanged;
        bool? hold;
        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            WriteWithMarks(data);
            commandsChanged = _commandsChanged;
            _commandsChanged = false;

            // Callbacks fire mid-parse, so read derived state only once the
            // write has finished.
            // Only noted here: the title is read when the view gets to it (TakeTitle), once per notice.
            if (_titleDirty)
            {
                _titleDirty = false;
                titleChanged = Interlocked.Exchange(ref _titleNoticePending, 1) == 0;
            }

            if (_pwdDirty)
            {
                _pwdDirty = false;
                directoryChanged = TakeReportedDirectory(ReadString(TerminalData.Pwd));
            }

            directoryChanged |= _leftRemote;
            _leftRemote = false;

            bell = _bell || flashBell;
            _bell = false;
            hold = _holdChanged;
            _holdChanged = null;
        }

        if (hold is { } held)
        {
            RenderHoldChanged?.Invoke(held);
        }

        if (titleChanged)
        {
            TitleChanged?.Invoke();
        }

        if (directoryChanged)
        {
            DirectoryChanged?.Invoke();
        }

        AnnounceCommands(commandsChanged);
        AnnounceNotifications();

        if (bell)
        {
            Bell?.Invoke();
        }

        OutputProcessed?.Invoke();
    }

    /// <summary>
    /// Changes the terminal's colors while it runs (a config reload): the default foreground,
    /// background, cursor and palette. Colors a program set itself (OSC 4/10/11) stay.
    /// </summary>
    private bool _dark = true;

    /// <summary>How many lines are kept above the screen (fewer prunes the oldest at the next write).</summary>
    public void SetScrollback(int lines)
    {
        lock (Lock)
        {
            if (!_disposed)
            {
                nuint value = (nuint)Math.Max(0, lines);
                Native.TerminalSet(_terminal, TerminalOption.ScrollbackMaxLines, &value);
            }
        }
    }

    /// <summary>
    /// The cursor programs get until they choose one (DECSCUSR) and after they reset it; applies
    /// at once while the cursor is still the default.
    /// </summary>
    /// <param name="atPrompt">The shape while the shell waits at its prompt (shell integration's marks say when); null: the same.</param>
    public void SetCursorDefaults(TerminalCursorStyle style, bool blink, TerminalCursorStyle? atPrompt = null)
    {
        lock (Lock)
        {
            _cursorDefaults = (style, blink, atPrompt);
            ApplyCursorDefaults();
        }
    }

    private (TerminalCursorStyle Style, bool Blink, TerminalCursorStyle? AtPrompt) _cursorDefaults = (TerminalCursorStyle.Block, false, null);

    /// <summary>
    /// The default cursor for now: the prompt's while the shell waits at one, else the usual. A
    /// program's own choice (DECSCUSR: vim, a shell's vi mode) wins over either. Hold the lock.
    /// </summary>
    private void ApplyCursorDefaults()
    {
        if (_disposed)
        {
            return;
        }

        var (usual, blinking, atPrompt) = _cursorDefaults;
        var style = _promptState is PromptState.Prompt or PromptState.Input && atPrompt is { } prompt ? prompt : usual;
        var b = (byte)(blinking ? 1 : 0);
        Native.TerminalSet(_terminal, TerminalOption.DefaultCursorStyle, &style);
        Native.TerminalSet(_terminal, TerminalOption.DefaultCursorBlink, &b);
    }

    /// <summary>
    /// Whether this pane's colors are dark, as programs are told: they ask (CSI ? 996 n), or
    /// subscribe to changes (mode 2031), which this then reports (CSI ? 997 ; 1|2 n).
    /// </summary>
    public void SetColorScheme(bool dark)
    {
        byte[]? report = null;
        lock (Lock)
        {
            if (_disposed || dark == _dark)
            {
                return;
            }

            _dark = dark;
            if (GetMode(GhosttyMode.ColorSchemeReport))
            {
                var buffer = stackalloc byte[32];
                if (Native.ColorSchemeReportEncode(dark ? ColorScheme.Dark : ColorScheme.Light, buffer, 32, out var written) == GhosttyResult.Success)
                {
                    report = new ReadOnlySpan<byte>(buffer, (int)written).ToArray();
                }
            }
        }

        if (report != null)
        {
            PtyReplyObserver?.Invoke(report);
            _pty?.Write(report);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte OnColorScheme(nint terminal, nint userdata, ColorScheme* scheme)
    {
        *scheme = FromUserdata(userdata)._dark ? ColorScheme.Dark : ColorScheme.Light;
        return 1;
    }

    public void SetTheme(TerminalTheme theme, PaletteGeneration generation = default)
    {
        lock (Lock)
        {
            if (!_disposed)
            {
                _generation = generation;
                ApplyTheme(theme);
            }
        }

        OutputProcessed?.Invoke(); // redraw
    }

    private readonly GhosttyColorRgb[] _builtinPalette = new GhosttyColorRgb[256];
    private PaletteGeneration _generation;

    private static readonly Lazy<GhosttyColorRgb[]> BuiltinBase = new(() =>
    {
        // Read from a throwaway terminal: libghostty's own palette, before any theme.
        var palette = new GhosttyColorRgb[256];
        if (Native.TerminalNew(0, out var terminal, 1, 1) == GhosttyResult.Success)
        {
            try
            {
                fixed (GhosttyColorRgb* p = palette)
                {
                    Native.TerminalGet(terminal, TerminalData.ColorPaletteDefault, p);
                }
            }
            finally
            {
                Native.TerminalFree(terminal);
            }
        }

        return palette[..16];
    });

    /// <summary>libghostty's built-in 16 base colors (what a scheme that doesn't set them shows).</summary>
    public static IReadOnlyList<GhosttyColorRgb> BuiltinBaseColors => BuiltinBase.Value;

    private void ApplyTheme(TerminalTheme theme)
    {
        var fg = theme.Foreground;
        var bg = theme.Background;
        var cursor = theme.Cursor;
        Native.TerminalSet(_terminal, TerminalOption.ColorForeground, &fg);
        Native.TerminalSet(_terminal, TerminalOption.ColorBackground, &bg);
        Native.TerminalSet(_terminal, TerminalOption.ColorCursor, &cursor);

        // libghostty takes the whole 256-color table: its built-in one, then the theme's entries.
        // Always set, so entries a new theme doesn't define go back to the built-in colors.
        var palette = (GhosttyColorRgb[])_builtinPalette.Clone();
        fixed (GhosttyColorRgb* p = palette)
        {
            foreach (var (index, color) in theme.Palette)
            {
                p[index] = color;
            }

            if (_generation.Generate)
            {
                // 16-255 from the scheme's 16 and its colors (Ghostty's palette-generate), except entries the scheme
                // or the config set themselves.
                var skip = stackalloc ulong[4];
                foreach (var index in theme.Palette.Keys.Where(i => i is >= 16 and < 256))
                {
                    skip[index / 64] |= 1UL << (index % 64);
                }

                Native.ColorPaletteGenerate(p, skip, &bg, &fg, _generation.Harmonious ? (byte)1 : (byte)0, p);
            }

            Native.TerminalSet(_terminal, TerminalOption.ColorPalette, p);
        }
    }

    /// <summary>
    /// The working directory the shell last reported on this PC (OSC 7's file:// URI, or OSC 9;9 / 1337's
    /// bare path), as sent; null until it reports one or after it clears it. Decode with
    /// <see cref="Zinc.Terminal.ShellDirectory.Parse"/>. A report from another machine (ssh) leaves it as it
    /// was, so new tabs still start where you ran ssh from; that machine is <see cref="RemoteHost"/>.
    /// </summary>
    public string? ReportedDirectory { get; private set; }

    /// <summary>The machine the last folder report came from when it wasn't this PC (a shell over ssh); else null.</summary>
    public string? RemoteHost { get; private set; }

    /// <summary>That report, as sent: <see cref="RecheckReportedDirectory"/> takes it once its host is known to be local.</summary>
    private string? _remoteReport;

    /// <summary>
    /// Notes a folder report (under the lock); true when <see cref="ReportedDirectory"/> or <see cref="RemoteHost"/>
    /// changed (going into ssh changes no local folder, but what the tab shows).
    /// </summary>
    private bool TakeReportedDirectory(string? reported)
    {
        if (ShellDirectory.RemoteHost(reported) is { } host)
        {
            var arrived = host != RemoteHost;
            if (arrived)
            {
                Log.Write($"folder: reports from {host} ignored (not this PC)");
            }

            RemoteHost = host;
            _remoteReport = reported;
            return arrived;
        }

        var left = RemoteHost != null;
        RemoteHost = null;
        _remoteReport = null;
        var changed = reported != ReportedDirectory;
        ReportedDirectory = reported;
        return changed || left;
    }

    /// <summary>
    /// The prompt-mark options of the shell last prompting while folder reports were local (see
    /// <see cref="NoteLocalPrompt"/>); null until one has.
    /// </summary>
    private string? _localPromptSignature;

    /// <summary>Set when a prompt ended the ssh session (<see cref="NoteLocalPrompt"/>): raises <see cref="DirectoryChanged"/> after the write.</summary>
    private bool _leftRemote;

    /// <summary>
    /// A prompt (under the lock; <paramref name="entered"/>: of a shell just started from a command). Back at the
    /// prompt of the shell that prompted while folders were local, ssh has ended: a shell that reports its folder
    /// only when it changes (zsh's) wouldn't say so. Two shells with the same mark options (no aid) look alike, as
    /// they do to command tracking: the remote one's prompt then ends it early.
    /// </summary>
    private void NoteLocalPrompt(string signature, bool entered)
    {
        if (RemoteHost == null)
        {
            if (!entered)
            {
                _localPromptSignature = signature;
            }
        }
        else if (signature == _localPromptSignature)
        {
            Log.Write($"folder: back from {RemoteHost} (a local prompt)");
            RemoteHost = null;
            _remoteReport = null;
            _leftRemote = true;
        }
    }

    /// <summary>
    /// Takes the last folder report ignored as remote if its host now counts as this PC (a WSL distro's own host
    /// name, learned from its wsl.conf after the shell's first prompt). Raises <see cref="DirectoryChanged"/> then.
    /// </summary>
    public void RecheckReportedDirectory()
    {
        bool changed;
        lock (Lock)
        {
            if (_remoteReport is not { } reported || ShellDirectory.RemoteHost(reported) != null)
            {
                return;
            }

            Log.Write($"folder: reports from {RemoteHost} are local after all");
            changed = TakeReportedDirectory(reported);
        }

        if (changed)
        {
            DirectoryChanged?.Invoke();
        }
    }

    /// <summary>The title's bytes as last taken (<see cref="TakeTitle"/>).</summary>
    private byte[] _titleBytes = [];

    /// <summary>1 while a <see cref="TitleChanged"/> notice waits for <see cref="TakeTitle"/>.</summary>
    private int _titleNoticePending;

    /// <summary>
    /// After <see cref="TitleChanged"/>: the title, or null if it's back to what it was last time. Programs retitle
    /// themselves constantly (cmd running a loop under ConPTY: ~14,000 different titles a second, the command line
    /// with its counter), and reading each one on the reader thread, a string and a UI update apiece, churned
    /// ~20 MB a second of garbage (2026-10-02). Now there's one notice until the view takes the title, so it's read
    /// at most once per UI tick. Any thread.
    /// </summary>
    public string? TakeTitle()
    {
        lock (Lock)
        {
            Volatile.Write(ref _titleNoticePending, 0); // a change after this read sends a new notice
            return _disposed ? null : ReadTitleIfChanged();
        }
    }

    private string? ReadTitleIfChanged()
    {
        GhosttyString s;
        var bytes = Native.TerminalGet(_terminal, TerminalData.Title, &s) == GhosttyResult.Success && s.Len > 0
            ? new ReadOnlySpan<byte>((byte*)s.Ptr, (int)s.Len)
            : [];
        if (bytes.SequenceEqual(_titleBytes))
        {
            return null;
        }

        _titleBytes = bytes.ToArray();
        return Encoding.UTF8.GetString(bytes);
    }

    private string ReadString(TerminalData what)
    {
        GhosttyString s;
        if (Native.TerminalGet(_terminal, what, &s) != GhosttyResult.Success || s.Len == 0)
        {
            return "";
        }

        return Encoding.UTF8.GetString((byte*)s.Ptr, (int)s.Len);
    }

    /// <summary>Writes VT data straight to the screen, as if the child had printed it.</summary>
    public void WriteToScreen(string text) => Feed(Encoding.UTF8.GetBytes(text));

    /// <summary>Shows <paramref name="output"/> as if the program had written it (the drawing benchmark feeds its workloads this way).</summary>
    public void WriteToScreen(ReadOnlySpan<byte> output) => Feed(output);

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

        NoteEnter(data);
        _pty?.Write(data);
    }

    public void SendInput(string text) => SendInput(Encoding.UTF8.GetBytes(text));

    /// <param name="atOnce">Tell the shell now instead of once the size settles: a resize known to be the only one
    /// (a pane held at its size through a layout animation).</param>
    public void Resize(ushort cols, ushort rows, uint cellWidthPx, uint cellHeightPx, bool atOnce = false)
    {
        lock (Lock)
        {
            _size = new GhosttySizeReportSize { Columns = cols, Rows = rows, CellWidth = cellWidthPx, CellHeight = cellHeightPx };
            Native.TerminalResize(_terminal, cols, rows, cellWidthPx, cellHeightPx);
            _commandSnapshot = null; // reflow moves rows
        }

        if (_pty == null)
        {
            return;
        }

        // The shell hears of the size once a resize settles: dragging a window's edge goes through
        // dozens of sizes, and a prompt redrawn for each while the lines reflow piles up stale
        // copies (powerlevel10k's frame, in Windows Terminal as well). The view reflows live.
        lock (_ptyResizeGate)
        {
            _ptyPendingSize = ((short)cols, (short)rows);
            _ptyResizeTimer ??= new Timer(_ => SendPtySize());
            _ptyResizeTimer.Change(atOnce ? TimeSpan.Zero : PtyResizeSettle, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>How long a resize must stay put before the shell is told (tests: zero).</summary>
    internal static TimeSpan PtyResizeSettle { get; set; } = TimeSpan.FromMilliseconds(100);

    private readonly object _ptyResizeGate = new();
    private Timer? _ptyResizeTimer;
    private (short Cols, short Rows)? _ptyPendingSize;
    private (short Cols, short Rows)? _ptySentSize;

    private void SendPtySize()
    {
        (short Cols, short Rows)? size;
        lock (_ptyResizeGate)
        {
            // The same size again (a layout rebuilt around an unchanged terminal) isn't news to the shell.
            size = _ptyPendingSize == _ptySentSize ? null : _ptyPendingSize;
            _ptySentSize = size ?? _ptySentSize;
            _ptyPendingSize = null;
        }

        try
        {
            if (size is var (cols, rows))
            {
                Log.Write($"pty: resize {cols}x{rows}");
                _pty?.Resize(cols, rows);
            }
        }
        catch (Exception ex)
        {
            Log.Crash("pty resize", ex); // a timer thread: nothing above to catch it
        }
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

    /// <summary>Rows written so far: those scrolled into the scrollback, and the screen's down to the cursor's.</summary>
    public int RowsUsed()
    {
        GhosttyTerminalScrollbar sb;
        ushort y = 0;
        lock (Lock)
        {
            Native.TerminalGet(_terminal, TerminalData.Scrollbar, &sb);
            Native.TerminalGet(_terminal, TerminalData.CursorY, &y);
        }

        return (int)(sb.Total - sb.Len) + y + 1;
    }

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
    /// <summary>Test hook: the cursor's row in the active screen.</summary>
    internal int CursorRow
    {
        get
        {
            lock (Lock)
            {
                ushort y = 0;
                Native.TerminalGet(_terminal, TerminalData.CursorY, &y);
                return y;
            }
        }
    }

    public string ReadScreenText()
    {
        lock (Lock)
        {
            return Format(GhosttyFormatterTerminalOptions.Init());
        }
    }

    /// <summary>
    /// What <c>zn terminal read</c> prints: the live screen (not the scrollback), or with <paramref name="lines"/> the
    /// last that many lines, scrollback included. Blank rows at the bottom don't count. <paramref name="styled"/>: with
    /// colors and styles as VT sequences.
    /// </summary>
    public string ReadText(int? lines = null, bool styled = false)
    {
        lock (Lock)
        {
            var options = GhosttyFormatterTerminalOptions.Init();
            options.Emit = styled ? 1 : 0; // GHOSTTY_FORMATTER_FORMAT_VT : _PLAIN
            options.Trim = 1;
            // One line per row, scrollback first; the formatter leaves out the blank rows at the bottom.
            var all = Format(options).Replace("\r\n", "\n").Split('\n');
            GhosttyTerminalScrollbar sb;
            Native.TerminalGet(_terminal, TerminalData.Scrollbar, &sb);
            var first = lines is { } n ? Math.Max(0, all.Length - n) : (int)Math.Min((ulong)all.Length, sb.Total - sb.Len);
            return string.Join('\n', all[first..]);
        }
    }

    /// <summary>
    /// Finds <paramref name="pattern"/> in the active screen and its scrollback, oldest first.
    /// Matches may span soft wraps but not real line breaks. Rows are screen rows: 0 is the
    /// oldest scrollback row, in the scrollbar's units.
    /// </summary>
    public void Search(Regex pattern, List<SearchMatch> into, int limit = 10_000)
    {
        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            // Find candidate lines fast in the formatted text (soft wraps joined: one line per
            // logical line), then map matches to cells on those lines only. Lines are matched
            // without their trailing blanks, so "$" means the end of the text.
            var options = GhosttyFormatterTerminalOptions.Init();
            options.Unwrap = 1;
            options.Trim = 1;
            var lines = Format(options).Split('\n');
            ushort cols;
            Native.TerminalGet(_terminal, TerminalData.Cols, &cols);
            GhosttyTerminalScrollbar sb;
            Native.TerminalGet(_terminal, TerminalData.Scrollbar, &sb);

            // Logical line k covers screen rows [start, start + count): rows continue while wrapped.
            var row = 0ul;
            for (var k = 0; k < lines.Length && row < sb.Total && into.Count < limit; k++)
            {
                var start = row;
                while (row + 1 < sb.Total && IsWrapped(row))
                {
                    row++;
                }

                row++;
                if (pattern.IsMatch(lines[k]))
                {
                    MatchLine(pattern, start, (int)(row - start), cols, into, limit);
                }
            }
        }
    }

    private bool IsWrapped(ulong row)
    {
        var gridRef = GhosttyGridRef.Init();
        var point = new GhosttyPoint { Tag = PointTag.Screen, X = 0, Y = (uint)row };
        byte wrap = 0;
        return Native.TerminalGridRef(_terminal, point, &gridRef) == GhosttyResult.Success &&
               Native.GridRefRow(&gridRef, out var raw) == GhosttyResult.Success &&
               Native.ScreenRowGet(raw, RowData.Wrap, &wrap) == GhosttyResult.Success && wrap != 0;
    }

    /// <summary>Matches one logical line (screen rows first..first+count-1) cell by cell.</summary>
    private void MatchLine(Regex pattern, ulong first, int count, ushort cols, List<SearchMatch> into, int limit)
    {
        var line = LogicalLine.Read(_terminal, first, count, cols);
        foreach (Match m in pattern.Matches(line.Text.TrimEnd(' ')))
        {
            if (m.Length == 0 || into.Count >= limit)
            {
                continue;
            }

            var (a, startCol, b, endCol) = line.Cells(m.Index, m.Length);
            into.Add(new SearchMatch(first + (ulong)a, startCol, first + (ulong)b, endCol));
        }
    }

    /// <summary>
    /// Screen rows joined across soft wraps into one string, with the way back from string
    /// indexes to (row, column). Hold the session lock.
    /// </summary>
    private sealed class LogicalLine
    {
        private readonly RowText[] _rows;
        private readonly int[] _offsets; // where each row's text starts; one past the end last

        private LogicalLine(RowText[] rows, int[] offsets, string text)
        {
            _rows = rows;
            _offsets = offsets;
            Text = text;
        }

        public string Text { get; }

        public static LogicalLine Read(nint terminal, ulong first, int count, ushort cols)
        {
            var rows = new RowText[count];
            var offsets = new int[count + 1];
            var text = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                rows[i] = RowText.Read(terminal, PointTag.Screen, (uint)(first + (ulong)i), cols);
                offsets[i] = text.Length;
                text.Append(rows[i].Text);
            }

            offsets[count] = text.Length;
            return new LogicalLine(rows, offsets, text.ToString());
        }

        /// <summary>The string index of column <paramref name="col"/> on the line's row <paramref name="row"/> (0 = first).</summary>
        public int IndexOf(int row, int col) => _offsets[row] + _rows[row].IndexOf(col);

        /// <summary>The line's rows and columns holding text [<paramref name="start"/>, start + length).</summary>
        public (int FirstRow, ushort FirstCol, int LastRow, ushort LastCol) Cells(int start, int length)
        {
            var count = _rows.Length;
            var a = Array.FindLastIndex(_offsets, count - 1, count, o => o <= start);
            var b = Array.FindLastIndex(_offsets, count - 1, count, o => o <= start + length - 1);
            return (a, _rows[a].Columns(start - _offsets[a], 1).First, b, _rows[b].Columns(start + length - 1 - _offsets[b], 1).Last);
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
    public string? GetSelectionText() => FormatSelection(0);

    /// <summary>
    /// The selection as HTML for the clipboard (copy with formatting): its colors and styles, in the terminal's
    /// background, text color and font (see <see cref="ClipboardHtml"/>). Null when nothing is selected.
    /// </summary>
    public string? GetSelectionHtml(string fontFamily, double fontPoints)
    {
        if (FormatSelection(2) is not { } html)
        {
            return null;
        }

        GhosttyColorRgb fg = TerminalTheme.Default.Foreground, bg = TerminalTheme.Default.Background;
        lock (Lock)
        {
            if (!_disposed)
            {
                // The effective colors: a program's OSC 10/11 change included. (Kept as they are when it has none.)
                Native.TerminalGet(_terminal, TerminalData.ColorForeground, &fg);
                Native.TerminalGet(_terminal, TerminalData.ColorBackground, &bg);
            }
        }

        return ClipboardHtml.Make(html, fg, bg, fontFamily, fontPoints);
    }

    /// <summary>The selection in a GhosttyFormatterFormat: 0 plain, 1 VT sequences, 2 HTML.</summary>
    private string? FormatSelection(int emit)
    {
        lock (Lock)
        {
            var opts = new GhosttyTerminalSelectionFormatOptions
            {
                Size = (nuint)sizeof(GhosttyTerminalSelectionFormatOptions),
                Emit = emit,
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

    /// <summary>How far a link may run across soft-wrapped rows (bounds the work per hover).</summary>
    private const int MaxLinkRows = 32;

    /// <summary>
    /// The link at viewport cell (<paramref name="col"/>, <paramref name="row"/>): an OSC 8
    /// hyperlink, else a URL in the text. Both may continue across soft wraps, including up into
    /// the scrollback.
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

            // Screen rows (scrollback included) from here on; the viewport starts at Offset.
            GhosttyTerminalScrollbar sb;
            Native.TerminalGet(_terminal, TerminalData.Scrollbar, &sb);
            var top = (long)sb.Offset;
            var at = (ulong)(top + row);
            int Viewport(ulong screenRow) => (int)((long)screenRow - top);

            if (HyperlinkAt(col, at) is { Length: > 0 } uri)
            {
                // The link's extent: neighboring cells carrying the same URI, across wraps.
                var (sr, sc) = (at, col);
                for (var i = 0; i < cols * MaxLinkRows && PreviousCell(sr, sc, cols) is { } p && HyperlinkAt(p.Col, p.Row) == uri; i++)
                {
                    (sr, sc) = p;
                }

                var (er, ec) = (at, col);
                for (var i = 0; i < cols * MaxLinkRows && NextCell(er, ec, cols, sb.Total) is { } n && HyperlinkAt(n.Col, n.Row) == uri; i++)
                {
                    (er, ec) = n;
                }

                return new LinkMatch(uri, Viewport(sr), sc, Viewport(er), ec, Explicit: true);
            }

            // Plain text: the URL covering this cell in its logical line (soft-wrapped rows joined).
            var first = at;
            while (first > 0 && at - first < MaxLinkRows && IsWrapped(first - 1))
            {
                first--;
            }

            var last = at;
            while (last + 1 < sb.Total && last - at < MaxLinkRows && IsWrapped(last))
            {
                last++;
            }

            var line = LogicalLine.Read(_terminal, first, (int)(last - first + 1), cols);
            if (UrlDetector.Find(line.Text, line.IndexOf((int)(at - first), col)) is not { } found)
            {
                return null;
            }

            var (s, l) = found;
            var (r1, c1, r2, c2) = line.Cells(s, l);
            return new LinkMatch(line.Text.Substring(s, l), Viewport(first + (ulong)r1), c1, Viewport(first + (ulong)r2), c2,
                Explicit: false);
        }
    }

    /// <summary>The cell before (row, col) in reading order, continuing onto the previous row if this one is its wrap.</summary>
    private (ulong Row, ushort Col)? PreviousCell(ulong row, ushort col, ushort cols) =>
        col > 0 ? (row, (ushort)(col - 1)) : row > 0 && IsWrapped(row - 1) ? (row - 1, (ushort)(cols - 1)) : null;

    /// <summary>The cell after (row, col) in reading order, continuing onto the next row if this one wraps.</summary>
    private (ulong Row, ushort Col)? NextCell(ulong row, ushort col, ushort cols, ulong total) =>
        col + 1 < cols ? (row, (ushort)(col + 1)) : row + 1 < total && IsWrapped(row) ? (row + 1, (ushort)0) : null;

    private string? HyperlinkAt(ushort col, ulong row)
    {
        var gridRef = GhosttyGridRef.Init();
        var point = new GhosttyPoint { Tag = PointTag.Screen, X = col, Y = (uint)row };
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
    private static void OnPwdChanged(nint terminal, nint userdata) => FromUserdata(userdata)._pwdDirty = true;

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

    /// <summary>"zinc 1.0.0+abc1234": the app's version, with a short commit hash if it has one.</summary>
    public static string Version { get; } = ComputeVersion();

    private static string ComputeVersion()
    {
        var info = typeof(TerminalSession).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0";
        var plus = info.IndexOf('+');
        return "zinc " + (plus >= 0 && info.Length > plus + 8 ? info[..(plus + 8)] : info);
    }

    // XTVERSION's answer, in memory that stays put (libghostty reads it after we return).
    private static readonly nint XtversionText = Marshal.StringToCoTaskMemUTF8(Version);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static GhosttyString OnXtversion(nint terminal, nint userdata) =>
        new() { Ptr = XtversionText, Len = (nuint)Encoding.UTF8.GetByteCount(Version) };

    /// <summary>
    /// A program holds the screen while it draws a frame (synchronized output, mode 2026), so
    /// the renderer should keep showing what was on screen when the hold began. Set on the
    /// reader thread, under <see cref="Lock"/>; see <see cref="RenderHoldBegan"/>.
    /// </summary>
    public bool RenderHeld { get; private set; }

    /// <summary>When the current hold began (Environment.TickCount64).</summary>
    public long RenderHeldSince { get; private set; }

    /// <summary>
    /// A hold began: capture the screen now (called synchronously, under <see cref="Lock"/>, on
    /// the reader thread: the renderer updates its snapshot from <see cref="Handle"/> here).
    /// </summary>
    public Action? RenderHoldBegan { get; set; }

    /// <summary>A hold began or ended (after the write that did it, on the reader thread).</summary>
    public event Action<bool>? RenderHoldChanged;

    private bool? _holdChanged;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnRenderHold(nint terminal, nint userdata, byte held)
    {
        var session = FromUserdata(userdata);
        session.RenderHeld = held != 0;
        if (held != 0)
        {
            session.RenderHeldSince = Environment.TickCount64;
            try
            {
                session.RenderHoldBegan?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Crash("render hold", ex);
            }
        }

        session._holdChanged = held != 0;
    }

    /// <summary>
    /// Gives up on a hold the program never released (it crashed mid-frame, say): turns
    /// synchronized output off, as libghostty's docs advise. Call under <see cref="Lock"/>.
    /// </summary>
    public void EndRenderHold()
    {
        var mode = new GhosttyTerminalModeConfig { Mode = GhosttyMode.SyncOutput, Value = 0 };
        Native.TerminalSet(_terminal, TerminalOption.Mode, &mode);
        RenderHeld = false;
    }

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
            // Asking first: only a read the user just allowed (anything else, e.g. one split
            // across reads or OSC 5522, is denied).
            if (session.ClipboardReader is { } reader && (session.ClipboardConsent == null || session._readConsented))
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
        lock (_ptyResizeGate)
        {
            _ptyResizeTimer?.Dispose();
            _ptyResizeTimer = null;
        }

        _pty?.Dispose();
        _flashTimer?.Dispose();
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
            FreeCommands();
            Native.TerminalFree(_terminal);
            _self.Free();
        }
    }
}
