namespace Zinc;

/// <summary>
/// Diagnostics. <see cref="Write"/> is opt-in. It writes to the debug log (<c>[debug] logging</c>, switched with
/// <see cref="SetEnabled"/>, into <see cref="FilePath"/>), or to ZINC_LOG=&lt;file&gt;, which wins and
/// is always on. <see cref="Crash"/> always keeps a record, in <see cref="CrashPath"/>, so a failure
/// leaves its stack trace even when nobody was logging. Neither ever throws, because a log that can't be
/// written mustn't take the app down with it.
/// </summary>
public static class Log
{
    private static readonly string? EnvironmentPath = Environment.GetEnvironmentVariable("ZINC_LOG") is { Length: > 0 } p ? p : null;
    private static readonly object Gate = new();
    private static volatile string? _path = EnvironmentPath;
    private static int _sinceSizeCheck;

    /// <summary>How big crash.log may grow before its older half is dropped.</summary>
    private const long CrashLogLimit = 512 * 1024;

    /// <summary>How big the debug log may grow before it's moved aside (to <see cref="OldFilePath"/>) and started again.</summary>
    private const long LogLimit = 4 * 1024 * 1024;

    public static bool Enabled => _path != null;

    /// <summary>Whether ZINC_LOG decides (then the setting changes nothing).</summary>
    public static bool FromEnvironment => EnvironmentPath != null;

    private static string Folder => AppFolders.Data;

    /// <summary>The debug log, which is ZINC_LOG's file or else %APPDATA%\Zinc\logs\zinc.log.</summary>
    public static string FilePath { get; } = EnvironmentPath ?? System.IO.Path.Combine(Folder, "logs", "zinc.log");

    /// <summary>The debug log before its last move aside.</summary>
    public static string OldFilePath => System.IO.Path.ChangeExtension(FilePath, ".old.log");

    /// <summary>%APPDATA%\Zinc\crash.log (ZINC_CRASH_LOG=&lt;file&gt; for test runs).</summary>
    public static string CrashPath { get; } =
        Environment.GetEnvironmentVariable("ZINC_CRASH_LOG") is { Length: > 0 } crash ? crash : System.IO.Path.Combine(Folder, "crash.log");

    /// <summary>Turns the debug log on or off (nothing changes while ZINC_LOG is set).</summary>
    public static void SetEnabled(bool on)
    {
        if (FromEnvironment || on == Enabled)
        {
            return;
        }

        if (on)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return; // nowhere to write, so it stays off
            }

            _path = FilePath;
            Write($"log: on ({About})");
        }
        else
        {
            Write("log: off");
            _path = null;
        }
    }

    /// <summary>The version, Windows and where Zinc runs from, which are the first things a bug report needs.</summary>
    public static string About =>
        $"{Zinc.Terminal.TerminalSession.Version}, Windows {Environment.OSVersion.Version}, " +
        $"{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}, {AppContext.BaseDirectory}";

    /// <summary>
    /// Adds a line. It's queued and written by a background thread, so logging never holds up the caller with a file
    /// write (and an antivirus scan of it) on the UI thread.
    /// </summary>
    public static void Write(string message)
    {
        if (_path is { } path)
        {
            // ZINC_LOG's lines keep their short time (test tools read them); the debug log may span days.
            var time = FromEnvironment ? $"{DateTime.Now:HH:mm:ss.fff}" : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}";
            Pending.Enqueue((path, $"{time} [{Environment.CurrentManagedThreadId}] {message}"));
            StartWriter();
            Wake.Set();
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentQueue<(string Path, string Line)> Pending = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static int _writerStarted;

    private static void StartWriter()
    {
        if (Interlocked.Exchange(ref _writerStarted, 1) == 1)
        {
            return;
        }

        new Thread(() =>
        {
            while (true)
            {
                Wake.WaitOne();
                Flush();
            }
        }) { IsBackground = true, Name = "Zinc log writer" }.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(); // what's queued when Zinc quits
    }

    /// <summary>Writes out every queued line now (before reading or clearing the log, or after a crash).</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            // Writes in batches of consecutive lines for the same file, with one file open per batch.
            var batch = new System.Text.StringBuilder();
            string? batchPath = null;
            while (Pending.TryPeek(out var next))
            {
                if (batchPath != null && next.Path != batchPath)
                {
                    Append(batchPath, batch.ToString(), rotate: !FromEnvironment);
                    batch.Clear();
                }

                Pending.TryDequeue(out _);
                batchPath = next.Path;
                batch.Append(next.Line).Append(Environment.NewLine);
            }

            if (batchPath != null)
            {
                Append(batchPath, batch.ToString(), rotate: !FromEnvironment);
            }
        }
    }

    /// <summary>
    /// The debug log's text (the moved-aside part first, then the current one), at most
    /// <paramref name="limit"/> characters from the end; "" if there's none.
    /// </summary>
    public static string ReadText(int limit = 1024 * 1024)
    {
        Flush();
        lock (Gate)
        {
            var text = string.Concat(ReadShared(OldFilePath), ReadShared(FilePath));
            return text.Length > limit ? text[^limit..] : text;
        }
    }

    /// <summary>Empties the debug log (both parts), so it holds only what happens next.</summary>
    public static void Clear()
    {
        Flush(); // nothing queued from before lands after the clear
        lock (Gate)
        {
            try
            {
                File.Delete(OldFilePath);
                if (File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, "");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held open elsewhere, so keep it.
            }
        }

        Write($"log: cleared ({About})");
    }

    private static string ReadShared(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "";
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// Records an exception that escaped (or was caught only to be survived): where it happened,
    /// and everything about it, in crash.log and, if on, the debug log. <paramref name="details"/> are lines that
    /// explain it, such as XAML's description of a layout cycle.
    /// </summary>
    public static void Crash(string where, Exception exception, IReadOnlyList<string>? details = null)
    {
        var message = $"crash ({where}): {exception}";
        if (details is { Count: > 0 })
        {
            message += $"{Environment.NewLine}  what led to it:{Environment.NewLine}    " +
                string.Join($"{Environment.NewLine}    ", details);
        }

        Write(message);
        Flush(); // the process may be about to end
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashPath)!);
            if (File.Exists(CrashPath) && new FileInfo(CrashPath).Length > CrashLogLimit)
            {
                // Keep the newer half.
                var text = File.ReadAllText(CrashPath);
                File.WriteAllText(CrashPath, text[(text.Length / 2)..]);
            }

            Append(CrashPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}" +
                $"  process {Environment.ProcessId}, {Zinc.Terminal.TerminalSession.Version}, {AppContext.BaseDirectory}{Environment.NewLine}", rotate: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write it, so there's nothing more to do.
        }
    }

    /// <summary>Appends <paramref name="text"/> (whole lines, each ending in a line break) to <paramref name="path"/>.</summary>
    private static void Append(string path, string text, bool rotate)
    {
        try
        {
            lock (Gate)
            {
                // Checked every few dozen batches, not on each one.
                if (rotate && ++_sinceSizeCheck >= 50)
                {
                    _sinceSizeCheck = 0;
                    if (File.Exists(path) && new FileInfo(path).Length > LogLimit)
                    {
                        File.Move(path, OldFilePath, overwrite: true);
                    }
                }

                File.AppendAllText(path, text);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log file that's locked or gone drops the line rather than fail the caller.
        }
    }
}
