using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zinc.Cli;

// What zn and Zinc say to each other over the pipe: one JSON line each way. Shared source: zn.exe compiles
// these files in (it doesn't reference Core), so they use nothing but the base library.

/// <summary>A command for the running Zinc.</summary>
public sealed class CliRequest
{
    /// <summary>The protocol's version: a newer Zinc keeps answering an older zn.</summary>
    public int V { get; set; } = CliProtocol.Version;

    /// <summary>"version", "list", "action", or a thing and its verb: "pane split", "terminal new".</summary>
    public string Cmd { get; set; } = "";

    /// <summary>The terminal zn runs in (ZINC_TERMINAL_ID); null outside Zinc.</summary>
    public int? Caller { get; set; }

    // Targets given with --terminal, --pane, --workspace, --window.
    public int? Terminal { get; set; }

    public int? Pane { get; set; }

    public int? Workspace { get; set; }

    public int? Window { get; set; }

    /// <summary>pane split's side: right, left, down, up.</summary>
    public string? Direction { get; set; }

    /// <summary>A profile id (wsl:Fedora) or name.</summary>
    public string? Profile { get; set; }

    /// <summary>The folder to start in, as a full Windows path (zn resolves a relative one).</summary>
    public string? Cwd { get; set; }

    /// <summary>workspace new's name.</summary>
    public string? Name { get; set; }

    /// <summary>pane split's share for the new pane: "30%" or "0.3".</summary>
    public string? Size { get; set; }

    /// <summary>action's text ("zoom-in", "new-tab wsl:Fedora").</summary>
    public string? Action { get; set; }

    /// <summary>The program to run instead of the profile's shell (what follows <c>--</c>), as one command line.</summary>
    public string? CommandLine { get; set; }

    /// <summary>close: without asking, even with commands running.</summary>
    public bool Force { get; set; }

    /// <summary>terminal send's text, escapes already turned into characters.</summary>
    public string? Text { get; set; }

    /// <summary>terminal send: as a paste (bracketed when the program asked), not as typed.</summary>
    public bool Paste { get; set; }

    /// <summary>terminal read: the last this many lines, scrollback included; null: the screen.</summary>
    public int? Lines { get; set; }

    /// <summary>terminal read: with colors and styles (VT sequences).</summary>
    public bool Ansi { get; set; }

    /// <summary>terminal title's text ("" gives the title back to the program).</summary>
    public string? Title { get; set; }

    /// <summary>workspace color: a color's name, a #RRGGBB, or "none".</summary>
    public string? Color { get; set; }

    /// <summary>workspace icon: an icon's name, or "none".</summary>
    public string? Icon { get; set; }

    /// <summary>terminal move: the pane to move into.</summary>
    public int? ToPane { get; set; }

    /// <summary>workspace move: a window's id, or "new".</summary>
    public string? ToWindow { get; set; }
}

/// <summary>The answer.</summary>
public sealed class CliReply
{
    public bool Ok { get; set; }

    /// <summary>Why not, in one sentence for people.</summary>
    public string? Error { get; set; }

    /// <summary>What kind of failure (<see cref="CliCodes"/>), for the exit code.</summary>
    public string? Code { get; set; }

    /// <summary>The id of what a command made (a new terminal's).</summary>
    public int? Id { get; set; }

    /// <summary>list's answer.</summary>
    public CliTree? Tree { get; set; }

    /// <summary>terminal read's answer.</summary>
    public string? Text { get; set; }

    /// <summary>The running Zinc's version.</summary>
    public string? Version { get; set; }

    /// <summary>
    /// What a command acted on when nothing said (run outside Zinc, no target flags): "pane 1 in window 1".
    /// zn prints it to stderr, so the user knows.
    /// </summary>
    public string? Target { get; set; }

    public static CliReply Done(int? id = null) => new() { Ok = true, Id = id };

    public static CliReply Fail(string code, string error) => new() { Ok = false, Code = code, Error = error };
}

/// <summary>Kinds of failure, each with its exit code (see <see cref="CliProtocol.ExitCodeFor"/>).</summary>
public static class CliCodes
{
    /// <summary>Zinc refused or failed: no such terminal, the action failed, cancelled at the question.</summary>
    public const string Failed = "failed";

    /// <summary>Wrong usage, found by Zinc (an unknown profile or action).</summary>
    public const string Usage = "usage";

    /// <summary>Remote control is off.</summary>
    public const string Off = "off";
}

/// <summary>Everything open, as list prints it: windows hold workspaces, workspaces panes, panes terminals.</summary>
public sealed class CliTree
{
    public List<CliWindow> Windows { get; set; } = [];
}

public sealed class CliWindow
{
    public int Id { get; set; }

    /// <summary>The window used last (where commands from outside Zinc go).</summary>
    public bool Focused { get; set; }

    /// <summary>The quick terminal's window.</summary>
    public bool Quick { get; set; }

    public List<CliWorkspace> Workspaces { get; set; } = [];
}

public sealed class CliWorkspace
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Shown in the window used last.</summary>
    public bool Focused { get; set; }

    /// <summary>The one its window shows.</summary>
    public bool Active { get; set; }

    public List<CliPane> Panes { get; set; } = [];
}

public sealed class CliPane
{
    public int Id { get; set; }

    /// <summary>The focused pane of the focused workspace.</summary>
    public bool Focused { get; set; }

    /// <summary>The focused pane of its workspace.</summary>
    public bool Active { get; set; }

    public List<CliTerminal> Terminals { get; set; } = [];
}

public sealed class CliTerminal
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    /// <summary>The folder its shell last reported; null if it never did.</summary>
    public string? Folder { get; set; }

    /// <summary>Its profile's id.</summary>
    public string Profile { get; set; } = "";

    /// <summary>The terminal keys go to (in the window used last).</summary>
    public bool Focused { get; set; }

    /// <summary>The tab its pane shows.</summary>
    public bool Active { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false)]
[JsonSerializable(typeof(CliRequest))]
[JsonSerializable(typeof(CliReply))]
public sealed partial class CliJson : JsonSerializerContext;

/// <summary>The same types written for people to read (--json output).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true)]
[JsonSerializable(typeof(CliTree))]
[JsonSerializable(typeof(CliReply))]
public sealed partial class CliJsonIndented : JsonSerializerContext;

public static class CliProtocol
{
    public const int Version = 1;

    /// <summary>Longest request or reply line either side reads (a screen's text fits easily).</summary>
    public const int MaxLine = 16 * 1024 * 1024;

    public static string Write(CliRequest request) => JsonSerializer.Serialize(request, CliJson.Default.CliRequest);

    public static string Write(CliReply reply) => JsonSerializer.Serialize(reply, CliJson.Default.CliReply);

    /// <summary>A request; null if the line isn't one.</summary>
    public static CliRequest? ReadRequest(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, CliJson.Default.CliRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static CliReply? ReadReply(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, CliJson.Default.CliReply);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>zn's exit code for a reply: 0 done, 1 refused or failed, 2 wrong usage, 4 remote control off (3 = no Zinc, zn's own).</summary>
    public static int ExitCodeFor(CliReply reply) => reply.Ok ? 0 : reply.Code switch
    {
        CliCodes.Usage => CliExit.Usage,
        CliCodes.Off => CliExit.Off,
        _ => CliExit.Failed,
    };
}

/// <summary>zn's exit codes.</summary>
public static class CliExit
{
    public const int Done = 0, Failed = 1, Usage = 2, NotRunning = 3, Off = 4;
}
