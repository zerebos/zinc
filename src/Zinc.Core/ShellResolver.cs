using System.Text;

namespace Zinc;

/// <summary>Picks the command line to run inside the terminal.</summary>
public static class ShellResolver
{
    /// <summary>Resolves the command for this process (its args, environment and PATH).</summary>
    public static string Resolve(Config config) =>
        Resolve(config, OwnArguments(), Environment.GetEnvironmentVariable);

    /// <summary>The flag for starting at sign-in (the Run entry Zinc writes), which isn't a command to run.</summary>
    public const string LoginFlag = "--at-login";

    /// <summary>Our command line after the executable, without <see cref="LoginFlag"/> or COM's <c>-Embedding</c>.</summary>
    public static string[] LaunchArguments() =>
        [.. Environment.GetCommandLineArgs().Skip(1).Where(a => a != LoginFlag && a != Zinc.Pty.DefaultTerminal.EmbeddingFlag)];

    /// <summary>The command our command line gives (none when it's Zinc's own flags, which <see cref="LaunchRequest"/> reads).</summary>
    public static string[] OwnArguments() => LaunchRequest.Parse(LaunchArguments()) is { Action: LaunchAction.None } request
        ? [.. request.Command]
        : [];

    /// <summary>Whether Windows started this process to hand it a console (as the default terminal).</summary>
    public static bool LaunchedForHandoff => Environment.GetCommandLineArgs().Skip(1).Contains(Zinc.Pty.DefaultTerminal.EmbeddingFlag);

    /// <summary>Whether this launch came from the sign-in entry.</summary>
    public static bool LaunchedAtLogin => Environment.GetCommandLineArgs().Skip(1).Contains(LoginFlag);

    /// <param name="args">Our own arguments, excluding the executable.</param>
    /// <param name="getEnv">Environment lookup (injectable for tests).</param>
    public static string Resolve(Config config, IReadOnlyList<string> args, Func<string, string?> getEnv)
    {
        // 1. Anything after the executable on our own command line: `Zinc.exe wsl.exe -d Ubuntu`.
        if (args.Count > 0)
        {
            return string.Join(' ', args.Select(Quote));
        }

        // 2. An explicit override.
        var fromEnv = getEnv("ZINC_SHELL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        // 3. The profile's `command`, else 4. a default shell.
        return ForProfile(config, getEnv);
    }

    /// <summary>
    /// The command for a profile opened from the UI (new tab/pane), which is its `command` or else a
    /// default shell. Our own arguments and ZINC_SHELL only apply to the first tab.
    /// </summary>
    public static string ForProfile(Config profile, Func<string, string?>? getEnv = null)
    {
        if (profile.Command is { } command)
        {
            return command;
        }

        // PowerShell 7, then Windows PowerShell, then cmd.
        var path = (getEnv ?? Environment.GetEnvironmentVariable)("PATH") ?? "";
        return FindOnPath(path, "pwsh.exe") ?? FindOnPath(path, "powershell.exe") ?? "cmd.exe";
    }

    internal static string? FindOnPath(string path, string exe)
    {
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), exe);
            if (File.Exists(candidate))
            {
                return Quote(candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// Quotes one argument so CommandLineToArgvW (and the MSVC CRT) parse it back
    /// unchanged. Backslashes are literal except before a quote, where they must be doubled.
    /// </summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // Escape the preceding backslashes and the quote itself.
                sb.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                sb.Append('\\', backslashes);
            }

            backslashes = 0;
            sb.Append(c);
        }

        // Backslashes before the closing quote must be doubled too.
        sb.Append('\\', backslashes * 2);
        return sb.Append('"').ToString();
    }
}
