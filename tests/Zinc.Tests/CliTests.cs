using Zinc.Cli;

namespace Zinc.Tests;

public class CliTests
{
    private static CliRequest Request(params string[] args)
    {
        var parsed = CliCommandLine.Parse(args);
        Assert.Null(parsed.Error);
        return Assert.IsType<CliRequest>(parsed.Request);
    }

    private static string Error(params string[] args)
    {
        var parsed = CliCommandLine.Parse(args);
        Assert.Null(parsed.Request);
        return Assert.IsType<string>(parsed.Error);
    }

    [Fact]
    public void ThingsAndVerbs()
    {
        Assert.Equal("list", Request("list").Cmd);
        Assert.Equal("version", Request("version").Cmd);
        Assert.Equal("window new", Request("window", "new").Cmd);
        Assert.Equal("terminal close", Request("terminal", "close").Cmd);
        Assert.Contains("has no \"split\"", Error("terminal", "split"));
        Assert.Contains("Unknown command", Error("splat"));
    }

    [Fact]
    public void HelpAtEveryLevel()
    {
        Assert.Contains("zn pane", CliCommandLine.Parse([]).Help);
        Assert.Contains("zn pane", CliCommandLine.Parse(["--help"]).Help);
        Assert.Contains("split right|left|down|up", CliCommandLine.Parse(["pane"]).Help);
        Assert.Contains("split right|left|down|up", CliCommandLine.Parse(["help", "pane"]).Help);
        Assert.Contains("--size", CliCommandLine.Parse(["pane", "split", "--help"]).Help);
    }

    [Fact]
    public void Targets()
    {
        var r = Request("terminal", "new", "--pane", "4", "--profile", "wsl:Fedora");
        Assert.Equal(4, r.Pane);
        Assert.Equal("wsl:Fedora", r.Profile);
        Assert.Equal(2, Request("workspace", "focus", "--workspace=2").Workspace);
        Assert.Contains("takes a number", Error("pane", "focus", "--pane", "two"));
        Assert.Contains("takes a number", Error("pane", "focus", "--pane", "0"));
        Assert.Contains("needs a value", Error("pane", "focus", "--pane"));
    }

    [Fact]
    public void OptionsBelongToTheirCommands()
    {
        Assert.Contains("doesn't take --size", Error("terminal", "new", "--size", "30%"));
        Assert.Contains("Unknown option --frobnicate", Error("list", "--frobnicate"));
        Assert.Contains("doesn't run a command", Error("pane", "focus", "--", "htop"));
        Assert.True(Request("pane", "close", "--force").Force);
        Assert.True(CliCommandLine.Parse(["list", "--json"]).Json);
        Assert.Contains("takes no value", Error("list", "--json=yes"));
    }

    [Fact]
    public void SplitTakesASide()
    {
        var r = Request("pane", "split", "down", "--size", "30%", "--", "htop", "-d", "5");
        Assert.Equal("down", r.Direction);
        Assert.Equal("30%", r.Size);
        Assert.Equal("htop -d 5", r.CommandLine);
        Assert.Contains("Which side", Error("pane", "split"));
        Assert.Contains("isn't a side", Error("pane", "split", "sideways"));
        Assert.Contains("Unexpected", Error("pane", "split", "left", "right"));
        Assert.Contains("--size takes", Error("pane", "split", "left", "--size", "95%"));
        Assert.Contains("Nothing after --", Error("pane", "split", "left", "--"));
    }

    [Fact]
    public void ActionTakesItsText()
    {
        Assert.Equal("new-tab wsl:Fedora", Request("action", "new-tab wsl:Fedora").Action);
        Assert.Contains("Which action", Error("action"));
    }

    [Theory]
    [InlineData("30%", 0.3)]
    [InlineData("0.3", 0.3)]
    [InlineData("30", 0.3)]
    [InlineData("50%", 0.5)]
    [InlineData("5%", null)]
    [InlineData("0.95", null)]
    [InlineData("half", null)]
    public void Sizes(string text, double? share)
    {
        var parsed = CliCommandLine.ParseSize(text);
        if (share == null)
        {
            Assert.Null(parsed);
        }
        else
        {
            Assert.Equal(share.Value, parsed!.Value, 6);
        }
    }

    [Fact]
    public void CommandLinesQuoteLikeWindows()
    {
        Assert.Equal("git log", CliCommandLine.Join(["git", "log"]));
        Assert.Equal("echo \"a b\" \"\"", CliCommandLine.Join(["echo", "a b", ""]));
        Assert.Equal("x \"say \\\"hi\\\"\"", CliCommandLine.Join(["x", "say \"hi\""]));
        // Backslashes are literal except before a quote, including the closing one.
        Assert.Equal(@"dir ""C:\Program Files\\""", CliCommandLine.Join(["dir", @"C:\Program Files\"]));
        Assert.Equal(@"dir C:\x\", CliCommandLine.Join(["dir", @"C:\x\"]));
    }

    [Fact]
    public void RequestsAndRepliesRoundTrip()
    {
        var line = CliProtocol.Write(new CliRequest { Cmd = "pane split", Caller = 3, Direction = "right", CommandLine = "htop" });
        Assert.DoesNotContain('\n', line);
        Assert.Contains("\"v\":1", line);
        Assert.DoesNotContain("\"pane\"", line); // unset fields aren't sent
        var back = CliProtocol.ReadRequest(line)!;
        Assert.Equal(("pane split", 3, "right", "htop"), (back.Cmd, back.Caller, back.Direction, back.CommandLine));
        Assert.Null(CliProtocol.ReadRequest("not json"));

        var reply = CliProtocol.ReadReply(CliProtocol.Write(CliReply.Fail(CliCodes.Off, "Remote control is off.")))!;
        Assert.Equal(CliExit.Off, CliProtocol.ExitCodeFor(reply));
        Assert.Equal(CliExit.Usage, CliProtocol.ExitCodeFor(CliReply.Fail(CliCodes.Usage, "")));
        Assert.Equal(CliExit.Failed, CliProtocol.ExitCodeFor(CliReply.Fail(CliCodes.Failed, "")));
        Assert.Equal(CliExit.Done, CliProtocol.ExitCodeFor(CliReply.Done(8)));
    }

    [Fact]
    public void PipeNames()
    {
        string? Env(string name, Dictionary<string, string> vars) => vars.GetValueOrDefault(name);
        var session = Path.Combine(Path.GetTempPath(), "zn-test-session.json");
        Assert.Equal("zinc-p", CliPipe.Find(n => Env(n, new() { ["ZINC_PIPE"] = "zinc-p" })));
        Assert.Equal(CliPipe.ForSession(session), CliPipe.Find(n => Env(n, new() { ["ZINC_SESSION"] = session })));
        Assert.Equal(CliPipe.ForSession(session.ToUpperInvariant()), CliPipe.ForSession(session));
        Assert.Null(CliPipe.Find(n => Env(n, new() { ["ZINC_CONFIG"] = "x.toml" }))); // a test Zinc without a session
        Assert.StartsWith(Zinc.Session.SessionStore.InstanceKey(session), CliPipe.ForSession(session));
        Assert.NotEqual(CliPipe.ForProcess(1), CliPipe.ForProcess(2));
    }

    [Fact]
    public void TerminalsLearnTheirIdAndPipe()
    {
        var env = new Dictionary<string, string?>();
        CliEnvironment.Add(env, 7, "Zinc.x.cli", _ => null);
        Assert.Equal("7", env["ZINC_TERMINAL_ID"]);
        Assert.Equal("Zinc.x.cli", env["ZINC_PIPE"]);
        Assert.Equal("ZINC_TERMINAL_ID:ZINC_PIPE", env["WSLENV"]);

        // Shell integration's WSLENV (already the inherited one plus its own) is extended, not replaced.
        env = new Dictionary<string, string?> { ["WSLENV"] = "USERPROFILE/p:ZDOTDIR/p" };
        CliEnvironment.Add(env, 7, "p", _ => "IGNORED");
        Assert.Equal("USERPROFILE/p:ZDOTDIR/p:ZINC_TERMINAL_ID:ZINC_PIPE", env["WSLENV"]);

        env = [];
        CliEnvironment.Add(env, 7, "p", name => name == "WSLENV" ? "ZINC_PIPE/u" : null);
        Assert.Equal("ZINC_PIPE/u:ZINC_TERMINAL_ID", env["WSLENV"]);
    }

    [Fact]
    public void ProgramsRunUnderTheirProfile()
    {
        Assert.Equal("htop -d 5", CliLaunch.WithProgram("pwsh.exe -NoLogo", "htop -d 5"));
        Assert.Equal("wsl.exe -d Fedora -- htop", CliLaunch.WithProgram("wsl.exe -d Fedora", "htop"));
        Assert.Equal("wsl.exe --cd \"/home/z\" -d Fedora -- htop", CliLaunch.WithProgram("wsl.exe --cd \"/home/z\" -d Fedora", "htop"));
        Assert.Equal("wsl.exe -d Fedora -- htop", CliLaunch.WithProgram("wsl.exe -d Fedora -- zsh -l", "htop"));
        Assert.Equal("wsl.exe -d Fedora -- htop", CliLaunch.WithProgram("wsl.exe -d Fedora -e zsh", "htop"));
    }

    private static CliTree Tree() => new()
    {
        Windows =
        [
            new CliWindow
            {
                Id = 1, Focused = true,
                Workspaces =
                [
                    new CliWorkspace
                    {
                        Id = 1, Name = "zinc", Focused = true, Active = true,
                        Panes =
                        [
                            new CliPane
                            {
                                Id = 1, Focused = true, Active = true,
                                Terminals =
                                [
                                    new CliTerminal { Id = 3, Title = "pwsh", Folder = @"C:\src", Profile = "pwsh", Focused = true, Active = true },
                                    new CliTerminal { Id = 9, Title = "zsh", Folder = "~/src", Profile = "wsl:Fedora" },
                                ],
                            },
                            new CliPane { Id = 2, Terminals = [new CliTerminal { Id = 7, Title = "htop", Profile = "pwsh", Active = true }] },
                        ],
                    },
                ],
            },
            new CliWindow
            {
                Id = 2,
                Workspaces =
                [
                    new CliWorkspace
                    {
                        Id = 2, Name = "notes", Active = true,
                        Panes = [new CliPane { Id = 4, Active = true, Terminals = [new CliTerminal { Id = 5, Title = "nvim", Profile = "pwsh", Active = true }] }],
                    },
                ],
            },
        ],
    };

    [Fact]
    public void ListPrintsColumns()
    {
        var lines = CliText.List(Tree(), "").Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        Assert.Equal("WINDOW  WORKSPACE   PANE   TERMINAL  TITLE  FOLDER", lines[0]);
        Assert.Equal(@"1 (*)   1 (*) zinc  1 (*)  3 (*)     pwsh   C:\src", lines[1]);
        Assert.Equal("2       2 notes     4      5         nvim", lines[4]); // nothing trails an empty last column
        Assert.EndsWith("PROFILE", CliText.List(Tree(), "terminal").Split(Environment.NewLine)[0]);
        Assert.Contains("1 (*)   1           3          zinc", CliText.List(Tree(), "window"));
        Assert.Contains("1       1 zinc     2      1          htop", CliText.List(Tree(), "pane"));
    }

    [Fact]
    public void ListFlagsNarrowIt()
    {
        var onlyPane = CliText.Filter(Tree(), new CliRequest { Pane = 4 });
        Assert.Equal(5, Assert.Single(Assert.Single(Assert.Single(onlyPane.Windows).Workspaces).Panes).Terminals.Single().Id);
        Assert.Equal(2, CliText.Filter(Tree(), new CliRequest { Window = 2 }).Windows.Single().Id);
        Assert.Empty(CliText.Filter(Tree(), new CliRequest { Workspace = 9 }).Windows);
        Assert.Equal(2, CliText.Filter(Tree(), new CliRequest()).Windows.Count);
    }

    [Fact]
    public void SendTakesTextWithEscapes()
    {
        Assert.Equal("ls\r", Request("terminal", "send", "ls\\r").Text);
        Assert.True(Request("terminal", "send", "--paste", "a\\nb").Paste);
        Assert.Equal("--help", Request("terminal", "send", "--", "--help").Text); // text that looks like an option
        Assert.Equal("-", Request("terminal", "send", "--", "-").Text); // a dash itself, not stdin
        var stdin = Request("terminal", "send", "-");
        Assert.Null(stdin.Text);
        Assert.True(CliCommandLine.SendsStdin(stdin));
        Assert.False(CliCommandLine.SendsStdin(Request("terminal", "send", "x")));
        Assert.Contains("What to send", Error("terminal", "send"));
        Assert.Contains("What to send", Error("terminal", "send", ""));
    }

    [Theory]
    [InlineData("ls\\r", "ls\r")]
    [InlineData("a\\tb\\nc", "a\tb\nc")]
    [InlineData("\\e[A", "\x1b[A")]
    [InlineData("\\x1b:q", "\x1b:q")]
    [InlineData("\\u00e9t\\u00e9", "été")]
    [InlineData("C:\\\\x", "C:\\x")]
    [InlineData("C:\\Users", "C:\\Users")]   // \U isn't an escape: it stays
    [InlineData("\\xZZ \\", "\\xZZ \\")]     // nor a broken \x, nor a last backslash
    public void Escapes(string written, string sent)
    {
        Assert.Equal(sent, CliCommandLine.Unescape(written));
    }

    [Fact]
    public void ReadTitleAndTheWorkspaceLook()
    {
        var read = Request("terminal", "read", "--lines", "20", "--ansi");
        Assert.Equal((20, true), (read.Lines, read.Ansi));
        Assert.Contains("number of lines", Error("terminal", "read", "--lines", "all"));
        Assert.Equal("", Request("terminal", "title", "").Title); // "" gives it back
        Assert.Contains("Which title", Error("terminal", "title"));
        Assert.Equal("api", Request("workspace", "rename", "api").Name);
        Assert.Equal("green", Request("workspace", "color", "green").Color);
        Assert.Equal("bug", Request("workspace", "icon", "bug").Icon);
        Assert.Contains("Which color", Error("workspace", "color"));
    }

    [Fact]
    public void MovesSayWhere()
    {
        Assert.Equal(4, Request("terminal", "move", "--to-pane", "4").ToPane);
        Assert.Equal("new", Request("workspace", "move", "--to-window", "new").ToWindow);
        Assert.Equal("2", Request("workspace", "move", "--to-window=2").ToWindow);
        Assert.Contains("--to-window takes", Error("workspace", "move", "--to-window", "next"));
        Assert.Contains("Where to? --to-pane", Error("terminal", "move"));
        Assert.Contains("Where to? --to-window", Error("workspace", "move"));
        Assert.Contains("doesn't take --to-pane", Error("workspace", "move", "--to-pane", "3"));
    }

    [Fact]
    public void ZnGoesAtTheEndOfPath()
    {
        var env = new Dictionary<string, string?>();
        CliEnvironment.Add(env, 1, "p", name => name == "PATH" ? @"C:\Windows;C:\Tools" : null, @"C:\Zinc\bin");
        Assert.Equal(@"C:\Windows;C:\Tools;C:\Zinc\bin", env["PATH"]);
        Assert.Equal(@"C:\Zinc\bin\zn.exe", env["ZINC_ZN"]);
        Assert.Equal("ZINC_TERMINAL_ID:ZINC_PIPE:ZINC_ZN/p", env["WSLENV"]); // WSL gets it as /mnt/c/...

        env = [];
        CliEnvironment.Add(env, 1, "p", name => name == "PATH" ? @"C:\Zinc\bin\;C:\Tools" : null, @"C:\Zinc\bin");
        Assert.False(env.ContainsKey("PATH")); // there already

        env = [];
        CliEnvironment.Add(env, 1, "p", _ => null);
        Assert.False(env.ContainsKey("PATH")); // no zn here: PATH untouched
    }

    [Fact]
    public void ReadGivesTheScreenOrTheLastLines()
    {
        using var session = new Zinc.Terminal.TerminalSession(20, 5, Zinc.Terminal.TerminalTheme.Default);
        session.WriteToScreen("a\r\nb\r\nc");
        Assert.Equal("a\nb\nc", session.ReadText());
        for (var i = 0; i < 10; i++)
        {
            session.WriteToScreen($"\r\nline{i}");
        }

        Assert.Equal("line5\nline6\nline7\nline8\nline9", session.ReadText()); // the screen, not the scrollback
        Assert.Equal("line8\nline9", session.ReadText(2));
        Assert.Equal(13, session.ReadText(100).Split('\n').Length);

        session.WriteToScreen("\x1b[2J\x1b[H\x1b[31mred\x1b[0m");
        Assert.Equal("red", session.ReadText()); // blank rows below don't count
        var styled = session.ReadText(styled: true);
        Assert.Contains("red", styled);
        Assert.Contains("\x1b[", styled);
    }
}
