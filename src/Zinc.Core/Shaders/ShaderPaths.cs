namespace Zinc.Shaders;

/// <summary>
/// Shader file paths between config.toml and the disk: as written they may be relative to the config file's folder,
/// start with <c>~</c> or hold %VARIABLES%; Settings writes them back as short as it can, with forward slashes (no
/// escaping in TOML, and Windows takes them).
/// </summary>
public static class ShaderPaths
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>A path as written in <paramref name="configFile"/>, made full.</summary>
    public static string Resolve(string written, string configFile)
    {
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
