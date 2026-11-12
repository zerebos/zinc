using Microsoft.Win32;
using Zinc.Terminal;

namespace Zinc;

/// <summary>What a pane's command line runs: which WSL distro, which MSYS installation.</summary>
internal static class ShellEnvironment
{
    /// <summary>
    /// The registry key (under HKCU\...\Lxss) of the distro that <c>wsl.exe</c> arguments
    /// select with <c>-d</c>/<c>--distribution</c>/<c>--distribution-id</c>, else the default one.
    /// </summary>
    public static RegistryKey? WslDistroKey(string args)
    {
        var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? name = null, id = null;
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "-d" or "--distribution": name = tokens[i + 1]; break;
                case "--distribution-id": id = tokens[i + 1]; break;
            }
        }

        using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss == null)
        {
            return null;
        }

        var defaultId = lxss.GetValue("DefaultDistribution") as string;
        foreach (var keyName in lxss.GetSubKeyNames())
        {
            var key = lxss.OpenSubKey(keyName);
            var matches = id != null
                ? keyName.Equals(id, StringComparison.OrdinalIgnoreCase)
                : name != null
                    ? string.Equals(key?.GetValue("DistributionName") as string, name, StringComparison.OrdinalIgnoreCase)
                    : keyName.Equals(defaultId, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                return key;
            }

            key?.Dispose();
        }

        return null;
    }

    /// <summary>
    /// Where <paramref name="distro"/> mounts Windows drives, from its /etc/wsl.conf (read through its share, which
    /// works because the pane asking is running it). If the file is unreadable, it's the default. A host name of its
    /// own joins <see cref="LocalHosts"/>, so its shells' folder reports count.
    /// </summary>
    private static string? MountRoot(string distro)
    {
        var conf = $@"\\wsl.localhost\{distro}\etc\wsl.conf";
        try
        {
            var text = File.Exists(conf) ? File.ReadAllText(conf) : null;
            var root = WslConf.MountRoot(text);
            Log.Write($"wsl: {distro} mounts drives at {root ?? "(nowhere)"}");
            if (WslConf.Hostname(text) is { } host)
            {
                LocalHosts.Add(host);
                Log.Write($"wsl: {distro} is named {host}");
            }

            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"wsl: couldn't read {conf}: {ex.Message}");
            return WslConf.DefaultMountRoot;
        }
    }

    /// <summary>For each host name not known to be this PC, whether a running distro turned out to be named that.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<bool>> Learning = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="then"/> (in the background) if <paramref name="host"/>, a folder report's host not known to
    /// be this PC, is a running WSL distro's own name (wsl.conf <c>[network] hostname</c>). Its panes may run it
    /// without wsl.exe (ubuntu.exe, or wsl from another shell), so their wsl.conf wasn't read. Only running distros are
    /// read, because the share would start a stopped one, and a distro sending reports is running. Each host name is
    /// checked once.
    /// </summary>
    public static void WhenWslHost(string host, Action then) =>
        Learning.GetOrAdd(host, name => Task.Run(() => LearnWslHost(name)))
            .ContinueWith(learned =>
            {
                if (learned.Result)
                {
                    then();
                }
            }, TaskScheduler.Default);

    private static bool LearnWslHost(string host)
    {
        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            var installed = lxss?.GetSubKeyNames()
                .Select(id => { using var key = lxss.OpenSubKey(id); return key?.GetValue("DistributionName") as string; })
                .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (installed.Count == 0)
            {
                return false;
            }

            var list = new System.Diagnostics.ProcessStartInfo("wsl.exe", "--list --running --quiet")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.Unicode, // wsl.exe writes UTF-16
            };
            using var process = System.Diagnostics.Process.Start(list)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // Names it lists that are installed distros (with none running it prints a sentence instead).
            foreach (var distro in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(installed.Contains))
            {
                MountRoot(distro); // adds its host name to LocalHosts
            }

            var local = LocalHosts.Is(host);
            Log.Write($"wsl: {host} is {(local ? "a running distro" : "no running distro's name")}");
            return local;
        }
        catch (Exception ex)
        {
            Log.Write($"wsl: couldn't learn whether {host} is a distro: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The distro a WSL command line runs (its -d/--distribution-id, else the default). Null if it isn't WSL.
    /// </summary>
    public static string? WslDistroOf(string commandLine)
    {
        try
        {
            var (exe, args) = ProfileIcons.SplitCommand(commandLine);
            if (!Path.GetFileNameWithoutExtension(exe).Equals("wsl", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var key = WslDistroKey(args);
            return key?.GetValue("DistributionName") as string;
        }
        catch (Exception ex)
        {
            Log.Write($"wsl distro of {commandLine}: {ex.Message}");
            return null;
        }
    }

    /// <summary>How a pane running <paramref name="commandLine"/> quotes paths (files dropped on it).</summary>
    public static PathQuoting QuotingFor(string commandLine, LinkContext context)
    {
        if (context.WslDistro != null || context.PosixRoot != null)
        {
            return PathQuoting.Posix;
        }

        var exe = Path.GetFileNameWithoutExtension(ProfileIcons.SplitCommand(commandLine).Item1);
        return exe.Equals("pwsh", StringComparison.OrdinalIgnoreCase) || exe.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            ? PathQuoting.PowerShell
            : PathQuoting.Windows;
    }

    /// <summary>How file links printed in a pane running <paramref name="commandLine"/> map to Windows paths.</summary>
    public static LinkContext LinkContextFor(string commandLine)
    {
        try
        {
            var (exe, args) = ProfileIcons.SplitCommand(commandLine);
            if (Path.GetFileNameWithoutExtension(exe).Equals("wsl", StringComparison.OrdinalIgnoreCase))
            {
                using var key = WslDistroKey(args);
                var distro = key?.GetValue("DistributionName") as string;
                return LinkContext.Windows with { WslDistro = distro, WslMountRoot = distro == null ? WslConf.DefaultMountRoot : MountRoot(distro) };
            }

            // Git for Windows and MSYS2 keep their POSIX root (usr\bin, etc) at or above the shell.
            var dir = ProfileIcons.FindExecutable(exe) is { } path ? Path.GetDirectoryName(path) : null;
            for (var i = 0; dir != null && i < 3; i++, dir = Path.GetDirectoryName(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "usr", "bin")) && Directory.Exists(Path.Combine(dir, "etc")))
                {
                    return LinkContext.Windows with { PosixRoot = dir };
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"link context for {commandLine}: {ex.Message}");
        }

        return LinkContext.Windows;
    }
}
