using Microsoft.Win32;

namespace Zinc.Shell;

/// <summary>
/// "Open in Zinc" on Explorer's menu for a folder, a folder's background and a drive (<c>[window] explorer-menu</c>).
/// These are classic shell verbs under the user's Software\Classes, each starting zinc.exe with <c>--folder</c>. On Windows 11
/// they're under "Show more options" (the new menu takes only packaged apps' commands). <paramref name="root"/> is
/// HKCU, or a scratch key in tests.
/// </summary>
public sealed class ExplorerMenu(RegistryKey root)
{
    public const string Label = "Open in Zinc";

    // The three places, and what each passes. A drive's %V is "C:\", which ends a quoted argument with \" (an escaped
    // quote), so it passes "C:\." instead (LaunchRequest reads that back as C:\).
    private static readonly (string Key, string Folder)[] Places =
    [
        (@"Software\Classes\Directory\shell\Zinc", "%V"),
        (@"Software\Classes\Directory\Background\shell\Zinc", "%V"),
        (@"Software\Classes\Drive\shell\Zinc", "%V."),
    ];

    /// <summary>Adds the entries for <paramref name="exe"/>, or removes them when it's null. True if anything changed.</summary>
    public bool Apply(string? exe)
    {
        var changed = false;
        foreach (var (key, folder) in Places)
        {
            if (exe == null)
            {
                if (root.OpenSubKey(key) != null)
                {
                    root.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
                    changed = true;
                }

                continue;
            }

            var command = $"\"{exe}\" {LaunchRequest.FolderFlag} \"{folder}\"";
            using var verb = root.CreateSubKey(key);
            using var run = verb.CreateSubKey("command");
            if (verb.GetValue(null) as string != Label || verb.GetValue("Icon") as string != exe || run.GetValue(null) as string != command)
            {
                verb.SetValue(null, Label);
                verb.SetValue("Icon", exe);
                run.SetValue(null, command);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>The exe the folder entry starts, or null if there's none.</summary>
    public string? RegisteredExe()
    {
        using var run = root.OpenSubKey(Places[0].Key + @"\command");
        return Pty.InstallCleanup.ExeOf(run?.GetValue(null) as string);
    }
}
