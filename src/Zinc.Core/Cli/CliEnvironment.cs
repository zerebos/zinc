namespace Zinc.Cli;

/// <summary>What every terminal's shell learns for zn: its own id and its Zinc's pipe (passed into WSL too), and where zn is.</summary>
public static class CliEnvironment
{
    /// <summary>zn.exe's full path, for WSL shells (WSLENV translates it).</summary>
    public const string ZnVariable = "ZINC_ZN";

    /// <summary>
    /// Adds the variables to <paramref name="environment"/> (a launch's, maybe with shell integration's WSLENV already
    /// in it; else <paramref name="inherited"/>'s WSLENV is the base). <paramref name="znFolder"/>, zn's folder, goes at
    /// the end of PATH: found by name, but never in place of a command of yours (WSL appends Windows' PATH too).
    /// </summary>
    public static void Add(IDictionary<string, string?> environment, int terminalId, string pipe, Func<string, string?> inherited,
        string? znFolder = null)
    {
        if (znFolder != null)
        {
            var path = environment.TryGetValue("PATH", out var own) ? own : inherited("PATH");
            var entries = (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (!entries.Any(e => e.TrimEnd('\\').Equals(znFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                environment["PATH"] = string.Join(';', entries.Append(znFolder));
            }

            // For WSL, whose PATH may not include Windows' (appendWindowsPath = false): the shell integration makes
            // `zn` run this (translated to /mnt/c/...).
            environment[ZnVariable] = Path.Combine(znFolder, "zn.exe");
        }

        environment[CliPipe.TerminalVariable] = terminalId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        environment[CliPipe.PipeVariable] = pipe;
        var wslenv = environment.TryGetValue("WSLENV", out var set) ? set : inherited("WSLENV");
        var shared = wslenv is { Length: > 0 } ? wslenv.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList() : [];
        string[] passed = znFolder != null ? [CliPipe.TerminalVariable, CliPipe.PipeVariable, ZnVariable + "/p"]
            : [CliPipe.TerminalVariable, CliPipe.PipeVariable];
        foreach (var entry in passed)
        {
            if (!shared.Any(e => e.Split('/')[0] == entry.Split('/')[0]))
            {
                shared.Add(entry);
            }
        }

        environment["WSLENV"] = string.Join(':', shared);
    }
}
