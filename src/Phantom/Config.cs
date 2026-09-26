using System.Diagnostics;
using System.Globalization;

namespace Phantom;

/// <summary>
/// User configuration from <c>%APPDATA%\Phantom\config</c>, in Ghostty's syntax:
/// one <c>key = value</c> per line, <c>#</c> comments, later lines win.
/// </summary>
public sealed record Config
{
    public const float DefaultFontSize = 14;

    /// <summary>Primary font family; glyphs it lacks fall back through DirectWrite.</summary>
    public string FontFamily { get; init; } = "Cascadia Mono";

    /// <summary>Font size in DIPs (device-independent pixels).</summary>
    public float FontSize { get; init; } = DefaultFontSize;

    /// <summary>Command line to run; null means pick a default shell.</summary>
    public string? Command { get; init; }

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
            switch (key)
            {
                case "font-family" when value.Length > 0:
                    config = config with { FontFamily = value };
                    break;
                case "font-size" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0:
                    config = config with { FontSize = size };
                    break;
                case "command" when value.Length > 0:
                    config = config with { Command = value };
                    break;
                default:
                    Debug.WriteLine($"config:{lineNumber}: ignored '{key}'");
                    break;
            }
        }

        return config;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
