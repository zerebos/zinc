namespace Zinc.Terminal;

/// <summary>A shell's start with Zinc's integration in it: its command line, and variables to set for it.</summary>
public sealed record ShellLaunch(string CommandLine, IReadOnlyDictionary<string, string?> Environment);

/// <summary>
/// Loads Zinc's shell integration (the scripts in <c>ShellIntegration\</c> next to the exe) into
/// the shells Zinc starts, after the user's own startup files: prompt and command marks (OSC 133)
/// and folder reports. A shell can't be told to load a file from outside, but how it starts can be
/// changed. PowerShell gets <c>-NoExit -Command</c> (it runs after the profile); bash and zsh look
/// up their startup in environment variables (<c>PROMPT_COMMAND</c> runs once at the first prompt,
/// <c>ZDOTDIR</c> points zsh at a .zshenv that restores the user's and loads theirs), passed into
/// WSL through <c>WSLENV</c>. Each
/// script stands down where the prompt already marks itself (powerlevel10k's own integration, or a
/// hand-written one) and removes its variables so programs started from the shell don't inherit them.
/// </summary>
public static class ShellIntegration
{
    /// <summary>The scripts' folder next to the exe.</summary>
    public const string FolderName = "ShellIntegration";

    /// <summary>What bash's first PROMPT_COMMAND runs (the script removes it again).</summary>
    public const string BashBootstrap = ". \"$ZINC_SHELL_INTEGRATION_DIR/bash/zinc.bash\"";

    /// <summary>
    /// <paramref name="commandLine"/> with the integration in <paramref name="scripts"/> added, or as
    /// it was (and no variables) where it can't be: a PowerShell told to run something else, or no scripts.
    /// </summary>
    /// <param name="environment">Reads the variables the child would inherit (ZDOTDIR, WSLENV).</param>
    public static ShellLaunch Prepare(string commandLine, string scripts, Func<string, string?> environment)
    {
        var none = new ShellLaunch(commandLine, new Dictionary<string, string?>());
        var args = Split(commandLine);
        if (args.Count == 0)
        {
            return none;
        }

        var exe = Path.GetFileNameWithoutExtension(args[0]).ToLowerInvariant();
        if (exe is "pwsh" or "powershell")
        {
            var (runs, noExit) = PowerShellArguments(args.Skip(1));
            if (runs)
            {
                return none;
            }

            // A script block rather than a script file: execution policies (Windows PowerShell's
            // default refuses .ps1 files) apply to files only.
            var file = Path.Combine(scripts, "pwsh", "zinc.ps1").Replace("'", "''");
            return none with
            {
                CommandLine = $"{commandLine}{(noExit ? "" : " -NoExit")} -Command \". ([scriptblock]::Create([IO.File]::ReadAllText('{file}')))\"",
            };
        }

        // The rest (bash, zsh, and wsl.exe, cmd.exe or a launcher script starting one): variables.
        // Forward slashes: MSYS programs read C:/x as a path, and WSLENV's /p translates it (/mnt/c/x).
        var dir = scripts.Replace('\\', '/').TrimEnd('/');
        var env = new Dictionary<string, string?>
        {
            ["ZINC_SHELL_INTEGRATION_DIR"] = dir,
            ["PROMPT_COMMAND"] = BashBootstrap,
            ["ZDOTDIR"] = dir + "/zsh",
        };
        List<string> wsl = ["ZINC_SHELL_INTEGRATION_DIR/p", "PROMPT_COMMAND", "ZDOTDIR/p"];
        if (environment("ZDOTDIR") is { Length: > 0 } zdotdir)
        {
            env["ZINC_ZDOTDIR"] = zdotdir; // .zshenv puts it back
        }

        var existing = environment("WSLENV") is { Length: > 0 } w ? w.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList() : [];
        env["WSLENV"] = string.Join(':', existing.Concat(wsl.Where(v => !existing.Contains(v))));
        return none with { Environment = env };
    }

    /// <summary>Whether PowerShell's arguments already run something (then it's left alone), and whether they include -NoExit.</summary>
    internal static (bool Runs, bool NoExit) PowerShellArguments(IEnumerable<string> args)
    {
        // Parameters that take a value (any unambiguous prefix works, as in PowerShell itself).
        string[] withValue = ["workingdirectory", "wd", "executionpolicy", "ep", "ex", "windowstyle", "w", "configurationname",
            "version", "v", "outputformat", "of", "o", "inputformat", "if", "settingsfile", "custompipename", "psconsolefile",
            "configurationfile"];
        var noExit = false;
        var skip = false;
        foreach (var arg in args)
        {
            if (skip)
            {
                skip = false;
                continue;
            }

            if (arg.Length < 2 || arg[0] is not ('-' or '/'))
            {
                return (true, noExit); // a script or command, positionally (or "-": read stdin)
            }

            var name = arg.TrimStart('-', '/').Split(':')[0].ToLowerInvariant();
            if (Prefix(name, "command") || Prefix(name, "commandwithargs") || name is "cwa" or "ec" ||
                Prefix(name, "file") || Prefix(name, "encodedcommand"))
            {
                return (true, noExit);
            }

            if (name.Length >= 3 && Prefix(name, "noexit"))
            {
                noExit = true;
            }
            else if (!arg.Contains(':') && withValue.Any(p => p == name || name.Length >= 2 && Prefix(name, p)))
            {
                skip = true;
            }
        }

        return (false, noExit);

        static bool Prefix(string name, string full) => name.Length > 0 && full.StartsWith(name, StringComparison.Ordinal);
    }

    /// <summary>A command line split into arguments (double quotes group, and are removed).</summary>
    internal static List<string> Split(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            args.Add(current.ToString());
        }

        return args;
    }
}
