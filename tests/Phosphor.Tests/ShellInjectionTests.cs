using System.Diagnostics;
using Phosphor.Terminal;

namespace Phosphor.Tests;

public class ShellInjectionTests
{
    private const string Scripts = @"C:\Phosphor\ShellIntegration";

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
        Assert.Contains(@"ReadAllText('C:\Phosphor\ShellIntegration\pwsh\phosphor.ps1')", launch.CommandLine);
        Assert.Empty(launch.Environment);
    }

    [Theory]
    [InlineData("pwsh.exe -Command Get-Date")]
    [InlineData("pwsh.exe -c ls")]
    [InlineData("pwsh -File build.ps1")]
    [InlineData("pwsh build.ps1")]
    [InlineData("pwsh.exe -NoExit -Command \"&{Import-Module x}\"")] // Developer PowerShell for VS
    [InlineData("powershell -EncodedCommand ZQBjAGgAbwA=")]
    [InlineData("powershell -ec ZQBjAGgAbwA=")]
    public void PowerShellToldToRunSomethingIsLeftAlone(string command)
    {
        var launch = Prepare(command);
        Assert.Equal(command, launch.CommandLine);
        Assert.Empty(launch.Environment);
    }

    [Fact]
    public void NoExitIsNotRepeated()
    {
        var launch = Prepare("pwsh.exe -noe");
        Assert.StartsWith("pwsh.exe -noe -Command ", launch.CommandLine);
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Git\bin\bash.exe"" --login -i")]
    [InlineData(@"C:\Windows\system32\wsl.exe --distribution-id {1234} --cd ~")]
    [InlineData(@"C:\msys64\msys2_shell.cmd -defterm -here -no-start -ucrt64")]
    public void OtherShellsGetVariablesAndKeepTheirCommand(string command)
    {
        var launch = Prepare(command);
        Assert.Equal(command, launch.CommandLine);
        Assert.Equal("C:/Phosphor/ShellIntegration", launch.Environment["PHOSPHOR_SHELL_INTEGRATION_DIR"]);
        Assert.Equal("C:/Phosphor/ShellIntegration/zsh", launch.Environment["ZDOTDIR"]);
        Assert.Equal(ShellIntegration.BashBootstrap, launch.Environment["PROMPT_COMMAND"]);
        Assert.Equal("PHOSPHOR_SHELL_INTEGRATION_DIR/p:PROMPT_COMMAND:ZDOTDIR/p", launch.Environment["WSLENV"]);
        Assert.False(launch.Environment.ContainsKey("PHOSPHOR_ZDOTDIR"));
    }

    [Fact]
    public void KeepsTheUsersWslenvAndZdotdir()
    {
        var launch = Prepare("wsl.exe", new() { ["WSLENV"] = "GOPATH/l:ZDOTDIR/p", ["ZDOTDIR"] = @"C:\me\zsh" });
        Assert.Equal("GOPATH/l:ZDOTDIR/p:PHOSPHOR_SHELL_INTEGRATION_DIR/p:PROMPT_COMMAND", launch.Environment["WSLENV"]);
        Assert.Equal(@"C:\me\zsh", launch.Environment["PHOSPHOR_ZDOTDIR"]);
    }

    [Theory]
    [InlineData("-NoLogo -NoProfile", false, false)]
    [InlineData("-wd C:\\x -NoExit", false, true)]
    [InlineData("-WindowStyle Hidden script.ps1", true, false)]
    [InlineData("-NoLogo -", true, false)]
    [InlineData("-ExecutionPolicy:Bypass -Nologo", false, false)]
    [InlineData("/c dir", true, false)]
    public void ReadsPowerShellsArguments(string args, bool runs, bool noExit) =>
        Assert.Equal((runs, noExit), ShellIntegration.PowerShellArguments(ShellIntegration.Split(args)));

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
        session.SendInput("mkdir -p '/tmp/phosphor a é' && cd '/tmp/phosphor a é'\r");
        WaitFor(session, () => ShellDirectory.Parse(session.ReportedDirectory)?.EndsWith("/tmp/phosphor a é", StringComparison.Ordinal) == true,
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
        var home = Directory.CreateTempSubdirectory("phosphor-bash-").FullName;
        File.WriteAllText(Path.Combine(home, ".bashrc"), "PROMPT_COMMAND=\"${PROMPT_COMMAND:+$PROMPT_COMMAND; }echo -n\"\n");
        using var session = StartShell($"\"{bash}\" --noprofile -i", new() { ["HOME"] = home });
        ChecksMarks(session, "false", 1, "cd /c/Windows", "/c/Windows");
        session.SendInput("echo \"[$PROMPT_COMMAND]\"\r");
        WaitFor(session, () => session.ReadScreenText().Contains("[__phosphor_status; echo -n; __phosphor_prompt]"), "PROMPT_COMMAND");
    }

    [Fact]
    public void Zsh()
    {
        const string zsh = @"C:\msys64\usr\bin\zsh.exe";
        Assert.SkipUnless(File.Exists(zsh), "no MSYS2 zsh");
        var home = Directory.CreateTempSubdirectory("phosphor-zsh-").FullName;
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
