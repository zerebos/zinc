using System.Text.RegularExpressions;

namespace Zinc.Settings;

/// <summary>
/// Editing a color scheme file (Ghostty's format: <c>key = value</c> lines, the palette as
/// <c>palette = N=#rrggbb</c>), one color at a time, leaving every other line as it was. Keys:
/// <c>background</c>, <c>foreground</c>, <c>cursor-color</c>, <c>cursor-text</c>,
/// <c>selection-background</c>, <c>selection-foreground</c>, and <c>palette.N</c> for palette entry N.
/// </summary>
public static partial class SchemeFile
{
    /// <summary>The colors a scheme sets, as the editor shows them (the palette's 16 first).</summary>
    public static readonly string[] Keys =
    [
        "background", "foreground", "cursor-color", "cursor-text", "selection-background", "selection-foreground",
        .. Enumerable.Range(0, 16).Select(i => $"palette.{i}"),
    ];

    /// <summary>Key <paramref name="key"/>'s value in <paramref name="text"/> (the last one, as reading applies them), or null.</summary>
    public static string? Get(string text, string key)
    {
        string? value = null;
        foreach (var line in text.Split('\n'))
        {
            if (Match(line, key) is { } v)
            {
                value = v;
            }
        }

        return value;
    }

    /// <summary>
    /// <paramref name="text"/> with <paramref name="key"/> set to <paramref name="value"/> (or removed when
    /// it's null). The key's line is edited in place, or else added at the end. Line endings are kept.
    /// </summary>
    public static string Set(string text, string key, string? value)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var trailing = lines.Count > 0 && lines[^1].Length == 0;
        if (trailing)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var line = value == null ? null : Line(key, value);
        var found = false;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (Match(lines[i], key) == null)
            {
                continue;
            }

            if (found || line == null)
            {
                lines.RemoveAt(i); // a duplicate (the last one wins when read), or removed
            }
            else
            {
                lines[i] = line;
            }

            found = true;
        }

        if (!found && line != null)
        {
            lines.Add(line);
        }

        return string.Join(newline, lines) + newline;
    }

    private static string Line(string key, string value) =>
        PaletteIndex(key) is { } n ? $"palette = {n}={value}" : $"{key} = {value}";

    private static int? PaletteIndex(string key) =>
        key.StartsWith("palette.", StringComparison.Ordinal) && int.TryParse(key["palette.".Length..], out var n) ? n : null;

    /// <summary>The value <paramref name="line"/> gives <paramref name="key"/>, or null if it doesn't set it.</summary>
    private static string? Match(string line, string key)
    {
        var m = Assignment().Match(line);
        if (!m.Success)
        {
            return null;
        }

        var (name, value) = (m.Groups[1].Value, m.Groups[2].Value.Trim());
        if (PaletteIndex(key) is { } n)
        {
            var entry = PaletteEntry().Match(value);
            return name == "palette" && entry.Success && int.Parse(entry.Groups[1].Value) == n ? entry.Groups[2].Value.Trim() : null;
        }

        return name == key ? value : null;
    }

    [GeneratedRegex(@"^\s*([a-z-]+)\s*=\s*(.*?)\s*$")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"^(\d+)\s*=\s*(.*)$")]
    private static partial Regex PaletteEntry();
}
