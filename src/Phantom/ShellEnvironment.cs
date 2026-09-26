using Microsoft.Win32;
using Phantom.Terminal;

namespace Phantom;

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

    /// <summary>How file links printed in a pane running <paramref name="commandLine"/> map to Windows paths.</summary>
    public static LinkContext LinkContextFor(string commandLine)
    {
        try
        {
            var (exe, args) = ProfileIcons.SplitCommand(commandLine);
            if (Path.GetFileNameWithoutExtension(exe).Equals("wsl", StringComparison.OrdinalIgnoreCase))
            {
                using var key = WslDistroKey(args);
                return LinkContext.Windows with { WslDistro = key?.GetValue("DistributionName") as string };
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
