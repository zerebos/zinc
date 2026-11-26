using System.Runtime.InteropServices;
using System.Text;

namespace Zinc.Pty;

/// <summary>
/// Spike: a child process on a POSIX pseudoterminal. It keeps ConPty's name and members so TerminalSession compiles
/// unchanged. A real port would put both behind one interface.
/// </summary>
public sealed unsafe partial class ConPty : IDisposable
{
    private const string Lib = "zinc-pty";
    private readonly int _master;
    private readonly Thread _reader;
    private readonly object _writeLock = new();
    private int _closed;

    public int ProcessId { get; }

    /// <summary>Always true here, which turns grapheme clustering (mode 2027) on, as Ghostty does on macOS.</summary>
    public static bool UsesBundledConpty => true;

    public string? ProgramPath => null;

    public event Action<ReadOnlySpan<byte>>? OutputReceived;

    public event Action<uint>? Exited;

    public ConPty(string commandLine, string? workingDirectory, short cols, short rows,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var args = Split(commandLine);
        var file = Resolve(args[0]) ?? throw new FileNotFoundException($"{args[0]} wasn't found.");

        var variables = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            variables[(string)e.Key] = (string?)e.Value ?? "";
        }

        variables["TERM"] = "xterm-256color";
        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value == null)
            {
                variables.Remove(name);
            }
            else
            {
                variables[name] = value;
            }
        }

        var argv = Block(args);
        var envp = Block(variables.Select(v => $"{v.Key}={v.Value}"));
        try
        {
            ProcessId = zinc_pty_spawn(file, argv, envp, workingDirectory, (ushort)cols, (ushort)rows, out _master);
        }
        finally
        {
            Free(argv);
            Free(envp);
        }

        if (ProcessId < 0)
        {
            throw new InvalidOperationException($"forkpty failed with errno {Marshal.GetLastPInvokeError()}.");
        }

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty reader" };
    }

    public void Begin() => _reader.Start();

    private void ReadLoop()
    {
        var buffer = new byte[64 * 1024];
        fixed (byte* p = buffer)
        {
            while (true)
            {
                var n = read(_master, p, (nuint)buffer.Length);
                if (n > 0)
                {
                    try
                    {
                        OutputReceived?.Invoke(new ReadOnlySpan<byte>(p, (int)n));
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"pty: output handler threw: {ex}");
                    }
                }
                else if (n == 0 || Marshal.GetLastPInvokeError() != 4) // 4 is EINTR. The child closing its side reads as EIO.
                {
                    break;
                }
            }
        }

        var code = zinc_pty_wait(ProcessId);
        Volatile.Write(ref _closed, 1);
        close(_master);
        try
        {
            Exited?.Invoke((uint)code);
        }
        catch (Exception ex)
        {
            Log.Write($"pty: exit handler threw: {ex}");
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || Volatile.Read(ref _closed) != 0)
        {
            return;
        }

        lock (_writeLock)
        {
            fixed (byte* p = data)
            {
                for (var sent = 0; sent < data.Length;)
                {
                    var n = write(_master, p + sent, (nuint)(data.Length - sent));
                    if (n > 0)
                    {
                        sent += (int)n;
                    }
                    else if (Marshal.GetLastPInvokeError() != 4)
                    {
                        return;
                    }
                }
            }
        }
    }

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Resize(short cols, short rows)
    {
        if (Volatile.Read(ref _closed) == 0)
        {
            zinc_pty_resize(_master, (ushort)cols, (ushort)rows);
        }
    }

    /// <summary>Hangs up on the child. The reader thread then sees the end of its output and closes the descriptor.</summary>
    public void Dispose()
    {
        if (Volatile.Read(ref _closed) == 0)
        {
            kill(ProcessId, 1); // SIGHUP
        }
    }

    private static List<string> Split(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var any = false;
        foreach (var c in commandLine)
        {
            if (quote != '\0')
            {
                if (c == quote) { quote = '\0'; } else { current.Append(c); }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                any = true;
            }
            else if (c == ' ')
            {
                if (any || current.Length > 0) { args.Add(current.ToString()); }
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
            }
        }

        if (any || current.Length > 0) { args.Add(current.ToString()); }
        return args;
    }

    private static string? Resolve(string program)
    {
        if (program.Contains('/'))
        {
            return File.Exists(program) ? program : null;
        }

        return (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':')
            .Select(dir => Path.Combine(dir, program)).FirstOrDefault(File.Exists);
    }

    private static nint* Block(IEnumerable<string> strings)
    {
        var list = strings.ToList();
        var block = (nint*)NativeMemory.AllocZeroed((nuint)(list.Count + 1), (nuint)sizeof(nint));
        for (var i = 0; i < list.Count; i++)
        {
            block[i] = Marshal.StringToCoTaskMemUTF8(list[i]);
        }

        return block;
    }

    private static void Free(nint* block)
    {
        for (var p = block; *p != 0; p++)
        {
            Marshal.FreeCoTaskMem(*p);
        }

        NativeMemory.Free(block);
    }

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int zinc_pty_spawn(string file, nint* argv, nint* envp, string? cwd, ushort cols, ushort rows, out int master);

    [LibraryImport(Lib)]
    private static partial int zinc_pty_resize(int master, ushort cols, ushort rows);

    [LibraryImport(Lib)]
    private static partial int zinc_pty_wait(int pid);

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint read(int fd, byte* buffer, nuint count);

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint write(int fd, byte* buffer, nuint count);

    [LibraryImport("libc")]
    private static partial int close(int fd);

    [LibraryImport("libc")]
    private static partial int kill(int pid, int signal);
}
