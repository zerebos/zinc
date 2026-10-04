using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;

namespace Zinc;

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
    private static readonly string IconCacheDir = Path.Combine(AppFolders.Data, "cache", "icons");

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

    /// <summary>
    /// The profile's icon as a file the taskbar's jump list can show (an .ico, or an .exe's own icon): found as the
    /// tab icon is, without the glyph fallbacks (null: Zinc's icon). A PNG given as the icon is wrapped in an .ico.
    /// </summary>
    internal static Zinc.Shell.JumpIcon JumpIconFor(Profile profile)
    {
        if (profile.Config.Icon is { } explicitPath && File.Exists(explicitPath))
        {
            var extension = Path.GetExtension(explicitPath).ToLowerInvariant();
            return extension is ".ico" or ".exe" or ".dll" ? new(explicitPath)
                : extension == ".png" && PngAsIco(explicitPath) is { } ico ? new(ico)
                : new(null);
        }

        var (exe, args) = SplitCommand(ShellResolver.ForProfile(profile.Config));
        var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        if (name == "wsl" && WslDistroIcon(args) is { } distroIcon)
        {
            return new(distroIcon);
        }

        if (name == "ssh" || FindExecutable(exe) is not { } path)
        {
            return new(null);
        }

        var gitIcon = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "mingw64", "share", "git", "git-for-windows.ico"));
        return new(name == "bash" && File.Exists(gitIcon) ? gitIcon : path);
    }

    /// <summary>A PNG as a one-image .ico (the format holds PNG data as it is), cached; null if it can't be written.</summary>
    private static string? PngAsIco(string png)
    {
        try
        {
            var info = new FileInfo(png);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{png}|{info.LastWriteTimeUtc.Ticks}")))[..16];
            var ico = Path.Combine(IconCacheDir, $"{Path.GetFileNameWithoutExtension(png)}-{hash}.ico");
            if (!File.Exists(ico))
            {
                var data = File.ReadAllBytes(png);
                Directory.CreateDirectory(IconCacheDir);
                using var file = File.Create(ico);
                using var w = new BinaryWriter(file);
                w.Write((ushort)0); // reserved
                w.Write((ushort)1); // icon
                w.Write((ushort)1); // one image
                w.Write((byte)0); // width 256 or more (Windows reads the PNG's own size)
                w.Write((byte)0);
                w.Write((byte)0); // no palette
                w.Write((byte)0);
                w.Write((ushort)1); // planes
                w.Write((ushort)32); // bits
                w.Write(data.Length);
                w.Write(6 + 16); // the image's offset
                w.Write(data);
            }

            return ico;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"jump list: icon {png} not converted ({ex.Message})");
            return null;
        }
    }

    private static IconSource Bitmap(string path) =>
        new BitmapIconSource { UriSource = new Uri(Small(path)), ShowAsMonochrome = false };

    /// <summary>The largest an icon is kept: tabs show it at 16-20 DIPs, so 64 px covers 300% scaling.</summary>
    private const int IconPixels = 64;

    /// <summary>
    /// A picture used as an icon, shrunk to <see cref="IconPixels"/> (cached by path and timestamp), or the file
    /// itself when it's small already or an .ico (Windows picks an .ico's frame by size). Every place an icon
    /// shows decodes its file at full size: a 2089x2050 PNG cost 16 MB per tab header and strip, ~400 MB with
    /// 9 workspaces (2026-10-02).
    /// </summary>
    private static string Small(string path)
    {
        if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        try
        {
            var stamp = File.GetLastWriteTimeUtc(path).Ticks;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{path}|{stamp}|{IconPixels}")))[..16];
            var png = Path.Combine(IconCacheDir, $"{Path.GetFileNameWithoutExtension(path)}-{hash}.png");
            if (File.Exists(png))
            {
                return png;
            }

            using var image = System.Drawing.Image.FromFile(path);
            if (image.Width <= IconPixels && image.Height <= IconPixels)
            {
                return path;
            }

            // Fit inside the square, keeping the picture's shape.
            var scale = Math.Min((double)IconPixels / image.Width, (double)IconPixels / image.Height);
            int w = Math.Max(1, (int)Math.Round(image.Width * scale)), h = Math.Max(1, (int)Math.Round(image.Height * scale));
            using var small = new System.Drawing.Bitmap(IconPixels, IconPixels, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(image, (IconPixels - w) / 2, (IconPixels - h) / 2, w, h);
            }

            Directory.CreateDirectory(IconCacheDir);
            small.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            return png;
        }
        catch (Exception ex)
        {
            Log.Write($"icon: {path}: couldn't shrink it ({ex.Message}); using it as it is");
            return path;
        }
    }

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
        using var key = ShellEnvironment.WslDistroKey(args);
        if (key?.GetValue("BasePath") is not string basePath)
        {
            return null;
        }

        var icon = Path.Combine(basePath.Replace(@"\\?\", ""), "shortcut.ico");
        return File.Exists(icon) ? icon : null;
    }

    /// <summary>Resolves a bare executable name like CreateProcess would (System32, then PATH).</summary>
    internal static string? FindExecutable(string exe)
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
