using System.Globalization;

namespace Phosphor.Settings;

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
}

/// <summary>A choice's label and the value written for it (null: the default, written as nothing).</summary>
public sealed record SettingChoice(string Label, object? Value);

/// <summary>A section of the settings window.</summary>
public sealed record SettingsSection(string Id, string Title, string Glyph);

/// <summary>
/// One setting as the settings window shows it: where it goes, what it's called, how it's edited
/// and its default (null: none written, the app's own behavior). <see cref="Key"/> is its
/// config.toml path ("font.size").
/// </summary>
public sealed record SettingDef(string Section, string Group, string Key, string Label, string Description, SettingKind Kind, object? Default)
{
    public IReadOnlyList<SettingChoice> Choices { get; init; } = [];

    /// <summary>Shown after a number, or as a Text setting's hint.</summary>
    public string? Unit { get; init; }

    public double Minimum { get; init; }

    public double Maximum { get; init; } = double.MaxValue;

    public double Step { get; init; } = 1;

    /// <summary>For Text: shown while empty.</summary>
    public string? Placeholder { get; init; }

    /// <summary>For Text: "10" is written as the number 10, "30s" as a string.</summary>
    public bool NumbersAsNumbers { get; init; }

    /// <summary>The key's path in config.toml.</summary>
    public IReadOnlyList<string> Path => Key.Split('.');

    /// <summary>Whether a profile can have its own value (the [font], [colors] and [terminal] groups).</summary>
    public bool ProfileOverridable => Path[0] is "font" or "colors" or "terminal";
}

/// <summary>
/// Every setting the settings window edits, grouped into its sections. Tested against the config
/// reader: each of its keys is either here or in <see cref="FileOnly"/>, each default is what an
/// empty config gives, and each choice reads back without a warning.
/// </summary>
public static class SettingsCatalog
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

    private static readonly SettingChoice[] NotifyChoices =
    [
        new("A dot on its tab", "badge"),
        new("A Windows notification while Phosphor is in the background", "toast"),
        new("A Windows notification whenever you're not looking", "toast-always"),
        new("Nothing", "never"),
    ];

    public static IReadOnlyList<SettingDef> All { get; } =
    [
        // ---- Appearance
        new("appearance", "Font", "font.family", "Font", "Any monospace font; Nerd Fonts show prompt icons.", SettingKind.Font, "Cascadia Mono NF"),
        new("appearance", "Font", "font.size", "Size", "Ctrl+0 returns to this size.", SettingKind.Number, 10.5)
            { Unit = "pt", Minimum = 4, Maximum = 72, Step = 0.5 },
        new("appearance", "Font", "font.builtin-glyphs", "Built-in glyphs",
            "Box drawing, blocks, Powerline and Symbols for Legacy Computing drawn by Ghostty's rasterizer: seamless in any font.",
            SettingKind.Toggle, true),
        new("appearance", "Font", "font.adjust-box-thickness", "Line thickness",
            "Thicker or thinner built-in lines: pixels (1, -1) or a percentage (20%).", SettingKind.Text, null)
            { Placeholder = "0", NumbersAsNumbers = true },
        new("appearance", "Colors", "colors.theme", "Color scheme",
            "About 650 are included; yours go in the themes folder next to config.toml.", SettingKind.Scheme, null),
        new("appearance", "Colors", "colors.background", "Background", "Instead of the scheme's.", SettingKind.Color, null),
        new("appearance", "Colors", "colors.foreground", "Text", "Instead of the scheme's.", SettingKind.Color, null),
        new("appearance", "Colors", "colors.cursor", "Cursor", "Instead of the scheme's.", SettingKind.Color, null),
        new("appearance", "Spacing", "terminal.padding-x", "Padding left and right", "Between the window's edge and the text.", SettingKind.Number, 6L)
            { Unit = "pt", Maximum = 100 },
        new("appearance", "Spacing", "terminal.padding-y", "Padding top and bottom", "Between the window's edge and the text.", SettingKind.Number, 6L)
            { Unit = "pt", Maximum = 100 },

        // ---- Layout
        new("layout", "Workspaces", "window.workspace-tabs", "Workspaces", "Tabs along the top, or a list in a sidebar. Applies to windows opened from now on.", SettingKind.Choice, "top")
            { Choices = [new("Along the top", "top"), new("In a sidebar", "left")] },
        new("layout", "Workspaces", "window.sidebar", "Sidebar", "With workspaces in a sidebar.", SettingKind.Choice, "full")
            { Choices = [new("Full", "full"), new("Icons only", "rail"), new("Hidden until you reach the edge", "auto-hide")] },
        new("layout", "Workspaces", "window.title-bar", "Title bar", "With workspaces in a sidebar.", SettingKind.Choice, "show")
            { Choices = [new("Shown", "show"), new("Hidden until you reach the top", "auto-hide")] },
        new("layout", "Panes", "window.tab-groups", "Tabs inside panes", "Each pane can hold several tabs.", SettingKind.Toggle, true),
        new("layout", "Panes", "window.group-tab-strip", "Pane tab strip", "The row of tabs above a pane.", SettingKind.Choice, "auto")
            { Choices = [new("When a pane has two or more tabs", "auto"), new("Always", "always")] },
        new("layout", "Panes", "window.unfocused-split-opacity", "Unfocused panes",
            "How much of an unfocused pane shows through (1: not dimmed).", SettingKind.Number, 0.7)
            { Minimum = 0.15, Maximum = 1, Step = 0.05 },
        new("layout", "Panes", "window.unfocused-split-fill", "Dimming color", "What unfocused panes fade toward (default: their background).", SettingKind.Color, null),
        new("layout", "When you come back", "window.save-state", "Reopen windows",
            "Windows, workspaces, splits and tabs come back in their folders.", SettingKind.Choice, "always")
            { Choices = [new("Reopen them", "always"), new("Start fresh", "never")] },
        new("layout", "When you come back", "window.quit-after-last-window-closed", "After the last window closes",
            "Staying in the tray keeps the quick terminal's hotkey working.", SettingKind.Choice, null)
            { Choices = [new("Stay in the tray while the quick terminal hotkey works", null), new("Always quit", true), new("Always stay in the tray", false)] },

        // ---- Terminal
        new("terminal", "Shell", "terminal.shell-integration", "Shell integration",
            "Marks prompts and commands in PowerShell, bash and zsh (WSL too), for jumping, copying output and failure bars.",
            SettingKind.Choice, "auto")
            { Choices = [new("On", "auto"), new("Off", "off")] },
        new("terminal", "Shell", "terminal.command-marks", "Command marks", "The bar beside a command's output.", SettingKind.Choice, "failures")
            { Choices = [new("Failed commands", "failures"), new("Every command", "all"), new("None", "none")] },
        new("terminal", "Mouse", "terminal.mouse-pointer", "Pointer over text", "", SettingKind.Choice, "arrow")
            { Choices = [new("Arrow, like Windows Terminal", "arrow"), new("Text I-beam, like Ghostty", "text")] },
        new("terminal", "Clipboard", "terminal.clipboard-write", "Programs may copy",
            "Copying with OSC 52, as tmux, neovim and remote shells do.", SettingKind.Toggle, true),
        new("terminal", "Clipboard", "terminal.clipboard-read", "Programs may read the clipboard",
            "Anything running, including a remote host, could read it.", SettingKind.Toggle, false),

        // ---- Notifications
        new("notifications", "Long commands", "notify.command-finished", "When a long command finishes out of view", "", SettingKind.Choice, "badge")
            { Choices = NotifyChoices },
        new("notifications", "Long commands", "notify.command-finished-after", "Counts as long after",
            "Seconds, or with a unit: 45s, 2m.", SettingKind.Text, 10L)
            { Placeholder = "10", NumbersAsNumbers = true },
        new("notifications", "Programs", "notify.programs", "When a program sends a notification", "OSC 9 and OSC 777, e.g. a build script.", SettingKind.Choice, "badge")
            { Choices = NotifyChoices },

        // ---- Quick terminal
        new("quick", "Summon", "quick-terminal.hotkey", "Hotkey", "Shows and hides it from anywhere, e.g. win+grave or ctrl+alt+t.", SettingKind.Text, "win+grave"),
        new("quick", "Placement", "quick-terminal.position", "Position", "", SettingKind.Choice, "top")
            { Choices = [new("Top", "top"), new("Bottom", "bottom"), new("Left", "left"), new("Right", "right"), new("Center", "center")] },
        new("quick", "Placement", "quick-terminal.screen", "Screen", "", SettingKind.Choice, "mouse")
            { Choices = [new("The one with the pointer", "mouse"), new("The main one", "main")] },
        new("quick", "Placement", "quick-terminal.size", "Size",
            "A percentage or pixels (50%, 600px) along its edge; defaults: 50%, or 70% by 60% centered.", SettingKind.Text, null)
            { Placeholder = "50%" },
        new("quick", "Behavior", "quick-terminal.autohide", "Hide when it loses focus", "", SettingKind.Toggle, true),
    ];

    /// <summary>Settings the window leaves to config.toml, with what to tell people about them.</summary>
    public static IReadOnlyDictionary<string, string> FileOnly { get; } = new Dictionary<string, string>
    {
        ["colors.palette"] = "Palette colors by number (palette.1 = \"#AC4242\") are set in config.toml.",
    };

    public static SettingDef? Find(string key) => All.FirstOrDefault(s => s.Key == key);

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
