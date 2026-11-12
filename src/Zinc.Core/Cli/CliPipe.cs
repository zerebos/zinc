namespace Zinc.Cli;

/// <summary>
/// Where the running Zinc listens, which is a named pipe per session file (the same key that keeps Zinc to one
/// running copy), so a test Zinc and yours never answer each other's zn. A run without a session (tests with only ZINC_CONFIG) gets
/// one per process, which only its own terminals know (ZINC_PIPE).
/// </summary>
public static class CliPipe
{
    /// <summary>The variable every terminal gets with its Zinc's pipe name.</summary>
    public const string PipeVariable = "ZINC_PIPE";

    /// <summary>The variable every terminal gets with its own id.</summary>
    public const string TerminalVariable = "ZINC_TERMINAL_ID";

    /// <summary>
    /// The session file a Zinc started now would use (ZINC_SESSION, else none when ZINC_CONFIG is set, else
    /// %APPDATA%\Zinc\session.json). Zinc's SessionStore uses this too.
    /// </summary>
    public static string? SessionPath(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        if (getEnv("ZINC_SESSION") is { Length: > 0 } explicitPath)
        {
            return explicitPath;
        }

        if (getEnv("ZINC_CONFIG") is { Length: > 0 })
        {
            return null;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zinc", "session.json");
    }

    /// <summary>
    /// The name that one running Zinc per session file goes by: its single-instance key, and the name of the lock
    /// guarding the file. Same file, same key, however the path is cased.
    /// </summary>
    public static string InstanceKey(string sessionPath) =>
        "Zinc." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(sessionPath).ToLowerInvariant())))[..16];

    /// <summary>The pipe of the Zinc that owns <paramref name="sessionPath"/>.</summary>
    public static string ForSession(string sessionPath) => InstanceKey(sessionPath) + ".cli";

    /// <summary>The pipe of a Zinc without a session (a test run).</summary>
    public static string ForProcess(int processId) => $"Zinc.p{processId}.cli";

    /// <summary>
    /// The pipe zn talks to, which is its terminal's Zinc (ZINC_PIPE), else the one that owns the session, or null if
    /// neither.
    /// </summary>
    public static string? Find(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        if (getEnv(PipeVariable) is { Length: > 0 } pipe)
        {
            return pipe;
        }

        return SessionPath(getEnv) is { } session ? ForSession(session) : null;
    }
}
