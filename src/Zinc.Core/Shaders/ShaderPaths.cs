namespace Zinc.Shaders;

/// <summary>
/// Shader file paths between config.toml and the disk: as written they may be relative to the config file's folder,
/// start with <c>~</c> or hold %VARIABLES%; Settings writes them back as short as it can, with forward slashes (no
/// escaping in TOML, and Windows takes them). A bare name ("crt": no folder, no extension) is a shader by name, as
/// color schemes are: yours in <c>shaders\</c> next to the config, else one that comes with Zinc.
/// </summary>
public static class ShaderPaths
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The shaders that come with Zinc (<c>Shaders\</c> next to the exe; tests point it elsewhere).</summary>
    public static string BundledFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Shaders");

    /// <summary>Whether <paramref name="written"/> names a shader rather than giving a file.</summary>
    public static bool IsName(string written)
    {
        var text = written.Trim();
        return text.Length > 0 && text.IndexOfAny(['/', '\\', ':', '.', '%', '~']) < 0;
    }

    /// <summary>Your own shader by that name (in <c>shaders\</c> next to the config), else Zinc's.</summary>
    private static string ByName(string name, string configFile)
    {
        var yours = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configFile)) ?? ".", "shaders", name + ".glsl");
        return File.Exists(yours) ? yours : Path.Combine(BundledFolder, name + ".glsl");
    }

    /// <summary>Whether <paramref name="full"/> is one of the shaders that come with Zinc.</summary>
    public static bool IsBundled(string full) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(full)), Path.GetFullPath(BundledFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>The shaders that come with Zinc: name and what it does (its first comment line, after "Zinc: name.").</summary>
    public static IReadOnlyList<(string Name, string Description)> Bundled()
    {
        if (!Directory.Exists(BundledFolder))
        {
            return [];
        }

        return [.. Directory.GetFiles(BundledFolder, "*.glsl").Order(StringComparer.OrdinalIgnoreCase).Select(file =>
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var first = File.ReadLines(file).FirstOrDefault() ?? "";
            var prefix = $"// Zinc: {name}.";
            var description = first.StartsWith(prefix, StringComparison.Ordinal) ? first[prefix.Length..].Trim() : "";
            // The first sentence is enough for a menu.
            var end = description.IndexOf(". ", StringComparison.Ordinal);
            return (name, end > 0 ? description[..(end + 1)] : description);
        })];
    }

    /// <summary>A path as written in <paramref name="configFile"/>, made full.</summary>
    public static string Resolve(string written, string configFile)
    {
        if (IsName(written))
        {
            return ByName(written.Trim(), configFile);
        }

        var path = Environment.ExpandEnvironmentVariables(written.Trim());
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            path = Path.Combine(Home, path[2..]);
        }

        return path.Length == 0 ? "" : Path.GetFullPath(path, Path.GetDirectoryName(Path.GetFullPath(configFile)) ?? ".");
    }

    /// <summary>
    /// How Settings writes <paramref name="full"/> into <paramref name="configFile"/>: relative to its folder when
    /// inside it ("shaders/crt.glsl"), else from your home ("~/dev/shaders/crt.glsl"), else the whole path.
    /// </summary>
    public static string ForConfig(string full, string configFile)
    {
        full = Path.GetFullPath(full);
        if (IsBundled(full))
        {
            return Path.GetFileNameWithoutExtension(full); // one of Zinc's: by name
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(configFile)) ?? "";
        string written;
        if (Inside(full, folder))
        {
            written = Path.GetRelativePath(folder, full);
        }
        else if (Inside(full, Home))
        {
            written = "~/" + Path.GetRelativePath(Home, full);
        }
        else
        {
            written = full;
        }

        return written.Replace('\\', '/');
    }

    /// <summary>The files a config value lists (one path, a list of them, or nothing), as written.</summary>
    public static IReadOnlyList<string> Listed(object? value) => value switch
    {
        string one => [one],
        IEnumerable<object> list => [.. list.OfType<string>()],
        _ => [],
    };

    private static bool Inside(string path, string folder) =>
        folder.Length > 0 && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
