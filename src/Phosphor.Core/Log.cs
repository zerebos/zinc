namespace Phosphor;

/// <summary>
/// Diagnostics. <see cref="Write"/> is opt-in (PHOSPHOR_LOG=&lt;file&gt; appends timestamped lines);
/// <see cref="Crash"/> always keeps a record, in <see cref="CrashPath"/>, so a failure leaves its
/// stack trace even when nobody was logging. Neither ever throws: a log that can't be written
/// mustn't take the app down with it.
/// </summary>
public static class Log
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("PHOSPHOR_LOG") is { Length: > 0 } p ? p : null;
    private static readonly object Gate = new();

    /// <summary>How big crash.log may grow before its older half is dropped.</summary>
    private const long CrashLogLimit = 512 * 1024;

    public static bool Enabled => Path != null;

    /// <summary>%LOCALAPPDATA%\Phosphor\crash.log (PHOSPHOR_CRASH_LOG=&lt;file&gt; for test runs).</summary>
    public static string CrashPath { get; } =
        Environment.GetEnvironmentVariable("PHOSPHOR_CRASH_LOG") is { Length: > 0 } crash ? crash : System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Phosphor", "crash.log");

    public static void Write(string message)
    {
        if (Path != null)
        {
            Append(Path, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}");
        }
    }

    /// <summary>
    /// Records an exception that escaped (or was caught only to be survived): where it happened,
    /// and everything about it, in crash.log and, if set, the PHOSPHOR_LOG file.
    /// </summary>
    public static void Crash(string where, Exception exception)
    {
        var message = $"crash ({where}): {exception}";
        Write(message);
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
                $"  process {Environment.ProcessId}, {Phosphor.Terminal.TerminalSession.Version}, {AppContext.BaseDirectory}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write it: nothing more to do.
        }
    }

    private static void Append(string path, string line)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log file that's locked or gone: drop the line rather than fail the caller.
        }
    }
}
