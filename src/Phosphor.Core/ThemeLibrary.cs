namespace Phosphor;

/// <summary>
/// Named color schemes: the user's (a themes folder next to the config file) over the ones that
/// ship with Phosphor. Files use Ghostty's theme format (as iTerm2-Color-Schemes publishes it):
/// <c>key = value</c> lines, of which the colors apply.
/// </summary>
public sealed class ThemeLibrary(string userFolder, string bundledFolder)
{
    /// <summary>The library for a config file: its folder's <c>themes</c>, then <paramref name="bundledFolder"/>.</summary>
    public static ThemeLibrary For(string configPath, string bundledFolder) =>
        new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", "themes"), bundledFolder);

    public string UserFolder => userFolder;

    private IEnumerable<string> Folders => [userFolder, bundledFolder];

    /// <summary>Every scheme's name, sorted; a user's scheme hides a bundled one of the same name.</summary>
    public IReadOnlyList<string> Names() =>
        Folders.Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFiles)
            .Select(f => Path.GetFileName(f))
            .DistinctBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The file for scheme <paramref name="name"/> (the user's first; names match case-insensitively), or null.</summary>
    public string? Find(string name)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        foreach (var folder in Folders)
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // Case-insensitive fallback (the collection's names are mixed case).
            if (Directory.Exists(folder) &&
                Directory.EnumerateFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary><paramref name="config"/> with scheme <paramref name="name"/>'s colors; null if there's no such scheme.</summary>
    public Config? Apply(Config config, string name) => Find(name) is { } file ? ApplyFile(config, file) : null;

    /// <summary>Applies a scheme file's colors (the rest it may set, selection or cursor text, isn't used yet).</summary>
    public static Config ApplyFile(Config config, string file)
    {
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (line.StartsWith('#') || eq < 1)
            {
                continue;
            }

            var key = line[..eq].Trim();
            if (key is "background" or "foreground" or "cursor-color" or "palette" &&
                config.Apply(key, line[(eq + 1)..].Trim()) is { } next)
            {
                config = next;
            }
        }

        return config;
    }
}
