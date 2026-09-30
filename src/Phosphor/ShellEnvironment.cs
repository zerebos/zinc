using Microsoft.Win32;
using Phosphor.Terminal;

namespace Phosphor;

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
    /// Where <paramref name="distro"/> mounts Windows drives, from its /etc/wsl.conf (read through
    /// its share; the pane asking is running it). Unreadable: the default.
    /// </summary>
    private static string? MountRoot(string distro)
    {
        var conf = $@"\\wsl.localhost\{distro}\etc\wsl.conf";
        try
        {
            var root = WslConf.MountRoot(File.Exists(conf) ? File.ReadAllText(conf) : null);
            Log.Write($"wsl: {distro} mounts drives at {root ?? "(nowhere)"}");
            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"wsl: couldn't read {conf}: {ex.Message}");
            return WslConf.DefaultMountRoot;
        }
    }

    /// <summary>The distro a WSL command line runs (its -d/--distribution-id, else the default); null if it isn't WSL.</summary>
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
