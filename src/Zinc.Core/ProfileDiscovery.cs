using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace Zinc;

/// <summary>A profile found on this machine: its id, the name shown, and what it runs.</summary>
public sealed record DiscoveredProfile(string Id, string Name, string Command, string? Icon = null);

/// <summary>Which sources to look in; config: <c>[discover]</c>, each true by default.</summary>
public sealed record DiscoverySettings(
    bool Wsl = true, bool PowerShell = true, bool Cmd = true, bool GitBash = true, bool Msys2 = true, bool Ssh = true,
    bool VisualStudio = true);

/// <summary>
/// Finds the shells installed here, as Windows Terminal does: WSL distros, PowerShell, cmd,
/// Git Bash, MSYS2 (its UCRT64 environment), Visual Studio's developer shells, and the hosts in
/// ~/.ssh/config. Ids name where each came from (<c>wsl:Fedora</c>, <c>ssh:homelab</c>), so a
/// <c>[profile."wsl:Fedora"]</c> section can change one; the sources run on every config load.
/// </summary>
public static class ProfileDiscovery
{
    /// <summary>
    /// The search a profile id belongs to (its <c>[discover]</c> key: "wsl", "ssh", "visual-studio"…),
    /// or null for an id no search makes (a profile of your own).
    /// </summary>
    public static string? SourceOf(string id) => id switch
    {
        _ when id.StartsWith("wsl:", StringComparison.Ordinal) => "wsl",
        _ when id.StartsWith("ssh:", StringComparison.Ordinal) => "ssh",
        _ when id.StartsWith("vs:", StringComparison.Ordinal) => "visual-studio",
        _ when id.StartsWith("msys2:", StringComparison.Ordinal) => "msys2",
        "git-bash" => "git-bash",
        "pwsh" or "powershell" => "powershell",
        "cmd" => "cmd",
        _ => null,
    };

    /// <summary>
    /// The name a search would give profile <paramref name="id"/> ("SSH - homelab", "Fedora"), for
    /// one it didn't find this time (not here any more, or its search is off).
    /// </summary>
    public static string NameFor(string id) => id switch
    {
        _ when id.StartsWith("ssh:", StringComparison.Ordinal) => $"SSH - {id[4..]}",
        _ when id.StartsWith("wsl:", StringComparison.Ordinal) => id[4..],
        "pwsh" => "PowerShell",
        "powershell" => "Windows PowerShell",
        "cmd" => "Command Prompt",
        "git-bash" => "Git Bash",
        "msys2:ucrt64" => "MSYS2 UCRT64",
        _ => id,
    };

    /// <summary>Whether search <paramref name="source"/> (a <see cref="SourceOf"/> key) is on.</summary>
    public static bool IsOn(DiscoverySettings settings, string source) => source switch
    {
        "wsl" => settings.Wsl,
        "ssh" => settings.Ssh,
        "visual-studio" => settings.VisualStudio,
        "msys2" => settings.Msys2,
        "git-bash" => settings.GitBash,
        "powershell" => settings.PowerShell,
        "cmd" => settings.Cmd,
        _ => true,
    };

    public static IReadOnlyList<DiscoveredProfile> Discover(DiscoverySettings settings)
    {
        var found = new List<DiscoveredProfile>();
        void From(bool enabled, string source, Func<IEnumerable<DiscoveredProfile>> find)
        {
            if (!enabled)
            {
                return;
            }

            try
            {
                found.AddRange(find());
            }
            catch (Exception ex)
            {
                Log.Write($"discovery: {source} failed: {ex.Message}");
            }
        }

        From(settings.Wsl, "wsl", Wsl);
        From(settings.PowerShell, "powershell", PowerShellProfiles);
        From(settings.Cmd, "cmd", Cmd);
        From(settings.GitBash, "git-bash", GitBash);
        From(settings.Msys2, "msys2", Msys2);
        From(settings.VisualStudio, "visual-studio", VisualStudio);
        From(settings.Ssh, "ssh", Ssh);
        return found;
    }

    // ------------------------------------------------------------------ WSL

    private static IEnumerable<DiscoveredProfile> Wsl()
    {
        using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss == null)
        {
            return [];
        }

        var defaultId = lxss.GetValue("DefaultDistribution") as string;
        var distros = new List<(string Id, string Name)>();
        foreach (var id in lxss.GetSubKeyNames())
        {
            using var key = lxss.OpenSubKey(id);
            if (key?.GetValue("DistributionName") is string name)
            {
                distros.Add((id, name));
            }
        }

        return WslProfiles(distros, defaultId, Path.Combine(Environment.SystemDirectory, "wsl.exe"));
    }

    /// <summary>Profiles for registered distros: the default first, Docker Desktop's internal ones left out.</summary>
    public static IEnumerable<DiscoveredProfile> WslProfiles(IEnumerable<(string Id, string Name)> distros, string? defaultId, string wslExe) =>
        distros
            .Where(d => !d.Name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => string.Equals(d.Id, defaultId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            // By registration id, so renaming the distro doesn't break the profile.
            .Select(d => new DiscoveredProfile($"wsl:{d.Name}", d.Name, $"{wslExe} --distribution-id {d.Id} --cd ~"));

    // ------------------------------------------------------------ Windows shells

    private static IEnumerable<DiscoveredProfile> PowerShellProfiles()
    {
        if (ShellResolver.FindOnPath(Environment.GetEnvironmentVariable("PATH") ?? "", "pwsh.exe") != null)
        {
            yield return new DiscoveredProfile("pwsh", "PowerShell", "pwsh.exe");
        }

        if (File.Exists(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")))
        {
            yield return new DiscoveredProfile("powershell", "Windows PowerShell", "powershell.exe");
        }
    }

    private static IEnumerable<DiscoveredProfile> Cmd() => [new DiscoveredProfile("cmd", "Command Prompt", "cmd.exe")];

    private static IEnumerable<DiscoveredProfile> GitBash()
    {
        var roots = new List<string?>();
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GitForWindows"))
        {
            roots.Add(key?.GetValue("InstallPath") as string);
        }

        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git"));
        var bash = roots.OfType<string>().Select(r => Path.Combine(r, "bin", "bash.exe")).FirstOrDefault(File.Exists);
        return bash == null ? [] : [new DiscoveredProfile("git-bash", "Git Bash", $"\"{bash}\" --login -i")];
    }

    /// <summary>MSYS2's recommended environment, UCRT64, started as its docs say for other terminals.</summary>
    private static IEnumerable<DiscoveredProfile> Msys2()
    {
        const string root = @"C:\msys64";
        var script = Path.Combine(root, "msys2_shell.cmd");
        if (!File.Exists(script))
        {
            return [];
        }

        var icon = Path.Combine(root, "ucrt64.ico");
        return [new DiscoveredProfile("msys2:ucrt64", "MSYS2 UCRT64", $"{script} -defterm -here -no-start -ucrt64", File.Exists(icon) ? icon : null)];
    }

    // ------------------------------------------------------------ Visual Studio

    private static IReadOnlyList<DiscoveredProfile>? _visualStudio;

    /// <summary>
    /// Developer PowerShell and Developer Command Prompt for each Visual Studio install
    /// (vswhere). Asked once per run: it starts a process, and installs rarely change.
    /// </summary>
    private static IEnumerable<DiscoveredProfile> VisualStudio()
    {
        if (_visualStudio != null)
        {
            return _visualStudio;
        }

        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return _visualStudio = [];
        }

        using var process = Process.Start(new ProcessStartInfo(vswhere, "-all -prerelease -format json -utf8")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var json = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        var installs = JsonSerializer.Deserialize<JsonElement[]>(json) ?? [];
        return _visualStudio = VisualStudioProfiles(installs.Select(i => (
            i.GetProperty("instanceId").GetString()!,
            i.GetProperty("displayName").GetString()!,
            i.GetProperty("installationPath").GetString()!))).ToList();
    }

    /// <summary>The two developer shells of each install, started the way Windows Terminal starts them.</summary>
    public static IEnumerable<DiscoveredProfile> VisualStudioProfiles(IEnumerable<(string InstanceId, string DisplayName, string Path)> installs)
    {
        foreach (var (instance, display, path) in installs)
        {
            var devShell = System.IO.Path.Combine(path, "Common7", "Tools", "Microsoft.VisualStudio.DevShell.dll");
            var devCmd = System.IO.Path.Combine(path, "Common7", "Tools", "VsDevCmd.bat");
            yield return new DiscoveredProfile($"vs:{instance}:pwsh", $"Developer PowerShell for {display}",
                $"powershell.exe -NoExit -Command \"&{{Import-Module \"\"\"{devShell}\"\"\"; Enter-VsDevShell {instance} -SkipAutomaticLocation}}\"");
            yield return new DiscoveredProfile($"vs:{instance}:cmd", $"Developer Command Prompt for {display}",
                $"cmd.exe /k \"{devCmd}\"");
        }
    }

    // ------------------------------------------------------------------ SSH

    private static IEnumerable<DiscoveredProfile> Ssh()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var config = Path.Combine(dir, "config");
        if (!File.Exists(config))
        {
            return [];
        }

        var hosts = SshHosts(File.ReadAllText(config), pattern => ReadIncludes(dir, pattern));
        return hosts.Select(h => new DiscoveredProfile($"ssh:{h}", $"SSH - {h}", $"ssh.exe {h}"));
    }

    /// <summary>Files an ssh <c>Include</c> names (relative to ~/.ssh; <c>*</c> within a folder).</summary>
    private static IEnumerable<string> ReadIncludes(string sshDir, string pattern)
    {
        var expanded = pattern.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), pattern[1..].TrimStart('/', '\\'))
            : Path.Combine(sshDir, pattern);
        var folder = Path.GetDirectoryName(expanded);
        var name = Path.GetFileName(expanded);
        if (folder == null || !Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder, name).Order(StringComparer.OrdinalIgnoreCase).Select(File.ReadAllText);
    }

    /// <summary>
    /// The concrete hosts in an ssh config: every <c>Host</c> name without wildcards or negation,
    /// in order, once each; <c>Include</c>d files are read in place (via
    /// <paramref name="readInclude"/>, which returns their contents).
    /// </summary>
    public static IReadOnlyList<string> SshHosts(string config, Func<string, IEnumerable<string>> readInclude)
    {
        var hosts = new List<string>();
        Collect(config, 0);
        return hosts;

        void Collect(string text, int depth)
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                // "Keyword value..." or "Keyword=value...".
                var split = line.IndexOfAny([' ', '\t', '=']);
                if (split < 0)
                {
                    continue;
                }

                var keyword = line[..split];
                var values = line[(split + 1)..].Trim().TrimStart('=').Trim();
                if (keyword.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var name in Tokens(values))
                    {
                        if (name.IndexOfAny(['*', '?', '!']) < 0 && !hosts.Contains(name))
                        {
                            hosts.Add(name);
                        }
                    }
                }
                else if (keyword.Equals("Include", StringComparison.OrdinalIgnoreCase) && depth < 8)
                {
                    foreach (var pattern in Tokens(values))
                    {
                        foreach (var included in readInclude(pattern))
                        {
                            Collect(included, depth + 1);
                        }
                    }
                }
            }
        }

        // Space-separated, with "double quoted" tokens.
        static IEnumerable<string> Tokens(string s)
        {
            var i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                {
                    i++;
                }

                if (i >= s.Length)
                {
                    yield break;
                }

                if (s[i] == '"')
                {
                    var close = s.IndexOf('"', i + 1);
                    var end = close < 0 ? s.Length : close;
                    yield return s[(i + 1)..end];
                    i = end + 1;
                }
                else
                {
                    var start = i;
                    while (i < s.Length && !char.IsWhiteSpace(s[i]))
                    {
                        i++;
                    }

                    yield return s[start..i];
                }
            }
        }
    }
}
