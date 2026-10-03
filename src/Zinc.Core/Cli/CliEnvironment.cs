namespace Zinc.Cli;

/// <summary>What every terminal's shell learns for zn: its own id and its Zinc's pipe, passed into WSL too.</summary>
public static class CliEnvironment
{
    /// <summary>
    /// Adds the variables to <paramref name="environment"/> (a launch's, maybe with shell integration's WSLENV already
    /// in it; else <paramref name="inherited"/>'s WSLENV is the base).
    /// </summary>
    public static void Add(IDictionary<string, string?> environment, int terminalId, string pipe, Func<string, string?> inherited)
    {
        environment[CliPipe.TerminalVariable] = terminalId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        environment[CliPipe.PipeVariable] = pipe;
        var wslenv = environment.TryGetValue("WSLENV", out var set) ? set : inherited("WSLENV");
        var entries = wslenv is { Length: > 0 } ? wslenv.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList() : [];
        foreach (var name in new[] { CliPipe.TerminalVariable, CliPipe.PipeVariable })
        {
            if (!entries.Any(e => e.Split('/')[0] == name))
            {
                entries.Add(name);
            }
        }

        environment["WSLENV"] = string.Join(':', entries);
    }
}
