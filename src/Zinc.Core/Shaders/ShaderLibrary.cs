namespace Zinc.Shaders;

/// <summary>
/// A shader the picker offers: its name, file, how config.toml would name it (<see cref="Written"/>), a line about it,
/// and whether it moves on its own (uses time: redraws while idle).
/// </summary>
public sealed record ShaderEntry(string Name, string File, string Written, string? Description, bool UsesTime);

/// <summary>
/// A group of shaders in the picker: Zinc's, yours (<c>shaders\</c> next to the config), or a folder you added
/// (<see cref="Folder"/>; <see cref="Missing"/> when it's gone).
/// </summary>
public sealed record ShaderSection(string Title, string? Folder, IReadOnlyList<ShaderEntry> Shaders, bool Missing = false);

/// <summary>Every shader the picker can offer, from Zinc, your shaders folder and the folders you added (read from disk).</summary>
public static class ShaderLibrary
{
    public const string ZincTitle = "Comes with Zinc", YoursTitle = "Your shaders";

    // Bigger than any shader: a file this size isn't one (and isn't read whole to describe it).
    private const long MaxSize = 512 * 1024;

    public static IReadOnlyList<ShaderSection> List(string configFile, IReadOnlyList<string> folders)
    {
        var sections = new List<ShaderSection>();
        var zinc = ShaderPaths.Bundled()
            .Select(s => Entry(Path.Combine(ShaderPaths.BundledFolder, s.Name + ".glsl"), s.Name, s.Description))
            .OfType<ShaderEntry>().ToList();
        if (zinc.Count > 0)
        {
            sections.Add(new ShaderSection(ZincTitle, null, zinc));
        }

        // Yours, by name (a name finds yours before Zinc's of the same name).
        var yoursFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configFile)) ?? ".", "shaders");
        var yours = Files(yoursFolder).Select(f => Entry(f, Path.GetFileNameWithoutExtension(f), null)).OfType<ShaderEntry>().ToList();
        if (yours.Count > 0)
        {
            sections.Add(new ShaderSection(YoursTitle, yoursFolder, yours));
        }

        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder))
            {
                sections.Add(new ShaderSection(Path.GetFileName(folder.TrimEnd('\\', '/')), folder, [], Missing: true));
                continue;
            }

            var shaders = Files(folder).Select(f => Entry(f, null, null, configFile)).OfType<ShaderEntry>().ToList();
            sections.Add(new ShaderSection(Path.GetFileName(folder.TrimEnd('\\', '/')), folder, shaders));
        }

        return sections;
    }

    private static IEnumerable<string> Files(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".glsl" or ".frag")
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <param name="name">How config.toml names it (Zinc's and yours: by name), or null for its path.</param>
    private static ShaderEntry? Entry(string file, string? name, string? description, string? configFile = null)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > MaxSize)
            {
                return null;
            }

            var text = File.ReadAllText(file);
            return new ShaderEntry(Path.GetFileNameWithoutExtension(file), file,
                name ?? ShaderPaths.ForConfig(file, configFile!), description ?? Describe(text), ShaderSource.UsesTime(text));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A line about a shader from its own text: the first comment near the top that reads like a sentence (not a
    /// separator, a license line or an attribution URL alone); null if there's none.
    /// </summary>
    public static string? Describe(string text)
    {
        foreach (var raw in text.Split('\n').Take(15))
        {
            var line = raw.Trim();
            if (line.StartsWith("//"))
            {
                line = line.TrimStart('/').Trim();
            }
            else if (line.StartsWith("/*") || line.StartsWith('*'))
            {
                line = line.TrimStart('/', '*').TrimEnd('/', '*').Trim();
            }
            else if (line.Length > 0)
            {
                break; // code: no description above it
            }
            else
            {
                continue;
            }

            if (line.Count(char.IsLetter) < 4 || line.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("license", StringComparison.OrdinalIgnoreCase) || line.Contains("copyright", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line.Length > 120 ? line[..117].TrimEnd() + "…" : line;
        }

        return null;
    }
}
