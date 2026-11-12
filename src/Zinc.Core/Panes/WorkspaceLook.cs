namespace Zinc.Panes;

/// <summary>
/// Icons a workspace can have instead of its active tab's profile icon, from a curated set of Segoe
/// Fluent Icons glyphs (Windows 11's own icon font). Saved by name.
/// </summary>
public static class WorkspaceIcons
{
    public static IReadOnlyList<(string Name, char Glyph)> All { get; } =
    [
        ("folder", '\uE8B7'), ("home", '\uE80F'), ("code", '\uE943'), ("terminal", '\uE756'),
        ("settings", '\uE713'), ("bug", '\uEBE8'), ("package", '\uE7B8'), ("component", '\uE950'),
        ("document", '\uE8A5'), ("library", '\uE8F1'), ("globe", '\uE774'), ("network", '\uE968'),
        ("cloud", '\uE753'), ("mail", '\uE715'), ("people", '\uE716'), ("person", '\uE77B'),
        ("work", '\uE821'), ("chart", '\uE9D2'), ("lightning", '\uE945'), ("star", '\uE734'),
        ("heart", '\uEB51'), ("flag", '\uE7C1'), ("pin", '\uE707'), ("lock", '\uE72E'),
        ("game", '\uE7FC'), ("music", '\uEC4F'), ("camera", '\uE722'), ("calculator", '\uE8EF'),
    ];

    /// <summary>The glyph for a saved icon name, or null if there's no such icon (any more).</summary>
    public static char? Glyph(string? name) =>
        All.FirstOrDefault(i => i.Name == name) is { Name: not null } icon ? icon.Glyph : null;
}

/// <summary>Automatic workspace colors. Config: <c>[window] workspace-colors = off | folder | cycle</c>.</summary>
public enum WorkspaceColorMode
{
    Off,

    /// <summary>From the workspace's folder, so the same folder always gets the same color.</summary>
    Folder,

    /// <summary>Each new workspace takes the next color.</summary>
    Cycle,
}

/// <summary>The colors a workspace can have (the workspace menu's, the page's, the automatic ones).</summary>
public static class WorkspaceColors
{
    public static IReadOnlyList<(string Name, uint Rgb)> All { get; } =
    [
        ("Red", 0xC0392B), ("Orange", 0xD35400), ("Yellow", 0xB7950B), ("Green", 0x229954),
        ("Teal", 0x148F77), ("Blue", 0x2471A3), ("Purple", 0x7D3C98), ("Pink", 0xB03A78),
    ];

    /// <summary>
    /// The index in <see cref="All"/> of the color for a folder, which is always the same one for the same path (FNV-1a
    /// over the path, with case and trailing separators ignored, so C:\Src and c:\src\ agree).
    /// </summary>
    public static int ForFolder(string path)
    {
        var hash = 2166136261u;
        foreach (var c in path.TrimEnd('/', '\\').ToLowerInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash % (uint)All.Count);
    }
}
