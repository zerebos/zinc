using System.Text.RegularExpressions;
using Zinc.Terminal;

namespace Zinc.Cli;

/// <summary>How a program given to zn (<c>zn pane split -- htop</c>) starts under a profile.</summary>
public static partial class CliLaunch
{
    /// <summary>
    /// The command line for <paramref name="program"/> where <paramref name="shell"/> (a profile's command, already
    /// placed in its folder) would have run: inside the same WSL distro for a WSL profile (its <c>-d</c> and
    /// <c>--cd</c> kept, whatever it ran replaced), else the program itself.
    /// </summary>
    public static string WithProgram(string shell, string program)
    {
        if (!ShellDirectory.IsWsl(shell))
        {
            return program;
        }

        var options = RunsSomething().Match(shell) is { Success: true } m ? shell[..m.Index] : shell;
        return $"{options.TrimEnd()} -- {program}";
    }

    // wsl.exe's own options end where it's told what to run: "--" or "-e"/"--exec".
    [GeneratedRegex(@"\s(--|-e|--exec)(\s|$)")]
    private static partial Regex RunsSomething();
}
