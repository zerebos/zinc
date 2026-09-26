using System.Diagnostics;
using System.Globalization;
using Phantom.Ghostty;
using Phantom.Terminal;

namespace Phantom;

/// <summary>
/// User configuration from <c>%APPDATA%\Phantom\config</c>, in Ghostty's syntax:
/// one <c>key = value</c> per line, <c>#</c> comments, later lines win. Keys and
/// units follow Ghostty so lines can be copied from a Ghostty config.
/// </summary>
public sealed record Config
{
    /// <summary>Default font size in points (≈14 DIPs).</summary>
    public const float DefaultFontSize = 10.5f;

    /// <summary>Primary font family; glyphs it lacks fall back through DirectWrite.</summary>
    public string FontFamily { get; init; } = "Cascadia Mono";

    /// <summary>Font size in points, like Ghostty and Windows Terminal.</summary>
    public float FontSize { get; init; } = DefaultFontSize;

    /// <summary>Font size in DIPs (96 per inch; a point is 1/72 inch).</summary>
    public float FontSizeDip => FontSize * 96f / 72f;

    /// <summary>Command line to run; null means pick a default shell.</summary>
    public string? Command { get; init; }

    public TerminalTheme Theme { get; init; } = TerminalTheme.Default;

    /// <summary>Padding around the grid, in DIPs.</summary>
    public float PaddingLeft { get; init; } = 8;
    public float PaddingRight { get; init; } = 8;
    public float PaddingTop { get; init; } = 8;
    public float PaddingBottom { get; init; } = 8;

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Phantom", "config");

    public static Config Load(string? path = null)
    {
        path ??= Environment.GetEnvironmentVariable("PHANTOM_CONFIG") is { Length: > 0 } fromEnv ? fromEnv : DefaultPath;
        var config = new Config();
        if (!File.Exists(path))
        {
            return config;
        }

        var palette = new Dictionary<int, GhosttyColorRgb>();
        foreach (var (raw, lineNumber) in File.ReadLines(path).Select((l, i) => (l, i + 1)))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                Debug.WriteLine($"config:{lineNumber}: expected 'key = value'");
                continue;
            }

            var key = line[..eq].Trim();
            var value = Unquote(line[(eq + 1)..].Trim());
            var theme = config.Theme;
            var ok = true;
            switch (key)
            {
                case "font-family" when value.Length > 0:
                    config = config with { FontFamily = value };
                    break;
                case "font-size" when TryFloat(value, out var size) && size > 0:
                    config = config with { FontSize = size };
                    break;
                case "command" when value.Length > 0:
                    config = config with { Command = value };
                    break;
                case "background" when TryColor(value, out var c):
                    config = config with { Theme = theme with { Background = c } };
                    break;
                case "foreground" when TryColor(value, out var c):
                    config = config with { Theme = theme with { Foreground = c } };
                    break;
                case "cursor-color" when TryColor(value, out var c):
                    config = config with { Theme = theme with { Cursor = c } };
                    break;
                case "palette" when TryPaletteEntry(value, out var index, out var c):
                    palette[index] = c;
                    break;
                case "window-padding-x" when TryPadding(value, out var left, out var right):
                    config = config with { PaddingLeft = left, PaddingRight = right };
                    break;
                case "window-padding-y" when TryPadding(value, out var top, out var bottom):
                    config = config with { PaddingTop = top, PaddingBottom = bottom };
                    break;
                default:
                    ok = false;
                    break;
            }

            if (!ok)
            {
                Debug.WriteLine($"config:{lineNumber}: ignored '{line}'");
            }
        }

        return palette.Count == 0 ? config : config with { Theme = config.Theme with { Palette = palette } };
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static bool TryFloat(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Parses #rrggbb or rrggbb.</summary>
    public static bool TryColor(string s, out GhosttyColorRgb color)
    {
        color = default;
        s = s.TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = GhosttyColorRgb.FromHex(rgb);
        return true;
    }

    /// <summary>Parses Ghostty's <c>N=#rrggbb</c> palette entry.</summary>
    private static bool TryPaletteEntry(string s, out int index, out GhosttyColorRgb color)
    {
        color = default;
        index = -1;
        var eq = s.IndexOf('=');
        return eq > 0 &&
               int.TryParse(s[..eq].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) &&
               index is >= 0 and <= 255 &&
               TryColor(s[(eq + 1)..].Trim(), out color);
    }

    /// <summary>Parses Ghostty padding in points: <c>N</c> (both sides) or <c>A,B</c>; returns DIPs.</summary>
    private static bool TryPadding(string s, out float first, out float second)
    {
        first = second = 0;
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !TryFloat(parts[0], out first))
        {
            return false;
        }

        second = first;
        if ((parts.Length == 2 && !TryFloat(parts[1], out second)) || first < 0 || second < 0)
        {
            return false;
        }

        first *= 96f / 72f;
        second *= 96f / 72f;
        return true;
    }
}
