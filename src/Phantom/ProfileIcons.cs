using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;

namespace Phantom;

/// <summary>
/// Picks each profile's tab icon, the way Windows Terminal does when none is set:
/// explicit <c>icon</c> → the WSL distro's registered icon → Git for Windows' icon →
/// a globe-style glyph for SSH → the command's own executable icon → a generic
/// terminal glyph. Results are cached per profile, so tabs never reload an icon.
/// </summary>
internal static class ProfileIcons
{
    // Segoe Fluent Icons glyphs.
    private const string NetworkGlyph = "\uE968";
    private const string TerminalGlyph = "\uE756";

    private static readonly Dictionary<Profile, IconSource> Cache = new();
    private static readonly string IconCacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Phantom", "cache", "icons");

    public static IconSource For(Profile profile)
    {
        if (!Cache.TryGetValue(profile, out var icon))
        {
            icon = Resolve(profile);
            Cache[profile] = icon;
        }

        return icon;
    }

    private static IconSource Resolve(Profile profile)
    {
        if (profile.Config.Icon is { } explicitPath && File.Exists(explicitPath))
        {
            return Bitmap(explicitPath);
        }

        var command = ShellResolver.ForProfile(profile.Config);
        var (exe, args) = SplitCommand(command);
        var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();

        if (name == "wsl" && WslDistroIcon(args) is { } distroIcon)
        {
            return Bitmap(distroIcon);
        }

        if (name == "ssh")
        {
            return Glyph(NetworkGlyph);
        }

        if (FindExecutable(exe) is { } path)
        {
            // Git Bash's bash.exe has a generic icon; Git for Windows ships its own.
            var gitIcon = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "mingw64", "share", "git", "git-for-windows.ico"));
            if (name == "bash" && File.Exists(gitIcon))
            {
                return Bitmap(gitIcon);
            }

            if (ExtractExeIcon(path) is { } png)
            {
                return Bitmap(png);
            }
        }

        return Glyph(TerminalGlyph);
    }

    private static IconSource Bitmap(string path) =>
        new BitmapIconSource { UriSource = new Uri(path), ShowAsMonochrome = false };

    private static IconSource Glyph(string glyph) =>
        new FontIconSource { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets") };

    /// <summary>Splits a command line into its executable and the rest.</summary>
    internal static (string Exe, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end < 0 ? (command.Trim('"'), "") : (command[1..end], command[(end + 1)..].Trim());
        }

        var space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    /// <summary>The icon WSL registered for the distro named in <c>-d</c>/<c>--distribution(-id)</c>.</summary>
    private static string? WslDistroIcon(string args)
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
            using var key = lxss.OpenSubKey(keyName);
            var matches = id != null
                ? keyName.Equals(id, StringComparison.OrdinalIgnoreCase)
                : name != null
                    ? string.Equals(key?.GetValue("DistributionName") as string, name, StringComparison.OrdinalIgnoreCase)
                    : keyName.Equals(defaultId, StringComparison.OrdinalIgnoreCase);
            if (matches && key?.GetValue("BasePath") is string basePath)
            {
                var icon = Path.Combine(basePath.Replace(@"\\?\", ""), "shortcut.ico");
                return File.Exists(icon) ? icon : null;
            }
        }

        return null;
    }

    /// <summary>Resolves a bare executable name like CreateProcess would (System32, then PATH).</summary>
    private static string? FindExecutable(string exe)
    {
        exe = Environment.ExpandEnvironmentVariables(exe);
        if (Path.IsPathRooted(exe))
        {
            return File.Exists(exe) ? exe : null;
        }

        var names = Path.HasExtension(exe) ? [exe] : new[] { exe + ".exe", exe };
        var dirs = new[] { Environment.SystemDirectory }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var dir in dirs)
        {
            foreach (var n in names)
            {
                var candidate = Path.Combine(dir.Trim('"'), n);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Saves an executable's icon as a PNG (cached by path and timestamp) and returns its path.</summary>
    private static string? ExtractExeIcon(string exePath)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(exePath).Ticks;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{exePath}|{stamp}")))[..16];
            var png = Path.Combine(IconCacheDir, $"{Path.GetFileNameWithoutExtension(exePath)}-{hash}.png");
            if (!File.Exists(png))
            {
                using var icon = System.Drawing.Icon.ExtractIcon(exePath, 0, 32);
                if (icon == null)
                {
                    return null;
                }

                Directory.CreateDirectory(IconCacheDir);
                using var bitmap = icon.ToBitmap();
                bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }

            return png;
        }
        catch (Exception ex)
        {
            Log.Write($"icon: {exePath}: {ex.Message}");
            return null;
        }
    }
}
