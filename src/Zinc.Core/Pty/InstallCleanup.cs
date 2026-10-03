using Microsoft.Win32;

namespace Zinc.Pty;

/// <summary>
/// What an uninstall takes back from Windows before the installer removes Zinc's files: the sign-in entry
/// (launch at login) and the default-terminal registration. Only what points into the install being removed:
/// another copy (the dev install, a zip) may own them, and they stay its.
/// </summary>
public static class InstallCleanup
{
    /// <summary>Where launch at login lives (LoginStartup writes it).</summary>
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "Zinc";

    /// <summary>Removes what points into <paramref name="installDir"/>; returns what it did, for the log.</summary>
    public static IReadOnlyList<string> Uninstall(RegistryKey root, string installDir)
    {
        var done = new List<string>();
        using (var run = root.OpenSubKey(RunKey, writable: true))
        {
            if (IsInside(ExeOf(run?.GetValue(RunValue) as string), installDir))
            {
                run!.DeleteValue(RunValue, throwOnMissingValue: false);
                done.Add("removed the sign-in entry");
            }
        }

        var terminal = new DefaultTerminal(root);
        if (IsInside(terminal.RegisteredExe(), installDir))
        {
            var wasDefault = terminal.Current() == DefaultTerminal.State.Zinc;
            terminal.Disable();
            done.Add(wasDefault ? "gave the default terminal back" : "removed the default-terminal class");
        }

        return done;
    }

    /// <summary>The program a command line starts: its quoted first part, else everything up to the first space.</summary>
    public static string? ExeOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }

    /// <summary>Whether <paramref name="path"/> is inside <paramref name="folder"/> (case-insensitive, whole folder names).</summary>
    public static bool IsInside(string? path, string folder)
    {
        if (path == null)
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
