namespace Phantom;

/// <summary>Picks the command line to run inside the terminal.</summary>
public static class ShellResolver
{
    public static string Resolve()
    {
        // 1. Anything after the executable on our own command line: `Phantom.exe wsl.exe -d Ubuntu`.
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            return string.Join(' ', args.Skip(1).Select(Quote));
        }

        // 2. An explicit override.
        var fromEnv = Environment.GetEnvironmentVariable("PHANTOM_SHELL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        // 3. PowerShell 7, then Windows PowerShell, then cmd.
        return FindOnPath("pwsh.exe") ?? FindOnPath("powershell.exe") ?? "cmd.exe";
    }

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
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

    private static string Quote(string arg) =>
        arg.Length > 0 && !arg.Contains(' ') && !arg.Contains('"') ? arg : '"' + arg.Replace("\"", "\\\"") + '"';
}
