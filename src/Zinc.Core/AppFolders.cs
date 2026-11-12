namespace Zinc;

/// <summary>
/// Where Zinc keeps what's yours: <c>%APPDATA%\Zinc</c> holds the config, schemes, session, logs, crash log
/// and icon cache. <c>%LOCALAPPDATA%\Zinc</c> is the installer's folder (Velopack's install root, replaced on
/// install and removed on uninstall), so nothing of yours may live there. Logs and the cache used to live there,
/// and <see cref="MoveOldLocalData"/> moves them across once.
/// </summary>
public static class AppFolders
{
    /// <summary>%APPDATA%\Zinc.</summary>
    public static string Data { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zinc");

    /// <summary>
    /// %LOCALAPPDATA%\Zinc, where logs and the icon cache lived before (and where the installer installs).
    /// </summary>
    public static string OldLocal { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zinc");

    /// <summary>What used to live in <see cref="OldLocal"/>.</summary>
    public static readonly string[] MovedItems = ["logs", "crash.log", "cache"];

    /// <summary>
    /// Moves <see cref="MovedItems"/> from <paramref name="from"/> into <paramref name="to"/>, moving a folder's
    /// files one by one. When a file is already in <paramref name="to"/>, that copy stays and the old one is
    /// dropped. A file that can't be moved (because it's in use) stays for the next start. Removes
    /// <paramref name="from"/> if that left it empty, and never touches anything else in it (an installed Zinc).
    /// Returns how many files moved.
    /// </summary>
    public static int MoveOldLocalData(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            return 0;
        }

        var moved = 0;
        foreach (var item in MovedItems)
        {
            var source = Path.Combine(from, item);
            var target = Path.Combine(to, item);
            if (File.Exists(source))
            {
                moved += MoveFile(source, target) ? 1 : 0;
            }
            else if (Directory.Exists(source))
            {
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    moved += MoveFile(file, Path.Combine(target, Path.GetRelativePath(source, file))) ? 1 : 0;
                }

                RemoveEmptyFolders(source);
            }
        }

        RemoveIfEmpty(from);
        return moved;
    }

    private static bool MoveFile(string source, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                File.Delete(source); // the newer copy is already there
                return false;
            }

            File.Move(source, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // in use, so it waits for the next start
        }
    }

    private static void RemoveEmptyFolders(string folder)
    {
        foreach (var sub in Directory.EnumerateDirectories(folder))
        {
            RemoveEmptyFolders(sub);
        }

        RemoveIfEmpty(folder);
    }

    private static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
