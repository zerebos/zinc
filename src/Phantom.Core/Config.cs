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

    /// <summary>Loads the global settings (see <see cref="ConfigFile"/> for profiles).</summary>
    public static Config Load(string? path = null) => ConfigFile.Load(path).Global;

    /// <summary>Profile tab icon (image path); only meaningful inside a profile.</summary>
    public string? Icon { get; init; }

    /// <summary>Starting directory for the command; null means the user's home.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Applies one <c>key = value</c> setting. Returns null when the key is unknown
    /// or the value invalid, leaving the caller to report it.
    /// </summary>
    public Config? Apply(string key, string value)
    {
        var theme = Theme;
        return key switch
        {
            "font-family" when value.Length > 0 => this with { FontFamily = value },
            "font-size" when TryFloat(value, out var size) && size > 0 => this with { FontSize = size },
            "command" when value.Length > 0 => this with { Command = value },
            "working-directory" when value.Length > 0 => this with { WorkingDirectory = value },
            "icon" when value.Length > 0 => this with { Icon = value },
            "background" when TryColor(value, out var c) => this with { Theme = theme with { Background = c } },
            "foreground" when TryColor(value, out var c) => this with { Theme = theme with { Foreground = c } },
            "cursor-color" when TryColor(value, out var c) => this with { Theme = theme with { Cursor = c } },
            "palette" when TryPaletteEntry(value, out var index, out var c) =>
                this with { Theme = theme with { Palette = new Dictionary<int, GhosttyColorRgb>(theme.Palette) { [index] = c } } },
            "window-padding-x" when TryPadding(value, out var left, out var right) =>
                this with { PaddingLeft = left, PaddingRight = right },
            "window-padding-y" when TryPadding(value, out var top, out var bottom) =>
                this with { PaddingTop = top, PaddingBottom = bottom },
            _ => null,
        };
    }

    /// <summary>
    /// The directory to start in: <see cref="WorkingDirectory"/> with <c>~</c> / <c>home</c>
    /// expanded, or the user's home.
    /// </summary>
    public string ResolveWorkingDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return WorkingDirectory switch
        {
            null or "~" or "home" => home,
            var dir when dir.StartsWith("~/") || dir.StartsWith("~\\") => Path.Combine(home, dir[2..]),
            var dir => Environment.ExpandEnvironmentVariables(dir),
        };
    }

    internal static string Unquote(string value) =>
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
