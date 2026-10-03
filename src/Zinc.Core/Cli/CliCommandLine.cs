using System.Text;

namespace Zinc.Cli;

/// <summary>What zn's command line asks for: a request to send, help to print, or a usage error.</summary>
public sealed record CliParse(CliRequest? Request, bool Json = false, string? Help = null, string? Error = null);

/// <summary>
/// zn's command line: <c>zn &lt;thing&gt; &lt;verb&gt; [options] [-- command]</c>, plus <c>zn list</c>, <c>zn action</c> and
/// <c>zn version</c>. Parsed by hand: a few things with a few verbs each.
/// </summary>
public static class CliCommandLine
{
    private const string Targets = "terminal pane workspace window";

    /// <summary>A command: its thing ("" for the top-level ones), verb, what it does, its arguments and its options.</summary>
    private sealed record Verb(string Thing, string Name, string Summary, string Usage, string Options, string? Positional = null);

    // Options by name: what each takes and says in help.
    private static readonly Dictionary<string, string> OptionHelp = new()
    {
        ["terminal"] = "--terminal N     acts on terminal N (ids: zn list)",
        ["pane"] = "--pane N         acts on pane N (its shown terminal)",
        ["workspace"] = "--workspace N    acts on workspace N (its focused pane and terminal)",
        ["window"] = "--window N       acts on window N (its shown workspace)",
        ["profile"] = "--profile P      a profile, by id (wsl:Fedora) or name",
        ["cwd"] = "--cwd DIR        the folder to start in",
        ["name"] = "--name NAME      the workspace's name",
        ["size"] = "--size 30%       the new pane's share of the space",
        ["force"] = "--force          closes without asking, even with commands running",
        ["json"] = "--json           prints JSON for scripts",
        ["command"] = "-- COMMAND ...   runs this program instead of the profile's shell",
    };

    private static readonly Verb[] Verbs =
    [
        new("", "list", "Everything open: windows, workspaces, panes and terminals", "zn list", "json"),
        new("", "action", "Runs any action by its name, as a key would", "zn action \"NAME [ARGS]\"", Targets, "action"),
        new("", "version", "zn's version and the running Zinc's", "zn version", ""),
        new("window", "list", "Windows, with their workspaces", "zn window list", "json"),
        new("window", "new", "Opens a window", "zn window new", "profile cwd command"),
        new("window", "focus", "Brings a window to the front", "zn window focus", Targets),
        new("window", "close", "Closes a window (asks first if commands are running)", "zn window close", Targets + " force"),
        new("workspace", "list", "Workspaces, with their panes", "zn workspace list", "json window"),
        new("workspace", "new", "Opens a workspace in a window", "zn workspace new", Targets + " name profile cwd command"),
        new("workspace", "focus", "Shows a workspace", "zn workspace focus", Targets),
        new("workspace", "close", "Closes a workspace (asks first if commands are running)", "zn workspace close", Targets + " force"),
        new("pane", "list", "Panes, with their terminals", "zn pane list", "json window workspace"),
        new("pane", "split", "Opens a pane beside another, with one terminal", "zn pane split right|left|down|up",
            Targets + " profile cwd size command", "direction"),
        new("pane", "focus", "Focuses a pane (its shown terminal)", "zn pane focus", Targets),
        new("pane", "close", "Closes a pane and all its terminals (asks first if commands are running)", "zn pane close", Targets + " force"),
        new("terminal", "list", "Terminals: ids, titles, folders, profiles", "zn terminal list", "json window workspace pane"),
        new("terminal", "new", "Opens a terminal as a new tab in a pane", "zn terminal new", Targets + " profile cwd command"),
        new("terminal", "focus", "Shows and focuses a terminal", "zn terminal focus", Targets),
        new("terminal", "close", "Closes a terminal (asks first if a command is running)", "zn terminal close", Targets + " force"),
    ];

    private static readonly string[] Things = ["window", "workspace", "pane", "terminal"];

    public static readonly string[] Directions = ["right", "left", "down", "up"];

    /// <summary>Reads zn's arguments.</summary>
    public static CliParse Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is "help" or "--help" or "-h" or "/?")
        {
            return new CliParse(null, Help: args.Count > 1 && Things.Contains(args[1]) ? ThingHelp(args[1]) : MainHelp());
        }

        var thing = Things.Contains(args[0]) ? args[0] : "";
        var at = thing.Length > 0 ? 1 : 0;
        if (thing.Length > 0 && (at >= args.Count || args[at] is "--help" or "-h" or "help"))
        {
            return new CliParse(null, Help: ThingHelp(thing));
        }

        var verbName = args[at];
        if (Verbs.FirstOrDefault(v => v.Thing == thing && v.Name == verbName) is not { } verb)
        {
            return thing.Length > 0
                ? Usage($"zn {thing} has no \"{verbName}\". zn {thing} --help lists what it does.")
                : Usage($"Unknown command \"{verbName}\". zn --help lists them.");
        }

        var request = new CliRequest { Cmd = thing.Length > 0 ? $"{thing} {verb.Name}" : verb.Name };
        var allowed = verb.Options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var json = false;
        string? positional = null;
        for (var i = at + 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
            {
                return new CliParse(null, Help: VerbHelp(verb));
            }

            if (arg == "--")
            {
                if (!allowed.Contains("command"))
                {
                    return Usage($"{Name(verb)} doesn't run a command.");
                }

                if (i + 1 >= args.Count)
                {
                    return Usage("Nothing after --: give the program to run.");
                }

                request.CommandLine = Join(args.Skip(i + 1));
                break;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                string? value = null;
                if (name.IndexOf('=') is var eq and > 0)
                {
                    (name, value) = (name[..eq], name[(eq + 1)..]);
                }

                if (!allowed.Contains(name) || name == "command")
                {
                    return Usage(OptionHelp.ContainsKey(name) && name != "command"
                        ? $"{Name(verb)} doesn't take --{name}."
                        : $"Unknown option {arg}. {Name(verb)} --help lists its options.");
                }

                if (name is "json" or "force")
                {
                    if (value != null)
                    {
                        return Usage($"--{name} takes no value.");
                    }

                    if (name == "json")
                    {
                        json = true;
                    }
                    else
                    {
                        request.Force = true;
                    }

                    continue;
                }

                if (value == null)
                {
                    if (i + 1 >= args.Count)
                    {
                        return Usage($"--{name} needs a value.");
                    }

                    value = args[++i];
                }

                if (name is "terminal" or "pane" or "workspace" or "window")
                {
                    if (!int.TryParse(value, out var id) || id <= 0)
                    {
                        return Usage($"--{name} takes a number from zn list, not \"{value}\".");
                    }

                    _ = name switch
                    {
                        "terminal" => request.Terminal = id,
                        "pane" => request.Pane = id,
                        "workspace" => request.Workspace = id,
                        _ => request.Window = id,
                    };
                    continue;
                }

                if (value.Length == 0)
                {
                    return Usage($"--{name} needs a value.");
                }

                switch (name)
                {
                    case "profile":
                        request.Profile = value;
                        break;
                    case "cwd":
                        request.Cwd = value;
                        break;
                    case "name":
                        request.Name = value;
                        break;
                    case "size":
                        if (ParseSize(value) == null)
                        {
                            return Usage($"--size takes a share like 30% or 0.3, not \"{value}\".");
                        }

                        request.Size = value;
                        break;
                }

                continue;
            }

            if (verb.Positional == null || positional != null)
            {
                return Usage($"Unexpected \"{arg}\". {Name(verb)} --help shows how to use it.");
            }

            positional = arg;
        }

        switch (verb.Positional)
        {
            case "direction" when positional == null || !Directions.Contains(positional):
                return Usage(positional == null
                    ? "Which side? zn pane split right, left, down or up."
                    : $"\"{positional}\" isn't a side: right, left, down or up.");
            case "direction":
                request.Direction = positional;
                break;
            case "action" when string.IsNullOrWhiteSpace(positional):
                return Usage("Which action? For example: zn action \"zoom-in\".");
            case "action":
                request.Action = positional;
                break;
        }

        return new CliParse(request, json);
    }

    /// <summary>A pane share: "30%" or "0.3", between 10% and 90%; null if it isn't one.</summary>
    public static double? ParseSize(string text)
    {
        var percent = text.EndsWith('%');
        if (!double.TryParse(percent ? text[..^1] : text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var share = percent || value > 1 ? value / 100 : value;
        return share is >= 0.1 and <= 0.9 ? share : null;
    }

    /// <summary>Arguments as one Windows command line, quoted the way programs read them back (CommandLineToArgvW).</summary>
    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    private static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            // Backslashes before a quote are doubled, and the quote escaped; elsewhere they're literal.
            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }

        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static CliParse Usage(string error) => new(null, Error: error);

    private static string Name(Verb verb) => verb.Thing.Length > 0 ? $"zn {verb.Thing} {verb.Name}" : $"zn {verb.Name}";

    public static string MainHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("zn: controls the running Zinc from a script or a shell.");
        sb.AppendLine();
        sb.AppendLine("Usage: zn <thing> <verb> [options] [-- command]");
        sb.AppendLine();
        foreach (var thing in Things)
        {
            sb.AppendLine($"  zn {thing,-10} {string.Join(" | ", Verbs.Where(v => v.Thing == thing).Select(v => v.Name))}");
        }

        sb.AppendLine();
        foreach (var verb in Verbs.Where(v => v.Thing.Length == 0))
        {
            sb.AppendLine($"  {verb.Usage,-26} {verb.Summary}");
        }

        sb.AppendLine();
        sb.AppendLine("Inside a Zinc terminal, commands act on that terminal (its pane, workspace and window); outside,");
        sb.AppendLine("on the focused one of the window used last. --terminal, --pane, --workspace and --window aim");
        sb.AppendLine("elsewhere. zn <thing> --help shows a thing's commands.");
        return sb.ToString();
    }

    public static string ThingHelp(string thing)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"zn {thing}:");
        sb.AppendLine();
        foreach (var verb in Verbs.Where(v => v.Thing == thing))
        {
            sb.AppendLine($"  {verb.Usage,-36} {verb.Summary}");
        }

        sb.AppendLine();
        sb.AppendLine($"zn {thing} <verb> --help shows a command's options.");
        return sb.ToString();
    }

    private static string VerbHelp(Verb verb)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{verb.Usage}: {verb.Summary}.");
        var options = verb.Options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (options.Length > 0)
        {
            sb.AppendLine();
            foreach (var option in options)
            {
                sb.AppendLine("  " + OptionHelp[option]);
            }
        }

        return sb.ToString();
    }
}
