using System.Globalization;

namespace Zinc.Settings;

/// <summary>How a setting is edited in the settings window.</summary>
public enum SettingKind
{
    /// <summary>On or off.</summary>
    Toggle,

    /// <summary>One of <see cref="SettingDef.Choices"/>.</summary>
    Choice,

    /// <summary>A number, in <see cref="SettingDef.Unit"/>s.</summary>
    Number,

    /// <summary>Free text (a hotkey, "50%", "2m"); numbers are written as numbers when <see cref="SettingDef.NumbersAsNumbers"/>.</summary>
    Text,

    /// <summary>"#rrggbb", or nothing (the scheme's).</summary>
    Color,

    /// <summary>A monospace font family.</summary>
    Font,

    /// <summary>A color scheme by name, or a light/dark pair.</summary>
    Scheme,

    /// <summary>An ordered list of files (custom shaders): written as a list of paths, empty for none.</summary>
    Files,
}

/// <summary>
/// A choice's label (short: it's a menu item), the value written for it (null: the default, written
/// as nothing), and what it means in more words, shown under the setting's name while it's chosen.
/// </summary>
public sealed record SettingChoice(string Label, object? Value, string? Detail = null);

/// <summary>A section of the settings window.</summary>
public sealed record SettingsSection(string Id, string Title, string Glyph);

/// <summary>
/// One setting as the settings window shows it: where it goes, what it's called, how it's edited, and how the reader
/// applies it (<see cref="Binding"/>). <see cref="Key"/> is its config.toml path ("font.size"). Only settings the
/// reader handles with its own code have no binding (<c>colors.theme</c>, a profile's fields, <c>[discover]</c>). Their
/// default is <paramref name="DeclaredDefault"/>, which a bound setting leaves out.
/// </summary>
public sealed record SettingDef(string Section, string Group, string Key, string Label, string Description, SettingKind Kind,
    object? DeclaredDefault = null)
{
    /// <summary>How the reader applies this key's value to a <see cref="Config"/>.</summary>
    public SettingBinding? Binding { get; init; }

    /// <summary>
    /// What a valid value looks like, for the reader's warning when one isn't ("a file path or a list of them").
    /// </summary>
    public string? Expected { get; init; }

    /// <summary>
    /// The value an empty config amounts to, or null when none is written and the app's own behavior applies.
    /// </summary>
    public object? Default => Binding != null ? Binding.DefaultFor(this) : DeclaredDefault;

    public IReadOnlyList<SettingChoice> Choices { get; init; } = [];

    /// <summary>Shown after a number, or as a Text setting's hint.</summary>
    public string? Unit { get; init; }

    public double Minimum { get; init; }

    public double Maximum { get; init; } = double.MaxValue;

    public double Step { get; init; } = 1;

    /// <summary>For Text: shown while empty.</summary>
    public string? Placeholder { get; init; }

    /// <summary>A color setting's "no color" choice, if not "Use the scheme's color".</summary>
    public string? NullLabel { get; init; }

    /// <summary>For Text: "10" is written as the number 10, "30s" as a string.</summary>
    public bool NumbersAsNumbers { get; init; }

    /// <summary>
    /// Read and tested, but kept out of the settings window (and the README) until the app does what it says
    /// ZINC_TEST_UNFINISHED_SETTINGS=1 shows them.
    /// </summary>
    public bool Unfinished { get; init; }

    /// <summary>The key's path in config.toml.</summary>
    public IReadOnlyList<string> Path => Key.Split('.');

    /// <summary>Whether a profile can have its own value (the [font], [colors], [terminal] and [shaders] groups).</summary>
    public bool ProfileOverridable => Path[0] is "font" or "colors" or "terminal" or "shaders";
}

/// <summary>
/// Settings folded into one card: under <paramref name="Lead"/> (its card is the fold's header), or,
/// without one, under <paramref name="Title"/> and <paramref name="Description"/>.
/// </summary>
public sealed record SettingFold(string? Title, string? Description, string? Lead, IReadOnlyList<string> Children);

/// <summary>
/// Every setting the settings window edits, grouped into its sections. Tested against the config
/// reader: each of its keys is either here or in <see cref="FileOnly"/>, each default is what an
/// empty config gives, and each choice reads back without a warning.
/// </summary>
public static partial class SettingsCatalog
{
    public static IReadOnlyList<SettingsSection> Sections { get; } =
    [
        new("appearance", "Appearance", ""),
        new("layout", "Layout", ""),
        new("profiles", "Profiles", ""),
        new("keyboard", "Keyboard", ""),
        new("terminal", "Terminal", ""),
        new("notifications", "Notifications", ""),
        new("quick", "Quick terminal", ""),
        new("about", "About", ""),
    ];

    /// <summary>
    /// Every setting the settings window edits, section by section. Each section's list is a method in its own partial
    /// file, so building this doesn't depend on the order static fields are initialized in.
    /// </summary>
    public static IReadOnlyList<SettingDef> All { get; } =
    [
        .. AppearanceSettings(),
        .. LayoutSettings(),
        .. TerminalSettings(),
        .. NotificationSettings(),
        .. QuickSettings(),
        .. AboutSettings(),
    ];

    /// <summary>Settings the window leaves to config.toml, with what to tell people about them.</summary>
    public static IReadOnlyDictionary<string, string> FileOnly { get; } = new Dictionary<string, string>
    {
        ["colors.palette"] = "Palette colors by number (palette.1 = \"#AC4242\") are set in config.toml.",
        ["shaders.folders"] = "Folders of shaders to choose from. Add and remove them on the shaders page (the library's … menu).",
    };

    /// <summary>The setting with config.toml path <paramref name="key"/> ("font.size"), if <see cref="All"/> has one.</summary>
    public static SettingDef? Find(string key) => (_byKey ??= All.ToDictionary(s => s.Key)).GetValueOrDefault(key);

    // Built on first use, so it doesn't depend on the order the static fields are initialized in. ToDictionary throws
    // on a key listed twice.
    private static Dictionary<string, SettingDef>? _byKey;

    /// <summary>
    /// <paramref name="config"/> with <paramref name="key"/> ("font.size") set to <paramref name="value"/>, a TOML
    /// value, as the reader applies it. Null when the value doesn't fit.
    /// </summary>
    public static Config? Apply(Config config, string key, object value) =>
        Find(key) is { Binding: { } binding } setting
            ? binding.Read(setting, config, value)
            : throw new ArgumentException($"{key} isn't a setting with a binding", nameof(key));

    /// <summary>
    /// Settings shown folded into one card: under the setting they depend on (<see cref="SettingFold.Lead"/>),
    /// or together under a title of their own. Each fold's settings are in one group, in catalog order.
    /// </summary>
    public static IReadOnlyList<SettingFold> Folds { get; } =
    [
        new(null, null, "font.builtin-glyphs", ["font.adjust-box-thickness"]),
        new("Background, text, cursor and selection", "The scheme's, unless you pick your own.", null,
            ["colors.background", "colors.foreground", "colors.cursor", "colors.cursor-text", "colors.selection-background", "colors.selection-foreground"]),
        new(null, null, "window.workspace-tabs", ["window.sidebar", "window.title-bar"]),
        PaneStyle.Fold,
        new(null, null, "window.unfocused-pane-opacity", ["window.unfocused-pane-fill"]),
        new(null, null, "terminal.paste-warning", ["terminal.large-paste-warning"]),
        new(null, null, "colors.palette-generate", ["colors.palette-harmonious"]),
        new("Programs using the clipboard", "Copying and reading with OSC 52, as tmux, neovim and remote shells do.", null,
            ["terminal.clipboard-write", "terminal.clipboard-read"]),
        new(null, null, "notify.command-finished", ["notify.command-finished-after", "notify.command-finished-sound"]),
        new(null, null, "window.free-hidden-memory", ["window.free-hidden-after"]),
    ];

    /// <summary>The fold <paramref name="key"/> leads or sits in, if any.</summary>
    public static SettingFold? FoldOf(string key) => Folds.FirstOrDefault(f => f.Lead == key || f.Children.Contains(key));

    /// <summary>Whether two config values are the same setting (11 and 11.0 are).</summary>
    public static bool Same(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (long or int or double or float, long or int or double or float) =>
            Math.Abs(Convert.ToDouble(a, CultureInfo.InvariantCulture) - Convert.ToDouble(b, CultureInfo.InvariantCulture)) < 1e-9,
        (string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase),
        _ => Equals(a, b),
    };

    /// <summary>
    /// What <paramref name="text"/> (typed into a Text or Number setting) is written as: a number
    /// where the setting takes one, else the text; null for empty (the default).
    /// </summary>
    public static object? FromText(SettingDef setting, string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            return null;
        }

        if ((setting.Kind == SettingKind.Number || setting.NumbersAsNumbers) &&
            double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            // Whole numbers as integers ("12", not "12.0"): the reader takes either.
            return number == Math.Floor(number) && Math.Abs(number) < long.MaxValue ? (object)(long)number : number;
        }

        return t;
    }

    /// <summary>
    /// Why <paramref name="value"/> wouldn't work for <paramref name="setting"/>, from the config
    /// reader itself (null: fine). Scheme names are looked up in <paramref name="themes"/>.
    /// </summary>
    public static string? Problem(SettingDef setting, object value, string? bundledThemes = null)
    {
        var path = setting.Path;
        var toml = $"[{path[0]}]\n{string.Join('.', path.Skip(1))} = {ConfigEditor.FormatValue(value)}\n";
        var warning = ConfigFile.Parse(toml, "config.toml", bundledThemes).Warnings.FirstOrDefault();
        if (warning == null)
        {
            return null;
        }

        // "config.toml:2: ignored font.size = 0 (expected …)" → the part after the location.
        var colon = warning.IndexOf(": ", StringComparison.Ordinal);
        return colon >= 0 ? warning[(colon + 2)..] : warning;
    }
}
