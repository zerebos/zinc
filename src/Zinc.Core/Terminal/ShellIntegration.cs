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
            var (runs, noExit, command) = PowerShellArguments(args.Skip(1));

            // A script block rather than a script file: execution policies (Windows PowerShell's
            // default refuses .ps1 files) apply to files only.
            var file = Path.Combine(scripts, "pwsh", "zinc.ps1").Replace("'", "''");
            var load = $". ([scriptblock]::Create([IO.File]::ReadAllText('{file}')))";
            if (runs)
            {
                // A -Command that stays open afterwards (Developer PowerShell for VS: -NoExit -Command "&{Enter-VsDevShell
                // ...}") runs ours after it: PowerShell joins every argument after -Command into the command. Anything
                // else that runs something (no -NoExit, -File, a script) is left alone.
                return noExit && command ? none with { CommandLine = $"{commandLine} \"; {load}\"" } : none;
            }

            return none with
            {
                CommandLine = $"{commandLine}{(noExit ? "" : " -NoExit")} -Command \"{load}\"",
            };
        }

        // fish and nushell send their own marks, and never run the scripts that take our variables back out: they'd
        // be left for every program started from them. Only when the command line says so (wsl.exe -- fish); a distro
        // whose login shell is fish still gets them.
        if (MarksItself(exe) || (exe == "wsl" && ProgramAfterOptions(args) is { } program &&
                                 MarksItself(Path.GetFileNameWithoutExtension(program).ToLowerInvariant())))
        {
            return none;
        }

        // elvish (Windows' own): started with -rc pointing at ours, which runs the user's rc.elv first (or the -rc they gave,
        // passed in ZINC_ELVISH_RC). Left alone when it runs something (-c, a script) or reads no rc (-norc).
        if (exe == "elvish" && ElvishArguments(args.Skip(1)) is var (elvishArgs, userRc))
        {
            var rcEnv = new Dictionary<string, string?>();
            if (userRc != null)
            {
                rcEnv["ZINC_ELVISH_RC"] = userRc;
            }

            var ours = Path.Combine(scripts, "elvish", "zinc.elv");
            return new ShellLaunch(string.Join(' ', [ShellResolver.Quote(args[0]), "-rc", ShellResolver.Quote(ours), .. elvishArgs.Select(ShellResolver.Quote)]), rcEnv);
        }

        // Forward slashes: MSYS programs read C:/x as a path, and WSLENV's /p translates it (/mnt/c/x).
        var dir = scripts.Replace('\\', '/').TrimEnd('/');

        // bash started directly (Git Bash, MSYS2's bash.exe): in POSIX mode with ENV pointing at our startup file, which
        // reads the user's startup files as bash would and loads the integration after them, so a startup file that
        // assigns PROMPT_COMMAND outright can't drop it (the PROMPT_COMMAND bootstrap below, which bash started by
        // anything else gets, runs only if the startup files keep it).
        if (exe == "bash" && BashArguments(args.Skip(1)) is { } bash)
        {
            var bashEnv = new Dictionary<string, string?>
            {
                ["ENV"] = dir + "/bash/startup.bash",
                ["ZINC_BASH_INJECT"] = string.Join(' ', ["1", .. bash.Flags]),
                ["ZINC_SHELL_INTEGRATION_DIR"] = dir,
            };
            if (environment("ENV") is { Length: > 0 } userEnv)
            {
                bashEnv["ZINC_BASH_ENV"] = userEnv; // put back by startup.bash
            }

            if (bash.RcFile is { } rc)
            {
                bashEnv["ZINC_BASH_RCFILE"] = rc;
            }

            if (environment("HISTFILE") is not { Length: > 0 })
            {
                bashEnv["ZINC_BASH_INJECT"] += " histfile"; // POSIX mode's default is ~/.sh_history
            }

            return new ShellLaunch(string.Join(' ', [ShellResolver.Quote(args[0]), "--posix", .. bash.Args.Select(ShellResolver.Quote)]), bashEnv);
        }

        // The rest (zsh, bash that runs a script, and wsl.exe, cmd.exe or a launcher script starting one): variables.
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

        // cmd: its PROMPT carries the marks (the variables above still reach a bash or zsh it starts). Under Clink,
        // clink/zinc.lua (on CLINK_PATH, and only when ZINC_CMD_PROMPT says Zinc set this cmd up) strips PROMPT's marks
        // and sends its own, with exit codes and the command's text.
        if (exe == "cmd" && CmdPrompt(environment("PROMPT")) is { } prompt)
        {
            env["PROMPT"] = prompt;
            env["ZINC_CMD_PROMPT"] = "1";
            var clink = scripts.TrimEnd('\\', '/') + @"\clink";
            env["CLINK_PATH"] = environment("CLINK_PATH") is { Length: > 0 } paths ? paths + ";" + clink : clink;
        }

        return none with { Environment = env };
    }

    /// <summary>
    /// cmd's prompt with the marks around <paramref name="prompt"/> (its PROMPT, else cmd's default $P$G): the last
    /// command finished (D: cmd has no exit code to give), a prompt starts (A, saying Zinc marks the command's start on
    /// Enter: cmd has no hook for it), the folder (OSC 9;9), and typing starts (B). $e is ESC; ESC \ ends each. Null when
    /// the prompt already sends marks of its own.
    /// </summary>
    internal static string? CmdPrompt(string? prompt)
    {
        prompt = string.IsNullOrEmpty(prompt) ? "$P$G" : prompt;
        if (prompt.Contains("133;", StringComparison.Ordinal))
        {
            return null;
        }

        return $"$e]133;D$e\\$e]133;A;{TerminalSession.COnEnter}$e\\$e]9;9;$P$e\\{prompt}$e]133;B$e\\";
    }

    /// <summary>
    /// elvish's arguments for a start with our -rc: the rest kept, and the user's own -rc (read by ours). Null when it
    /// won't read an rc interactively: -c, a script, -norc, or a mode that isn't the shell (-compileonly, -lsp, ...).
    /// </summary>
    internal static (List<string> Args, string? Rc)? ElvishArguments(IEnumerable<string> args)
    {
        List<string> kept = [];
        string? rc = null;
        using var each = args.GetEnumerator();
        while (each.MoveNext())
        {
            var arg = each.Current;
            var name = arg.TrimStart('-').Split('=')[0];
            if (!arg.StartsWith('-') || name is "c" or "norc" or "compileonly" or "lsp" or "daemon" or "help" or "version" or "buildinfo" or "json")
            {
                return null;
            }

            if (name == "rc")
            {
                if (arg.Contains('='))
                {
                    rc = arg[(arg.IndexOf('=') + 1)..];
                }
                else if (each.MoveNext())
                {
                    rc = each.Current;
                }
                else
                {
                    return null;
                }

                continue;
            }

            kept.Add(arg);
            if (name is "sock" or "db" or "log" or "deprecation-level" && !arg.Contains('=') && each.MoveNext())
            {
                kept.Add(each.Current); // a flag that takes a value
            }
        }

        return (kept, rc);
    }

    /// <summary>
    /// bash's arguments for a start in POSIX mode: what's kept, the startup flags startup.bash goes by (--login stays,
    /// --norc and --noprofile become flags) and an --rcfile. Null where bash won't be interactive or POSIX mode doesn't
    /// fit: -c, a script, --posix (already), -s or anything after - and -- (they'd be read differently), restricted.
    /// </summary>
    internal static (List<string> Args, List<string> Flags, string? RcFile)? BashArguments(IEnumerable<string> args)
    {
        List<string> kept = [];
        List<string> flags = [];
        string? rcfile = null;
        using var each = args.GetEnumerator();
        while (each.MoveNext())
        {
            var arg = each.Current;
            switch (arg)
            {
                case "--norc" or "--noprofile":
                    flags.Add(arg);
                    break;
                case "--rcfile" or "--init-file":
                    if (!each.MoveNext())
                    {
                        return null;
                    }

                    rcfile = each.Current;
                    break;
                case "--posix" or "--restricted" or "-" or "--":
                    return null;
                case "-o" or "+o" or "-O" or "+O":
                    // An option that takes a name.
                    if (!each.MoveNext())
                    {
                        return null;
                    }

                    kept.Add(arg);
                    kept.Add(each.Current);
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        kept.Add(arg); // --login, --noediting, ...
                    }
                    else if (arg.Length > 1 && arg[0] is '-' or '+' && !arg.AsSpan(1).ContainsAny("csrO"))
                    {
                        kept.Add(arg); // -i, -l, -il, ...
                    }
                    else
                    {
                        return null; // -c, -s, -r, a script
                    }

                    break;
            }
        }

        return (kept, flags, rcfile);
    }

    /// <summary>
    /// Whether PowerShell's arguments already run something, whether they include -NoExit (before what runs), and
    /// whether what runs is a -Command (the rest of the arguments).
    /// </summary>
    internal static (bool Runs, bool NoExit, bool Command) PowerShellArguments(IEnumerable<string> args)
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
                return (true, noExit, false); // a script or command, positionally (or "-": read stdin)
            }

            var name = arg.TrimStart('-', '/').Split(':')[0].ToLowerInvariant();
            if (Prefix(name, "command"))
            {
                return (true, noExit, true);
            }

            if (Prefix(name, "commandwithargs") || name is "cwa" or "ec" || Prefix(name, "file") || Prefix(name, "encodedcommand"))
            {
                return (true, noExit, false);
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

        return (false, noExit, false);

        static bool Prefix(string name, string full) => name.Length > 0 && full.StartsWith(name, StringComparison.Ordinal);
    }

    /// <summary>A command line split into arguments (double quotes group, and are removed).</summary>
    private static bool MarksItself(string exe) => exe is "fish" or "nu";

    /// <summary>What wsl.exe is told to run (after <c>--</c>, <c>-e</c> or <c>--exec</c>); null for the login shell.</summary>
    private static string? ProgramAfterOptions(List<string> args)
    {
        var at = args.FindIndex(1, a => a is "--" or "-e" or "--exec");
        return at >= 0 && at + 1 < args.Count ? args[at + 1] : null;
    }

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
