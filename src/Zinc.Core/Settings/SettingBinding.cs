using System.Globalization;
using Zinc.Ghostty;

namespace Zinc.Settings;

/// <summary>
/// How one setting's TOML value becomes part of a <see cref="Config"/>, and the TOML value that amounts to what
/// <c>new Config()</c> already holds. Made by the <see cref="Bind"/> methods.
/// </summary>
public sealed class SettingBinding
{
    private static readonly Config Defaults = new();

    private readonly Func<SettingDef, Config, object, string, Config?> _read;
    private readonly Func<SettingDef, Config, object?>? _default;

    internal SettingBinding(Func<SettingDef, Config, object, string, Config?> read, Func<SettingDef, Config, object?>? @default)
    {
        _read = read;
        _default = @default;
    }

    /// <summary>
    /// <paramref name="config"/> with <paramref name="value"/> applied, or null when the value doesn't fit.
    /// <paramref name="file"/> is the config file that holds it, which relative paths are resolved from.
    /// </summary>
    public Config? Read(SettingDef setting, Config config, object value, string file = "config.toml") =>
        _read(setting, config, value, file);

    /// <summary>
    /// The TOML value that matches <c>new Config()</c>, or null when none is written. A choice setting's default is the
    /// choice that leaves an empty config as it is.
    /// </summary>
    public object? DefaultFor(SettingDef setting)
    {
        if (setting.Kind == SettingKind.Choice || (setting.Choices.Count > 0 && _default == null))
        {
            return setting.Choices.FirstOrDefault(c => c.Value != null && Read(setting, Defaults, c.Value) == Defaults)?.Value;
        }

        return _default?.Invoke(setting, Defaults);
    }
}

/// <summary>
/// Bindings for the common kinds of setting. Each reads the TOML value Tomlyn gives (string, long, double, bool).
/// </summary>
public static class Bind
{
    /// <summary>true or false.</summary>
    public static SettingBinding Bool(Func<Config, bool> get, Func<Config, bool, Config> set) =>
        new((_, config, value, _) => value is bool b ? set(config, b) : null, (_, config) => get(config));

    /// <summary>
    /// A number in the setting's <see cref="SettingDef.Minimum"/> to <see cref="SettingDef.Maximum"/>. A number outside
    /// that range is refused, or moved to the nearer end when <paramref name="clamp"/> is set (as Ghostty does for some
    /// settings). With <paramref name="whole"/>, a number with a fraction is refused too.
    /// </summary>
    public static SettingBinding Number(Func<Config, double> get, Func<Config, double, Config> set, bool clamp = false, bool whole = false) =>
        new((setting, config, value, _) => AsNumber(value) switch
        {
            { } n when whole && n != Math.Floor(n) => null,
            { } n when clamp => set(config, Math.Clamp(n, setting.Minimum, setting.Maximum)),
            { } n when n >= setting.Minimum && n <= setting.Maximum => set(config, n),
            _ => null,
        }, (_, config) => Tidy(get(config)));

    /// <summary>Any text except an empty string. An empty default means none is written.</summary>
    public static SettingBinding Text(Func<Config, string> get, Func<Config, string, Config> set) =>
        new((_, config, value, _) => value is string { Length: > 0 } s ? set(config, s) : null,
            (_, config) => get(config) is { Length: > 0 } s ? s : null);

    /// <summary>"#rrggbb" or "rrggbb". No default is written, so the color comes from the scheme or the app.</summary>
    public static SettingBinding Color(Func<Config, GhosttyColorRgb, Config> set) =>
        new((_, config, value, _) => value is string s && Config.TryColor(s, out var color) ? set(config, color) : null, null);

    /// <summary>
    /// A kebab-case name of <typeparamref name="T"/> ("tab-accent" for TabAccent). The default is the choice that
    /// matches <c>new Config()</c>.
    /// </summary>
    public static SettingBinding Enum<T>(Func<Config, T, Config> set) where T : struct, System.Enum =>
        new((_, config, value, _) => value is string s && Config.TryMode<T>(s, out var mode) ? set(config, mode) : null, null);

    /// <summary>
    /// Any other kind of setting. <paramref name="read"/> takes the TOML value as it is, and <paramref name="default"/>
    /// gives the TOML value that matches a config. Nothing is written when there's no <paramref name="default"/> or it
    /// returns null.
    /// </summary>
    public static SettingBinding Custom(Func<Config, object, Config?> read, Func<Config, object?>? @default = null) =>
        Custom((_, config, value) => read(config, value), @default);

    /// <summary>
    /// The same, for a <paramref name="read"/> that needs the setting itself, such as its
    /// <see cref="SettingDef.Minimum"/> and <see cref="SettingDef.Maximum"/>.
    /// </summary>
    public static SettingBinding Custom(Func<SettingDef, Config, object, Config?> read, Func<Config, object?>? @default = null) =>
        new((setting, config, value, _) => read(setting, config, value), @default == null ? null : (_, config) => @default(config));

    /// <summary>
    /// One path or a list of them, with relative ones resolved by <paramref name="resolve"/> from the config file that
    /// names them. None is written by default.
    /// </summary>
    public static SettingBinding Paths(Func<string, string, string> resolve, Func<Config, ValueList<string>, Config> set) =>
        new((_, config, value, file) => PathList(value, file, resolve) is { } paths ? set(config, paths) : null, null);

    /// <summary>
    /// One path or a list of them from config.toml, blank ones left out and the rest resolved from
    /// <paramref name="file"/>. Null when the value is neither a string nor a list of strings.
    /// </summary>
    public static ValueList<string>? PathList(object value, string file, Func<string, string, string> resolve)
    {
        IEnumerable<string>? written = value switch
        {
            string one => [one],
            List<object> list when list.All(x => x is string) => list.Cast<string>(),
            _ => null,
        };
        return written == null ? null : new(written.Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => resolve(p, file)));
    }

    /// <summary>A number as a double, because Tomlyn gives a long or a double and code may pass an int or a float.</summary>
    public static double? AsNumber(object value) => value switch
    {
        long or int or double or float => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>A string, or a number as invariant text (for parsers that take either).</summary>
    public static string? AsText(object value) => value switch
    {
        string s => s,
        long or int or double or float => Convert.ToString(value, CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>
    /// A number in the form config.toml writes it, with whole numbers as integers and the float noise of a conversion
    /// rounded off.
    /// </summary>
    internal static object Tidy(double value)
    {
        var rounded = Math.Round(value, 9);
        return rounded == Math.Floor(rounded) && Math.Abs(rounded) < long.MaxValue ? (object)(long)rounded : rounded;
    }
}
