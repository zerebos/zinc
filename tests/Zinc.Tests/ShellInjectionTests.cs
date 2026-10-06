using System.Diagnostics;
using Zinc.Terminal;

namespace Zinc.Tests;

public class ShellInjectionTests
{
    private const string Scripts = @"C:\Zinc\ShellIntegration";

    private static ShellLaunch Prepare(string command, Dictionary<string, string>? env = null) =>
        ShellIntegration.Prepare(command, Scripts, name => env?.GetValueOrDefault(name));

    // ------------------------------------------------------------ what a launch becomes

    [Theory]
    [InlineData("pwsh.exe")]
    [InlineData("pwsh.exe -NoLogo")]
    [InlineData(@"""C:\Program Files\PowerShell\7\pwsh.exe"" -NoLogo -WorkingDirectory C:\src")]
    [InlineData("powershell.exe -ExecutionPolicy Bypass -nologo")]
    public void PowerShellRunsTheScriptAfterItsProfile(string command)
    {
        var launch = Prepare(command);
        Assert.StartsWith(command + " -NoExit -Command ", launch.CommandLine);
        Assert.Contains(@"ReadAllText('C:\Zinc\ShellIntegration\pwsh\zinc.ps1')", launch.CommandLine);
        Assert.Empty(launch.Environment);
    }

    [Theory]
    [InlineData("pwsh.exe -Command Get-Date")]
    [InlineData("pwsh.exe -c ls")]
    [InlineData("pwsh -File build.ps1")]
    [InlineData("pwsh build.ps1")]
    [InlineData("pwsh.exe -NoExit -File profile.ps1")] // -File: what follows are the script's arguments
    [InlineData("powershell -NoExit -EncodedCommand ZQBjAGgAbwA=")]
    [InlineData("powershell -EncodedCommand ZQBjAGgAbwA=")]
    [InlineData("powershell -ec ZQBjAGgAbwA=")]
    public void PowerShellToldToRunSomethingIsLeftAlone(string command)
    {
        var launch = Prepare(command);
        Assert.Equal(command, launch.CommandLine);
        Assert.Empty(launch.Environment);
    }

    [Theory]
    [InlineData("powershell.exe -NoExit -Command \"&{Import-Module x; Enter-VsDevShell abc -SkipAutomaticLocation}\"")] // Developer PowerShell for VS
    [InlineData(@"pwsh -noe -c Set-Location C:\src")]
    public void ACommandThatStaysOpenGetsTheScriptAfterIt(string command)
    {
        var launch = Prepare(command);
        Assert.Equal(command + @" ""; . ([scriptblock]::Create([IO.File]::ReadAllText('C:\Zinc\ShellIntegration\pwsh\zinc.ps1')))""", launch.CommandLine);
        Assert.Empty(launch.Environment);
    }

    [Fact]
    public void NoExitIsNotRepeated()
    {
        var launch = Prepare("pwsh.exe -noe");
        Assert.StartsWith("pwsh.exe -noe -Command ", launch.CommandLine);
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Git\bin\bash.exe"" -c ""ls; bash""")] // runs something: not interactive
    [InlineData(@"bash.exe build.sh")]
    [InlineData(@"C:\Windows\system32\wsl.exe --distribution-id {1234} --cd ~")]
    [InlineData(@"C:\msys64\msys2_shell.cmd -defterm -here -no-start -ucrt64")]
    public void OtherShellsGetVariablesAndKeepTheirCommand(string command)
    {
        var launch = Prepare(command);
        Assert.Equal(command, launch.CommandLine);
        Assert.Equal("C:/Zinc/ShellIntegration", launch.Environment["ZINC_SHELL_INTEGRATION_DIR"]);
        Assert.Equal("C:/Zinc/ShellIntegration/zsh", launch.Environment["ZDOTDIR"]);
        Assert.Equal(ShellIntegration.BashBootstrap, launch.Environment["PROMPT_COMMAND"]);
        Assert.Equal("ZINC_SHELL_INTEGRATION_DIR/p:PROMPT_COMMAND:ZDOTDIR/p", launch.Environment["WSLENV"]);
        Assert.False(launch.Environment.ContainsKey("ZINC_ZDOTDIR"));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Git\bin\bash.exe"" --login -i", @"""C:\Program Files\Git\bin\bash.exe"" --posix --login -i", "1 histfile", null)]
    [InlineData("bash.exe", "bash.exe --posix", "1 histfile", null)]
    [InlineData(@"C:\msys64\usr\bin\bash.exe --norc -i", @"C:\msys64\usr\bin\bash.exe --posix -i", "1 --norc histfile", null)]
    [InlineData("bash --rcfile \"C:/my rc\" -o vi -i", "bash --posix -o vi -i", "1 histfile", "C:/my rc")]
    public void BashStartedDirectlyReadsItsStartupThroughOurs(string command, string expected, string flags, string? rcfile)
    {
        var launch = Prepare(command);
        Assert.Equal(expected, launch.CommandLine);
        Assert.Equal("C:/Zinc/ShellIntegration/bash/startup.bash", launch.Environment["ENV"]);
        Assert.Equal(flags, launch.Environment["ZINC_BASH_INJECT"]);
        Assert.Equal(rcfile, launch.Environment.GetValueOrDefault("ZINC_BASH_RCFILE"));
        Assert.False(launch.Environment.ContainsKey("PROMPT_COMMAND"));
    }

    [Fact]
    public void BashKeepsTheUsersEnvAndHistfile()
    {
        var launch = Prepare("bash.exe -i", new() { ["ENV"] = "~/.shrc", ["HISTFILE"] = "~/.hist" });
        Assert.Equal("~/.shrc", launch.Environment["ZINC_BASH_ENV"]);
        Assert.Equal("1", launch.Environment["ZINC_BASH_INJECT"]);
    }

    [Theory]
    [InlineData(@"""C:\Program Files\fish\fish.exe""")]
    [InlineData("nu.exe --login")]
    [InlineData("wsl.exe -d Fedora -- fish")]
    [InlineData("wsl.exe --distribution-id {1234} --cd ~ -e /usr/bin/nu")]
    [InlineData("wsl.exe -d Fedora --exec fish -l")]
    public void FishAndNushellGetNoBashOrZshVariables(string command)
    {
        // They mark their prompts themselves, and would never take the variables back out.
        var launch = Prepare(command);
        Assert.Equal(command, launch.CommandLine);
        Assert.Empty(launch.Environment);
    }

    [Fact]
    public void WslRunningSomethingElseStillGetsThem()
    {
        Assert.NotEmpty(Prepare("wsl.exe -d Fedora -- zsh").Environment);
        Assert.NotEmpty(Prepare("wsl.exe -d Fedora --cd /home/fish").Environment); // a folder named fish isn't the shell
    }

    [Fact]
    public void KeepsTheUsersWslenvAndZdotdir()
    {
        var launch = Prepare("wsl.exe", new() { ["WSLENV"] = "GOPATH/l:ZDOTDIR/p", ["ZDOTDIR"] = @"C:\me\zsh" });
        Assert.Equal("GOPATH/l:ZDOTDIR/p:ZINC_SHELL_INTEGRATION_DIR/p:PROMPT_COMMAND", launch.Environment["WSLENV"]);
        Assert.Equal(@"C:\me\zsh", launch.Environment["ZINC_ZDOTDIR"]);
    }

    [Theory]
    [InlineData("-NoLogo -NoProfile", false, false)]
    [InlineData("-wd C:\\x -NoExit", false, true)]
    [InlineData("-WindowStyle Hidden script.ps1", true, false)]
    [InlineData("-NoLogo -", true, false)]
    [InlineData("-ExecutionPolicy:Bypass -Nologo", false, false)]
    [InlineData("/c dir", true, false)]
    public void ReadsPowerShellsArguments(string args, bool runs, bool noExit)
    {
        var (readRuns, readNoExit, _) = ShellIntegration.PowerShellArguments(ShellIntegration.Split(args));
        Assert.Equal((runs, noExit), (readRuns, readNoExit));
    }

    [Fact]
    public void SplitsLikeWindows() =>
        Assert.Equal(["C:\\Program Files\\x.exe", "-a", "b c", "d"], ShellIntegration.Split("\"C:\\Program Files\\x.exe\" -a \"b c\"  d"));

    // ------------------------------------------------------------ in real shells

    private static string RepoScripts()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var scripts = Path.Combine(dir.FullName, "assets", "shell-integration");
            if (Directory.Exists(scripts))
            {
                return scripts;
            }
        }

        throw new DirectoryNotFoundException("assets/shell-integration");
    }

    /// <summary>A shell started the way the app starts it, with a clean home where that's possible.</summary>
    private static TerminalSession StartShell(string command, Dictionary<string, string?>? extra = null)
    {
        var launch = ShellIntegration.Prepare(command, RepoScripts(), Environment.GetEnvironmentVariable);
        var env = new Dictionary<string, string?>(launch.Environment) { ["NO_COLOR"] = null };
        foreach (var (k, v) in extra ?? [])
        {
            env[k] = v;
        }

        var session = new TerminalSession(100, 30, TerminalTheme.Default);
        session.Resize(100, 30, 8, 16);
        session.Start(launch.CommandLine, Path.GetTempPath(), 100, 30, env);
        return session;
    }

    private static void WaitFor(TerminalSession session, Func<bool> done, string what)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (done())
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail($"{what} never happened. Screen:\n{session.ReadScreenText()}");
    }

    /// <summary>Runs a failing command and a folder change, and checks what the marks told us.</summary>
    private static void ChecksMarks(TerminalSession session, string fail, int code, string cd, string folderEnd)
    {
        WaitFor(session, () => session.Commands().Count >= 1, "the first prompt mark");
        session.SendInput(fail + "\r");
        WaitFor(session, () => session.Commands().Any(c => c.Ran && c.ExitCode != null), "the command finishing");
        var ran = session.Commands().First(c => c.Ran);
        Assert.Equal(fail, ran.Text);
        Assert.Equal(code, ran.ExitCode);

        session.SendInput(cd + "\r");
        WaitFor(session, () => ShellDirectory.Parse(session.ReportedDirectory)?.TrimEnd('\\', '/').EndsWith(folderEnd, StringComparison.OrdinalIgnoreCase) == true,
            $"the folder report ({folderEnd})");
    }

    /// <summary>A folder with a space (and a non-ASCII letter) comes back whole: the report is percent-encoded.</summary>
    private static void ChecksFolderEncoding(TerminalSession session)
    {
        session.SendInput("mkdir -p '/tmp/zinc a é' && cd '/tmp/zinc a é'\r");
        WaitFor(session, () => ShellDirectory.Parse(session.ReportedDirectory)?.EndsWith("/tmp/zinc a é", StringComparison.Ordinal) == true,
            "the folder report with a space");
    }

    [Fact]
    public void PowerShell()
    {
        var pwsh = ShellResolver.FindOnPath(Environment.GetEnvironmentVariable("PATH") ?? "", "pwsh.exe");
        Assert.SkipWhen(pwsh == null, "no pwsh.exe");
        using var session = StartShell("pwsh.exe -NoLogo -NoProfile");
        ChecksMarks(session, "cmd /c exit 3", 3, "cd ..", Path.GetFileName(Path.GetDirectoryName(Path.GetTempPath().TrimEnd('\\')))!);
    }

    [Fact]
    public void WindowsPowerShell()
    {
        // Its default execution policy refuses script files: the script is loaded as text.
        using var session = StartShell("powershell.exe -NoLogo -NoProfile");
        ChecksMarks(session, "cmd /c exit 4", 4, "cd C:\\Windows", "Windows");
    }

    [Fact]
    public void WindowsPowerShellAfterACommandThatStaysOpen()
    {
        // Started as Developer PowerShell for VS is (its quoting included): the command runs first, then the marks.
        using var session = StartShell("powershell.exe -NoLogo -NoProfile -NoExit -Command \"&{Set-Variable -Name zincDev -Value \"\"\"entered\"\"\" -Scope Global}\"");
        ChecksMarks(session, "cmd /c exit 5", 5, "cd C:\\Windows", "Windows");
        session.SendInput("Write-Output \"dev=$zincDev\"\r");
        WaitFor(session, () => session.ReadScreenText().Contains("dev=entered"), "the first command's variable");
    }

    [Fact]
    public void GitBash()
    {
        const string bash = @"C:\Program Files\Git\bin\bash.exe";
        Assert.SkipUnless(File.Exists(bash), "no Git Bash");
        using var session = StartShell($"\"{bash}\" --noprofile --norc -i");
        ChecksMarks(session, "false", 1, "cd /c/Windows", "/c/Windows");
        ChecksFolderEncoding(session);
    }

    [Fact]
    public void BashKeepsAPromptCommandItsStartupSets()
    {
        const string bash = @"C:\Program Files\Git\bin\bash.exe";
        Assert.SkipUnless(File.Exists(bash), "no Git Bash");
        var home = Directory.CreateTempSubdirectory("zinc-bash-").FullName;
        File.WriteAllText(Path.Combine(home, ".bashrc"), "PROMPT_COMMAND=\"${PROMPT_COMMAND:+$PROMPT_COMMAND; }echo -n\"\n");
        using var session = StartShell($"\"{bash}\" --noprofile -i", new() { ["HOME"] = home });
        ChecksMarks(session, "false", 1, "cd /c/Windows", "/c/Windows");
        session.SendInput("echo \"[$PROMPT_COMMAND]\"\r");
        WaitFor(session, () => session.ReadScreenText().Contains("[__zinc_si_status; echo -n; __zinc_si_prompt]"), "PROMPT_COMMAND");
    }

    [Theory]
    [InlineData("--noprofile -i")]
    [InlineData("--login -i")] // Git Bash's own profile: ~/.bash_profile, which sources ~/.bashrc
    public void BashWhoseStartupReplacesPromptCommand(string args)
    {
        const string bash = @"C:\Program Files\Git\bin\bash.exe";
        Assert.SkipUnless(File.Exists(bash), "no Git Bash");
        var home = Directory.CreateTempSubdirectory("zinc-bash-").FullName;
        File.WriteAllText(Path.Combine(home, ".bashrc"), "PROMPT_COMMAND='history -a'\nzinc_rc=read\n");
        File.WriteAllText(Path.Combine(home, ".bash_profile"), "zinc_profile=read\n. ~/.bashrc\n");
        using var session = StartShell($"\"{bash}\" {args}", new() { ["HOME"] = home });
        ChecksMarks(session, "false", 1, "cd /c/Windows", "/c/Windows");

        // Its startup files were read as bash reads them, and its own prompt command still runs (after ours).
        var login = args.Contains("--login");
        // And it's bash's own mode again, with bash's own history file.
        session.SendInput("echo \"[$zinc_profile|$zinc_rc|$(shopt -q login_shell && echo login)|$PROMPT_COMMAND|${HISTFILE##*/}|$(shopt -oq posix && echo posix)]\"\r");
        var expected = $"[{(login ? "read" : "")}|read|{(login ? "login" : "")}|__zinc_si_status; history -a; __zinc_si_prompt|.bash_history|]";
        WaitFor(session, () => session.ReadScreenText().Contains(expected), expected);
    }

    // The README's hand-written snippets from before automatic integration (same function names
    // as the first automatic scripts, which then replaced them and sent nothing).
    private const string OldBashSnippet = """
        __zinc_prompt() { printf '\e]133;D;%s\a\e]133;A\a' "$?"; }
        PROMPT_COMMAND="__zinc_prompt${PROMPT_COMMAND:+; $PROMPT_COMMAND}"
        PS1="$PS1"'\[\e]133;B\a\]'
        PS0=$'\e]133;C\a'"$PS0"

        """;

    private const string OldZshSnippet = """
        PS1='%~ %# '
        autoload -Uz add-zsh-hook
        __zinc_precmd() { print -n "\e]133;D;$?\a\e]133;A\a" }
        __zinc_preexec() { print -n "\e]133;C\a" }
        precmd_functions=(__zinc_precmd $precmd_functions)
        add-zsh-hook preexec __zinc_preexec
        PS1="$PS1%{"$'\e]133;B\a'"%}"

        """;

    [Fact]
    public void BashLeavesAHandWrittenIntegrationWorking()
    {
        const string bash = @"C:\Program Files\Git\bin\bash.exe";
        Assert.SkipUnless(File.Exists(bash), "no Git Bash");
        var home = Directory.CreateTempSubdirectory("zinc-bash-").FullName;
        File.WriteAllText(Path.Combine(home, ".bashrc"), OldBashSnippet);
        using var session = StartShell($"\"{bash}\" --noprofile -i", new() { ["HOME"] = home });
        // The snippet sends no command text: it's read back from the screen.
        ChecksMarks(session, "false", 1, "cd /c/Windows", "/c/Windows");
    }

    [Fact]
    public void ZshLeavesAHandWrittenIntegrationWorking()
    {
        const string zsh = @"C:\msys64\usr\bin\zsh.exe";
        Assert.SkipUnless(File.Exists(zsh), "no MSYS2 zsh");
        var home = Directory.CreateTempSubdirectory("zinc-zsh-").FullName;
        File.WriteAllText(Path.Combine(home, ".zshrc"), OldZshSnippet);
        using var session = StartShell($"{zsh} -i", new() { ["HOME"] = home });
        ChecksMarks(session, "false", 1, "cd /c/Windows", "/c/Windows");
    }

    [Fact]
    public void Zsh()
    {
        const string zsh = @"C:\msys64\usr\bin\zsh.exe";
        Assert.SkipUnless(File.Exists(zsh), "no MSYS2 zsh");
        var home = Directory.CreateTempSubdirectory("zinc-zsh-").FullName;
        File.WriteAllText(Path.Combine(home, ".zshrc"), "PS1='%~ %# '\n");
        // MSYS2's own tools and a UTF-8 locale, as its launcher sets them (Git's mkdir on PATH first
        // named a non-ASCII folder differently from how this zsh looked it up).
        using var session = StartShell($"{zsh} -i", new()
        {
            ["HOME"] = home,
            ["LANG"] = "C.UTF-8",
            ["PATH"] = $@"C:\msys64\usr\bin;{Environment.GetEnvironmentVariable("PATH")}",
        });
        ChecksMarks(session, "false", 1, "cd /c/Windows", "/c/Windows");
        // Its own startup files still ran (and ZDOTDIR is back to unset).
        session.SendInput("echo \"[${ZDOTDIR-unset}]\"\r");
        WaitFor(session, () => session.ReadScreenText().Contains("[unset]"), "ZDOTDIR restored");
        ChecksFolderEncoding(session);
    }
}
