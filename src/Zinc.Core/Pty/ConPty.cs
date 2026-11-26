using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Zinc.Pty;

/// <summary>
/// A child process attached to a Windows pseudoconsole (ConPTY).
/// Output is delivered on a dedicated reader thread via <see cref="OutputReceived"/>.
/// </summary>
public sealed unsafe partial class ConPty : IPty
{
    private readonly nint _hpc;
    private readonly SafeFileHandle _inputWrite;
    private readonly SafeFileHandle _outputRead;
    private readonly SafeWaitHandle _process;

    /// <summary>The child's process id.</summary>
    public int ProcessId { get; }
    private readonly FileStream _input;
    private readonly Thread _reader;
    private readonly object _writeLock = new();
    private int _closed;

    /// <summary>
    /// True when the out-of-box ConPTY (conpty.dll + OpenConsole.exe from the
    /// Microsoft.Windows.Console.ConPTY package) is next to the app. Otherwise
    /// the inbox kernel32 implementation is used.
    /// </summary>
    public static bool UsesBundledConpty { get; } =
        NativeLibrary.TryLoad("conpty.dll", typeof(ConPty).Assembly, DllImportSearchPath.AssemblyDirectory, out _) &&
        File.Exists(Path.Combine(AppContext.BaseDirectory, RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64", "OpenConsole.exe"));

    // Measure cell widths by grapheme cluster, matching libghostty with mode 2027 enabled.
    private const uint PseudoconsoleGlyphWidthGraphemes = 0x08;

    /// <summary>Raised on the reader thread with each chunk of child output.</summary>
    public event Action<ReadOnlySpan<byte>>? OutputReceived;

    /// <summary>Raised once, on a background thread, after the child exits and output is drained.</summary>
    public event Action<uint>? Exited;

    /// <param name="environment">Variables to set for the child on top of ours. A null value removes the variable.</param>
    public ConPty(string commandLine, string? workingDirectory, short cols, short rows,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (!CreatePipe(out var inputRead, out _inputWrite, 0, 0) ||
            !CreatePipe(out _outputRead, out var outputWrite, 0, 0))
        {
            throw new Win32Exception();
        }

        var size = new Coord(cols, rows);
        var hr = UsesBundledConpty
            ? ConptyCreatePseudoConsole(size, inputRead, outputWrite, PseudoconsoleGlyphWidthGraphemes, out _hpc)
            : CreatePseudoConsole(size, inputRead, outputWrite, 0, out _hpc);
        // The pseudoconsole holds its own duplicates of these ends.
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0)
        {
            throw Marshal.GetExceptionForHR(hr)!;
        }

        try
        {
            (_process, ProcessId) = StartProcess(commandLine, workingDirectory, _hpc, environment);
        }
        catch
        {
            // Nothing to run, so let go of the pseudoconsole and our pipe ends before the caller hears why.
            if (UsesBundledConpty)
            {
                ConptyClosePseudoConsole(_hpc);
            }
            else
            {
                ClosePseudoConsole(_hpc);
            }

            _inputWrite.Dispose();
            _outputRead.Dispose();
            throw;
        }
        _input = new FileStream(_inputWrite, FileAccess.Write, 1);

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ConPty reader" };
        _reader.Start();

        var waiter = new Thread(WaitForExit) { IsBackground = true, Name = "ConPty exit waiter" };
        waiter.Start();
    }

    /// <summary>A handed-over console, which starts reading in <see cref="Begin"/> once someone listens.</summary>
    private ConPty(nint hpc, SafeFileHandle inputWrite, SafeFileHandle outputRead, SafeWaitHandle process, int processId)
    {
        (_hpc, _inputWrite, _outputRead, _process, ProcessId) = (hpc, inputWrite, outputRead, process, processId);
        _input = new FileStream(_inputWrite, FileAccess.Write, 1);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ConPty reader" };
        _waiter = new Thread(WaitForExit) { IsBackground = true, Name = "ConPty exit waiter" };
    }

    private readonly Thread? _waiter;

    /// <summary>
    /// Starts reading a handed-over console's output and waiting for its program. Until then the output
    /// waits in the pipe, so none is lost while its pane is made. Started consoles read at once.
    /// </summary>
    public void Begin()
    {
        // ThreadState is a set of flags, so a background thread not started yet is "Background, Unstarted".
        if (_waiter is { } waiter && (waiter.ThreadState & ThreadState.Unstarted) != 0)
        {
            _reader.Start();
            waiter.Start();
        }
    }

    /// <summary>
    /// A console handed over to Zinc as the default terminal (ITerminalHandoff3, from Windows
    /// Terminal's OpenConsole). Its handles become a pseudoconsole as one Zinc started would be
    /// (ConptyPackPseudoConsole), with an input pipe and an output pipe. The handles given are the caller's (COM
    /// closes them after the call), so they're duplicated. <paramref name="theirInput"/> and
    /// <paramref name="theirOutput"/> are the pipes' other ends, to hand back.
    /// </summary>
    public static ConPty FromHandoff(nint server, nint reference, nint signal, nint client, out nint theirInput, out nint theirOutput)
    {
        if (!UsesBundledConpty)
        {
            throw new NotSupportedException("A handed-over console needs the bundled ConPTY (conpty.dll + OpenConsole.exe).");
        }

        // Two pipes, like a pseudoconsole Zinc creates, instead of Windows Terminal's one duplex pipe. A synchronous
        // duplex handle serializes its reads and writes, so a read waiting for output would block the reply to the
        // console's first query and deadlock both sides.
        if (!CreatePipe(out var inputRead, out var inputWrite, 0, 0) || !CreatePipe(out var outputRead, out var outputWrite, 0, 0))
        {
            throw new Win32Exception();
        }

        var self = GetCurrentProcess();
        nint Duplicate(nint handle) =>
            DuplicateHandle(self, handle, self, out var copy, 0, false, DuplicateSameAccess) ? copy : throw new Win32Exception();

        var process = new SafeWaitHandle(Duplicate(client), true);
        var hr = ConptyPackPseudoConsole(Duplicate(server), Duplicate(reference), Duplicate(signal), out var hpc);
        if (hr != 0)
        {
            foreach (var handle in new SafeHandle[] { inputRead, inputWrite, outputRead, outputWrite, process })
            {
                handle.Dispose();
            }

            throw Marshal.GetExceptionForHR(hr)!;
        }

        // Handed back, so COM owns these now.
        theirInput = inputRead.DangerousGetHandle();
        theirOutput = outputWrite.DangerousGetHandle();
        inputRead.SetHandleAsInvalid();
        outputWrite.SetHandleAsInvalid();
        return new ConPty(hpc, inputWrite, outputRead, process, GetProcessId(process)) { ProgramPath = ImagePath(process) };
    }

    /// <summary>
    /// A handed-over console's program (its exe's full path), or null. This isn't the console's title, because a
    /// console started from a shortcut takes the shortcut's name ("Command Prompt") as its title.
    /// </summary>
    public string? ProgramPath { get; private init; }

    private static string? ImagePath(SafeWaitHandle process)
    {
        var buffer = stackalloc char[1024];
        var size = 1024;
        return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || Volatile.Read(ref _closed) != 0)
        {
            return;
        }

        lock (_writeLock)
        {
            try
            {
                _input.Write(data);
                _input.Flush();
            }
            catch (IOException)
            {
                // The child went away, and the exit path reports it.
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Resize(short cols, short rows)
    {
        if (Volatile.Read(ref _closed) == 0)
        {
            if (UsesBundledConpty)
            {
                ConptyResizePseudoConsole(_hpc, new Coord(cols, rows));
            }
            else
            {
                ResizePseudoConsole(_hpc, new Coord(cols, rows));
            }
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[64 * 1024];
        using var stream = new FileStream(_outputRead, FileAccess.Read, 1);
        while (true)
        {
            int n;
            try
            {
                n = stream.Read(buffer, 0, buffer.Length);
            }
            catch (IOException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (n <= 0)
            {
                break;
            }

            try
            {
                OutputReceived?.Invoke(buffer.AsSpan(0, n));
            }
            catch (Exception ex)
            {
                // A bug in handling output (parsing, shell integration and so on) would otherwise end the
                // whole app from this background thread, silently. Record it and keep reading.
                if (_outputFailures++ < 3)
                {
                    Log.Crash("handling terminal output", ex);
                }
            }
        }
    }

    private int _outputFailures;

    private void WaitForExit()
    {
        WaitForSingleObject(_process, Infinite);
        GetExitCodeProcess(_process, out var code);

        // Closing the pseudoconsole flushes remaining output and then breaks
        // the output pipe, which ends the reader loop.
        Close();
        _reader.Join(TimeSpan.FromSeconds(2));
        try
        {
            Exited?.Invoke(code);
        }
        catch (Exception ex)
        {
            Log.Crash("a terminal's program exiting", ex); // on a background thread, so don't take the app down
        }
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        lock (_writeLock)
        {
            _input.Dispose();
        }

        // ClosePseudoConsole may block until the output pipe is drained, and
        // the reader thread is still draining it, so never call this on the
        // reader thread itself.
        if (UsesBundledConpty)
        {
            ConptyClosePseudoConsole(_hpc);
        }
        else
        {
            ClosePseudoConsole(_hpc);
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _closed) == 0)
        {
            TerminateProcess(_process, 1);
        }

        // Closing may block while output drains, so keep it off the UI thread.
        ThreadPool.QueueUserWorkItem(_ => Close());
    }

    private static (SafeWaitHandle Process, int Id) StartProcess(string commandLine, string? workingDirectory, nint hpc,
        IReadOnlyDictionary<string, string?>? environment)
    {
        nuint size = 0;
        InitializeProcThreadAttributeList(null, 1, 0, ref size);
        var attrList = (void*)Marshal.AllocHGlobal((nint)size);
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref size))
            {
                throw new Win32Exception();
            }

            if (!UpdateProcThreadAttribute(attrList, 0, ProcThreadAttributePseudoConsole, (void*)hpc, (nuint)sizeof(nint), null, null))
            {
                throw new Win32Exception();
            }

            var si = new StartupInfoEx();
            si.StartupInfo.cb = sizeof(StartupInfoEx);
            si.lpAttributeList = attrList;
            // Without this flag the child can inherit our (nonexistent) std handles
            // instead of the pseudoconsole's.
            si.StartupInfo.dwFlags = StartfUseStdHandles;

            // CreateProcessW may modify the command line buffer in place.
            var cmd = (commandLine + '\0').ToCharArray();
            var env = environment is { Count: > 0 } ? EnvironmentBlock(environment) : null;
            fixed (char* pCmd = cmd)
            fixed (char* pCwd = workingDirectory)
            fixed (char* pEnv = env)
            {
                if (!CreateProcessW(null, pCmd, null, null, 0, ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                        pEnv, pCwd, &si, out var pi))
                {
                    throw new Win32Exception();
                }

                CloseHandle(pi.hThread);
                return (new SafeWaitHandle(pi.hProcess, ownsHandle: true), pi.dwProcessId);
            }
        }
        finally
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal((nint)attrList);
        }
    }

    /// <summary>Our environment with <paramref name="changes"/> applied, as CreateProcess takes it (sorted, NUL-separated).</summary>
    private static char[] EnvironmentBlock(IReadOnlyDictionary<string, string?> changes)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            vars[(string)e.Key] = (string?)e.Value ?? "";
        }

        foreach (var (name, value) in changes)
        {
            if (value == null)
            {
                vars.Remove(name);
            }
            else
            {
                vars[name] = value;
            }
        }

        var block = new System.Text.StringBuilder();
        foreach (var (name, value) in vars)
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString().ToCharArray();
    }

    // ---- Win32 ----

    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseStdHandles = 0x00000100;
    private const nuint ProcThreadAttributePseudoConsole = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord(short x, short y)
    {
        public short X = x;
        public short Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public nint lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public void* lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint attributes, int size);

    [LibraryImport("kernel32.dll")]
    private static partial int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint hpc);

    [LibraryImport("kernel32.dll")]
    private static partial int ResizePseudoConsole(nint hpc, Coord size);

    [LibraryImport("kernel32.dll")]
    private static partial void ClosePseudoConsole(nint hpc);

    [LibraryImport("conpty.dll")]
    private static partial int ConptyCreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint hpc);

    [LibraryImport("conpty.dll")]
    private static partial int ConptyResizePseudoConsole(nint hpc, Coord size);

    [LibraryImport("conpty.dll")]
    private static partial void ConptyClosePseudoConsole(nint hpc);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(void* list, int count, int flags, ref nuint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(void* list, uint flags, nuint attribute, void* value, nuint size, void* previous, nuint* returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(void* list);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(char* app, char* cmd, void* procAttrs, void* threadAttrs, int inherit,
        uint flags, void* env, char* cwd, StartupInfoEx* si, out ProcessInformation pi);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    private const uint DuplicateSameAccess = 2;

    [LibraryImport("conpty.dll")]
    private static partial int ConptyPackPseudoConsole(nint server, nint reference, nint signal, out nint hpc);



    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateHandle(nint sourceProcess, nint source, nint targetProcess, out nint target, uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    private static partial int GetProcessId(SafeWaitHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(SafeWaitHandle process, uint flags, char* name, ref int size);

    private const uint Infinite = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeWaitHandle process, out uint code);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeWaitHandle process, uint code);
}
