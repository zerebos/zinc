namespace Zinc;

/// <summary>
/// Named color schemes: the user's (a themes folder next to the config file) over the ones that
/// ship with Zinc. Files use Ghostty's theme format (as iTerm2-Color-Schemes publishes it):
/// <c>key = value</c> lines, of which the colors apply.
/// </summary>
public sealed class ThemeLibrary(string userFolder, string bundledFolder)
{
    /// <summary>The library for a config file: its folder's <c>themes</c>, then <paramref name="bundledFolder"/>.</summary>
    public static ThemeLibrary For(string configPath, string bundledFolder) =>
        new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", "themes"), bundledFolder);

    public string UserFolder => userFolder;

    /// <summary>The schemes that come with Zinc.</summary>
    public string BundledFolder => bundledFolder;

    /// <summary>
    /// A theme that follows Windows' light/dark mode, written as Ghostty does:
    /// <c>"light:Catppuccin Latte,dark:Catppuccin Mocha"</c> (either order; a missing side uses
    /// the other). False for a plain scheme name.
    /// </summary>
    public static bool TryParsePair(string value, out string light, out string dark)
    {
        light = dark = "";
        string? l = null, d = null;
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':');
            var side = colon > 0 ? part[..colon].Trim().ToLowerInvariant() : "";
            if (side == "light")
            {
                l = part[(colon + 1)..].Trim();
            }
            else if (side == "dark")
            {
                d = part[(colon + 1)..].Trim();
            }
            else
            {
                return false; // a scheme's own name (they may contain neither ':' sides nor commas)
            }
        }

        if (string.IsNullOrEmpty(l) && string.IsNullOrEmpty(d))
        {
            return false;
        }

        light = string.IsNullOrEmpty(l) ? d! : l;
        dark = string.IsNullOrEmpty(d) ? l! : d;
        return true;
    }

    /// <summary>
    /// A light scheme to pair with <paramref name="dark"/>: its obvious counterpart where the
    /// collection has one ("Catppuccin Mocha" → "Catppuccin Latte", "Solarized Dark" → "Solarized
    /// Light", "Tokyo Night" → "Tokyo Night Day", "Rose Pine" → "Rose Pine Dawn", "Lit Cell" → "Lit Cell
    /// Light"); for Zinc's own colors (null), Lit Cell Light; else Catppuccin Latte.
    /// </summary>
    public static string? LightCounterpart(string? dark, IReadOnlyList<string> names)
    {
        string? Named(string name) => names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (dark != null)
        {
            foreach (var (from, to) in new[] { ("Mocha", "Latte"), ("Macchiato", "Latte"), ("Frappe", "Latte"), ("Frappé", "Latte"),
                         ("Dark", "Light"), ("Night", "Day"), ("Moon", "Dawn"), ("Storm", "Day"), ("Black", "White") })
            {
                if (dark.Contains(from, StringComparison.OrdinalIgnoreCase) &&
                    Named(dark.Replace(from, to, StringComparison.OrdinalIgnoreCase)) is { } swapped)
                {
                    return swapped;
                }
            }

            foreach (var suffix in new[] { " Light", " Day", " Dawn" })
            {
                if (Named(dark + suffix) is { } added)
                {
                    return added;
                }
            }
        }

        return (dark == null ? Named("Lit Cell Light") : null) ?? Named("Catppuccin Latte") ?? names.FirstOrDefault();
    }

    /// <summary>The scheme in force for <paramref name="value"/> (a name, or a light/dark pair) in <paramref name="scheme"/> mode.</summary>
    public static string Resolve(string value, Zinc.Ghostty.ColorScheme scheme) =>
        TryParsePair(value, out var light, out var dark) ? (scheme == Zinc.Ghostty.ColorScheme.Light ? light : dark) : value;

    private IEnumerable<string> Folders => [userFolder, bundledFolder];

    /// <summary>Every scheme's name, sorted; a user's scheme hides a bundled one of the same name.</summary>
    public IReadOnlyList<string> Names() =>
        Folders.SelectMany(Files)
            .Select(f => Path.GetFileName(f))
            .DistinctBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>A folder's files; none if it's missing or can't be read (a scheme folder isn't worth failing over).</summary>
    private static IReadOnlyList<string> Files(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"themes: couldn't list {folder}: {ex.Message}");
            return [];
        }
    }

    /// <summary>Scheme <paramref name="name"/>'s file in one folder, or null.</summary>
    private static string? In(string folder, string name)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        // The file's own spelling (the file system would find "dracula" as "Dracula" too, spelled as asked).
        return File.Exists(Path.Combine(folder, name))
            ? Files(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>Whether scheme <paramref name="name"/> is the user's (a file in their themes folder, maybe a copy of a bundled one).</summary>
    public bool IsUsers(string name) => In(userFolder, name) != null;

    /// <summary>Whether the user's scheme <paramref name="name"/> stands in for a bundled one of that name.</summary>
    public bool ReplacesBundled(string name) => IsUsers(name) && In(bundledFolder, name) != null;

    /// <summary>
    /// The file to edit for scheme <paramref name="name"/>: the user's, or else a copy of the bundled
    /// one made in their themes folder (which then stands in for it everywhere it's used); null if
    /// there's no such scheme.
    /// </summary>
    public string? Customize(string name)
    {
        if (In(userFolder, name) is { } own)
        {
            return own;
        }

        if (In(bundledFolder, name) is not { } bundled)
        {
            return null;
        }

        Directory.CreateDirectory(userFolder);
        var copy = Path.Combine(userFolder, Path.GetFileName(bundled));
        File.Copy(bundled, copy);
        return copy;
    }

    /// <summary>Why <paramref name="name"/> can't be a new scheme's name ("Name it", "taken", a character a file name can't have), or null.</summary>
    public string? NameProblem(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return "Give it a name.";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':') || name.Contains(','))
        {
            return "A name can't have \\ / : * ? \" < > | or a comma.";
        }

        return Find(name) != null ? $"There's a scheme called {name} already." : null;
    }

    /// <summary>
    /// A new scheme of the user's, <paramref name="name"/>, starting as a copy of scheme
    /// <paramref name="from"/>; its file, or null if the name won't do or there's no such scheme.
    /// </summary>
    public string? Create(string name, string from)
    {
        name = name.Trim();
        if (NameProblem(name) != null || Find(from) is not { } source)
        {
            return null;
        }

        Directory.CreateDirectory(userFolder);
        var file = Path.Combine(userFolder, name);
        File.WriteAllText(file, $"# {name}, made from {from}{Environment.NewLine}" + File.ReadAllText(source));
        return file;
    }

    /// <summary>Deletes one of the user's own schemes (not a copy of a bundled one: see <see cref="BackToOriginal"/>).</summary>
    public void Delete(string name)
    {
        if (!ReplacesBundled(name) && In(userFolder, name) is { } own)
        {
            File.Delete(own);
        }
    }

    /// <summary>Removes the user's copy of a bundled scheme, so the bundled one is used again.</summary>
    public void BackToOriginal(string name)
    {
        if (ReplacesBundled(name) && In(userFolder, name) is { } own)
        {
            File.Delete(own);
        }
    }

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
            if (Files(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary><paramref name="config"/> with scheme <paramref name="name"/>'s colors; null if there's no such scheme.</summary>
    public Config? Apply(Config config, string name)
    {
        if (Find(name) is not { } file)
        {
            return null;
        }

        try
        {
            // One scheme, whatever came before: its colors replace the theme's, not add to them.
            return ApplyFile(config with { Theme = Zinc.Terminal.TerminalTheme.SchemeBase }, file) with { PairedDark = null };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"themes: couldn't read {file}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Applies a scheme file's colors.</summary>
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
            if (key is "background" or "foreground" or "cursor-color" or "cursor-text" or "palette" or "selection-background" or "selection-foreground" &&
                config.Apply(key, line[(eq + 1)..].Trim()) is { } next)
            {
                config = next;
            }
        }

        return config;
    }
}
