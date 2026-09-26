namespace Phantom;

/// <summary>Opt-in diagnostics: set PHANTOM_LOG=&lt;file&gt; to append timestamped lines.</summary>
public static class Log
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("PHANTOM_LOG") is { Length: > 0 } p ? p : null;
    private static readonly object Gate = new();

    public static bool Enabled => Path != null;

    public static void Write(string message)
    {
        if (Path == null)
        {
            return;
        }

        lock (Gate)
        {
            File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
        }
    }
}
