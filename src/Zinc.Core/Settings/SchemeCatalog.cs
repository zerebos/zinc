using System.Collections.Concurrent;
using Zinc.Terminal;

namespace Zinc.Settings;

/// <summary>Which schemes the color schemes page lists: every one, the dark or light ones, or yours.</summary>
public enum SchemeKind
{
    All,
    Dark,
    Light,
    Yours,
}

/// <summary>
/// A scheme in the library: its name, whether it's dark (its background, as <see cref="Config.IsDark"/> judges), whether it's
/// yours (a file in your themes folder) and whether yours stands in for one Zinc comes with (a customized copy).
/// </summary>
public sealed record SchemeEntry(string Name, string File, bool IsDark, bool Yours, bool ReplacesBundled);

/// <summary>Every scheme for the color schemes page, read from disk, and the page's filters.</summary>
public static class SchemeCatalog
{
    // Each scheme file's colors, by file and its last write: ~650 files are read once a run (by List, off the UI thread),
    // then again only when edited.
    private static readonly ConcurrentDictionary<string, (DateTime Written, TerminalTheme Theme)> Themes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every scheme in <paramref name="themes"/>, sorted by name (yours over Zinc's of the same name).</summary>
    public static IReadOnlyList<SchemeEntry> List(ThemeLibrary themes)
    {
        var yours = Files(themes.UserFolder);
        var bundled = Files(themes.BundledFolder);
        var entries = new List<SchemeEntry>();
        foreach (var name in themes.Names())
        {
            var own = yours.TryGetValue(name, out var file);
            if (!own && !bundled.TryGetValue(name, out file))
            {
                continue;
            }

            entries.Add(new SchemeEntry(name, file!, IsDark(file!), own, own && bundled.ContainsKey(name)));
        }

        return entries;
    }

    /// <summary>The entries of <paramref name="kind"/> whose name has <paramref name="query"/> in it (any case).</summary>
    public static IReadOnlyList<SchemeEntry> Filter(IReadOnlyList<SchemeEntry> all, SchemeKind kind, string query)
    {
        query = query.Trim();
        return [.. all.Where(e => kind switch
            {
                SchemeKind.Dark => e.IsDark,
                SchemeKind.Light => !e.IsDark,
                SchemeKind.Yours => e.Yours,
                _ => true,
            } && (query.Length == 0 || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <summary>Whether scheme file <paramref name="file"/> has a dark background (no background: Zinc's own, dark).</summary>
    public static bool IsDark(string file) => new Config { Theme = Theme(file) }.IsDark;

    /// <summary>Scheme file <paramref name="file"/>'s colors (read once, and again after it changes); unreadable: Zinc's own.</summary>
    public static TerminalTheme Theme(string file)
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(file);
            if (Themes.TryGetValue(file, out var known) && known.Written == written)
            {
                return known.Theme;
            }

            var theme = ThemeLibrary.ApplyFile(new Config { Theme = TerminalTheme.SchemeBase }, file).Theme;
            Themes[file] = (written, theme);
            return theme;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TerminalTheme.SchemeBase;
        }
    }

    // A folder's files by name; none if it's missing or can't be read.
    private static Dictionary<string, string> Files(string folder)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    files.TryAdd(Path.GetFileName(file), file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"themes: couldn't list {folder}: {ex.Message}");
        }

        return files;
    }
}
