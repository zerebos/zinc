using System.Text;

namespace Phantom;

/// <summary>Picks the command line to run inside the terminal.</summary>
public static class ShellResolver
{
    /// <summary>Resolves the command for this process (its args, environment and PATH).</summary>
    public static string Resolve(Config config) =>
        Resolve(config, Environment.GetCommandLineArgs().Skip(1).ToArray(), Environment.GetEnvironmentVariable);

    /// <param name="args">Our own arguments, excluding the executable.</param>
    /// <param name="getEnv">Environment lookup (injectable for tests).</param>
    public static string Resolve(Config config, IReadOnlyList<string> args, Func<string, string?> getEnv)
    {
        // 1. Anything after the executable on our own command line: `Phantom.exe wsl.exe -d Ubuntu`.
        if (args.Count > 0)
        {
            return string.Join(' ', args.Select(Quote));
        }

        // 2. An explicit override.
        var fromEnv = getEnv("PHANTOM_SHELL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        // 3. `command` from the config file.
        if (config.Command is { } command)
        {
            return command;
        }

        // 4. PowerShell 7, then Windows PowerShell, then cmd.
        var path = getEnv("PATH") ?? "";
        return FindOnPath(path, "pwsh.exe") ?? FindOnPath(path, "powershell.exe") ?? "cmd.exe";
    }

    private static string? FindOnPath(string path, string exe)
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
    /// unchanged: backslashes are literal except before a quote, where they must be doubled.
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
