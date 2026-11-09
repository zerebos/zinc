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
/// applies it (<see cref="Binding"/>). <see cref="Key"/> is its config.toml path ("font.size"). A setting without a
/// binding is still read the older way and gives its default as <paramref name="DeclaredDefault"/>.
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

    private static readonly SettingChoice[] WeightChoices =
    [
        new("Thin", "thin"), new("Extra light", "extra-light"), new("Light", "light"), new("Normal", "normal"),
        new("Medium", "medium"), new("Semibold", "semibold"), new("Bold", "bold"), new("Extra bold", "extra-bold"),
        new("Black", "black"),
    ];

    /// <summary>A font weight by name ("semibold") or number (600).</summary>
    private static SettingBinding Weight(Func<Config, int, Config> set) =>
        Bind.Custom((c, v) => Bind.AsText(v) is { } s && Config.TryWeight(s, out var weight) ? set(c, weight) : null);

    /// <summary>A size adjustment in pixels (1, -1) or as a percentage ("20%").</summary>
    private static SettingBinding Adjustment(Func<Config, (double Amount, bool Percent), Config> set) =>
        Bind.Custom((c, v) => Bind.AsText(v) is { } s && Config.TryAdjustment(s, out var adjustment) ? set(c, adjustment) : null);

    /// <summary><paramref name="config"/> with its quick terminal settings changed.</summary>
    private static Config Quick(Config config, Func<QuickTerminalSettings, QuickTerminalSettings> change) =>
        config with { QuickTerminal = change(config.QuickTerminal) };

    /// <summary>The quick terminal's size as text: "50%", "600px", one number, or two of them as a list.</summary>
    private static string? QuickSize(object value) =>
        value is List<object> { Count: 1 or 2 } pair ? string.Join(",", pair.Select(Bind.AsText)) : Bind.AsText(value);

    /// <summary>A cursor shape's name, with "block_hollow" taken as "block-hollow" (Ghostty's spelling).</summary>
    private static Ghostty.TerminalCursorStyle? CursorStyle(object value) =>
        value is string s && Config.TryMode<Ghostty.TerminalCursorStyle>(s.Replace('_', '-'), out var style) ? style : null;

    /// <summary>
    /// Padding in points: one number for both sides, or two (<c>[left, right]</c>), each from 0 to the setting's
    /// maximum of 100. The config holds DIPs.
    /// </summary>
    private static SettingBinding Padding(Func<Config, float> first, Func<Config, float, float, Config> set) =>
        Bind.Custom((c, v) =>
        {
            var text = v is List<object> { Count: 1 or 2 } pair ? string.Join(",", pair.Select(Bind.AsText)) : Bind.AsText(v);
            return text != null && Config.TryPadding(text, out var a, out var b) && Math.Max(a, b) * 72 / 96 <= MaxPadding
                ? set(c, a, b)
                : null;
        }, c => Bind.Tidy(first(c) * 72 / 96));

    private const double MaxPadding = 100;

    private static readonly SettingChoice[] NotifyChoices =
    [
        new("Tab dot", "badge", "A dot on its tab. The taskbar button also flashes if Zinc is in the background."),
        new("Notification", "toast", "A dot on its tab, and a Windows notification if Zinc is in the background."),
        new("Always notify", "toast-always", "A Windows notification whenever you aren't looking at that pane, even in Zinc."),
        new("None", "never", "No dot, no notification."),
    ];

    public static IReadOnlyList<SettingDef> All { get; } =
    [
        // ---- Appearance
        new("appearance", "Font", "font.family", "Font", "Any monospace font. Nerd Fonts also show prompt icons.", SettingKind.Font)
            { Binding = Bind.Text(c => c.FontFamily, (c, v) => c with { FontFamily = v }) },
        new("appearance", "Font", "font.size", "Size", "Ctrl+0 returns to this size.", SettingKind.Number)
            {
                Unit = "pt", Minimum = 4, Maximum = 72, Step = 0.5,
                Binding = Bind.Number(c => c.FontSize, (c, v) => c with { FontSize = (float)v }),
            },
        new("appearance", "Font", "font.weight", "Weight",
            "How heavy normal text is. A font without that weight shows its nearest one.", SettingKind.Choice)
            {
                Choices = WeightChoices,
                Binding = Weight((c, w) => c with { FontWeight = w }),
            },
        new("appearance", "Font", "font.bold-weight", "Bold weight", "How heavy bold text is.", SettingKind.Choice)
            {
                Choices = WeightChoices,
                Binding = Weight((c, w) => c with { BoldWeight = w }),
            },
        new("appearance", "Font", "font.ligatures", "Ligatures",
            "Fonts that have them (Cascadia Code, JetBrains Mono, Fira Code) join => != -> and the like into one shape. Each character keeps its own cell, and the character under the cursor isn't joined.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.FontLigatures, (c, v) => c with { FontLigatures = v }) },
        new("appearance", "Font", "font.fallback", "Fallback fonts",
            "For characters your font lacks, tried in order before Windows picks one. Separate them with commas.",
            SettingKind.Text)
            {
                Placeholder = "Symbols Nerd Font, Noto Sans JP",
                Binding = Bind.Custom((c, v) => Config.FallbackList(v) is { } fonts ? c with { FontFallback = fonts } : null,
                    c => c.FontFallback is { Length: > 0 } fonts ? fonts : null),
            },
        new("appearance", "Font", "font.adjust-cell-height", "Line height",
            "More room between lines, as a percentage (20%) or in pixels (2). A negative value gives less. Text stays centered.", SettingKind.Text)
            {
                Placeholder = "0", NumbersAsNumbers = true,
                Binding = Adjustment((c, a) => c with { AdjustCellHeight = a }),
            },
        new("appearance", "Font", "font.builtin-glyphs", "Built-in glyphs",
            "Zinc draws box lines, blocks and Powerline shapes itself, so they join up in any font.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.BuiltinGlyphs, (c, v) => c with { BuiltinGlyphs = v }) },
        new("appearance", "Font", "font.adjust-box-thickness", "Line thickness",
            "Thicker or thinner built-in lines, in pixels (1, -1) or as a percentage (20%).", SettingKind.Text)
            {
                Placeholder = "0", NumbersAsNumbers = true,
                Binding = Adjustment((c, a) => c with { AdjustBoxThickness = a }),
            },
        new("appearance", "Colors", "colors.theme", "Color scheme",
            "About 650 are included. Add your own to the themes folder next to config.toml.", SettingKind.Scheme, null),
        new("appearance", "Colors", "colors.palette-generate", "Match 256 colors to the scheme",
            "Programs that use the extended 256 colors (vim and htop themes) get shades made from the scheme's 16, not the standard ones.",
            SettingKind.Toggle)
            {
                Binding = Bind.Bool(c => c.PaletteGeneration.Generate,
                    (c, v) => c with { PaletteGeneration = c.PaletteGeneration with { Generate = v } }),
            },
        new("appearance", "Colors", "colors.palette-harmonious", "Shades from background to text in light schemes",
            "When off, shades run from dark to light, which is what programs expect.", SettingKind.Toggle)
            {
                Binding = Bind.Bool(c => c.PaletteGeneration.Harmonious,
                    (c, v) => c with { PaletteGeneration = c.PaletteGeneration with { Harmonious = v } }),
            },
        new("appearance", "Colors", "colors.background", "Background", "", SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { Background = v } }) },
        new("appearance", "Colors", "colors.foreground", "Text", "", SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { Foreground = v } }) },
        new("appearance", "Colors", "colors.cursor", "Cursor", "", SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { Cursor = v } }) },
        new("appearance", "Colors", "colors.cursor-text", "Cursor text",
            "The character under a block cursor. If the scheme doesn't set one either, it's the background color.", SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { CursorText = v } }) },
        new("appearance", "Colors", "colors.selection-background", "Selection",
            "Behind selected text. If the scheme doesn't set one either, selection inverts the colors.", SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { SelectionBackground = v } }) },
        new("appearance", "Colors", "colors.selection-foreground", "Selected text",
            "If the scheme doesn't set one either, the text keeps its color, or inverts with the selection when \"Selection\" has no color either.",
            SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { SelectionForeground = v } }) },
        // A color, or how far the made fill stands from the background (PaneCard.HeaderLevel). The window shows the
        // choices in a list with "Custom color" last; null (Standard) is the default.
        new("appearance", "Colors", "colors.pane-header", "Pane headers",
            "Behind pane titles and tabs. On cards, the border is a step stronger.", SettingKind.Color)
        {
            Placeholder = "From the background",
            NullLabel = "Standard",
            Choices =
            [
                new("Same as the terminal", "terminal", "Hidden tabs get a shade instead, so the shown tab still stands out. Cards have no border."),
                new("Subtle", "subtle"),
                new("Standard", null),
                new("Strong", "strong"),
            ],
            Binding = Bind.Custom((c, v) => v switch
            {
                string s when Panes.PaneCard.TryLevel(s, out var level) => c with { PaneHeaderColor = null, PaneHeaderLevel = level },
                string s when Config.TryColor(s, out var color) =>
                    c with { PaneHeaderColor = color, PaneHeaderLevel = Panes.HeaderLevel.Standard },
                _ => null,
            }),
        },
        new("appearance", "Readability", "colors.minimum-contrast", "Minimum contrast",
            "Text that reads poorly against its background turns black or white. 1 turns it off, 4.5 is the usual accessibility level, and 21 makes all text black or white.",
            SettingKind.Number)
            {
                Minimum = 1, Maximum = 21, Step = 0.5,
                Binding = Bind.Number(c => c.MinimumContrast, (c, v) => c with { MinimumContrast = v }),
            },
        new("appearance", "Readability", "colors.bold-is-bright", "Bold in bright colors",
            "Bold text in the first eight colors shows their bright versions, as older terminals did.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.BoldIsBright, (c, v) => c with { BoldIsBright = v }) },
        new("appearance", "Readability", "colors.bold", "Bold text color", "For bold text in the default text color.", SettingKind.Color)
            { Placeholder = "Same as the text", Binding = Bind.Color((c, v) => c with { BoldColor = v }) },
        new("appearance", "Readability", "colors.faint-opacity", "Faint text",
            "How bright dim text is (1: as bright as other text).", SettingKind.Number)
            {
                Minimum = 0.1, Maximum = 1, Step = 0.05,
                Binding = Bind.Number(c => c.FaintOpacity, (c, v) => c with { FaintOpacity = v }),
            },
        new("appearance", "Readability", "colors.find-highlight", "Find highlight", "Behind each match of Find.", SettingKind.Color)
            { Placeholder = "Zinc's yellow", Binding = Bind.Color((c, v) => c with { SearchBackground = v }) },
        new("appearance", "Readability", "colors.find-highlight-current", "Current match", "Behind the match Find is on.", SettingKind.Color)
            { Placeholder = "Zinc's orange", Binding = Bind.Color((c, v) => c with { SearchSelectedBackground = v }) },
        new("appearance", "Cursor", "terminal.cursor-style", "Cursor shape", "Until a program picks its own (vim's insert mode often does).",
            SettingKind.Choice)
            {
                Choices = [new("Block", "block"), new("Bar", "bar"), new("Underline", "underline"), new("Hollow block", "block-hollow")],
                Binding = Bind.Custom((c, v) => CursorStyle(v) is { } style ? c with { CursorStyle = style } : null),
            },
        new("appearance", "Cursor", "terminal.cursor-blink", "Blinking cursor", "Until a program says otherwise.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.CursorBlink, (c, v) => c with { CursorBlink = v }) },
        new("appearance", "Cursor", "colors.cursor-opacity", "Cursor opacity",
            "1: solid. Lower lets the character under a block cursor show in its own color.", SettingKind.Number)
            {
                Minimum = 0.1, Maximum = 1, Step = 0.05,
                Binding = Bind.Number(c => c.CursorOpacity, (c, v) => c with { CursorOpacity = v }),
            },
        new("appearance", "Cursor", "terminal.cursor-style-at-prompt", "Cursor at the prompt",
            "While the shell waits for a command. A program's own cursor still wins. Needs shell integration.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Same as above", "same"), new("Bar", "bar", "Like a text box."), new("Block", "block"),
                    new("Underline", "underline"), new("Hollow block", "block-hollow"),
                ],
                Binding = Bind.Custom((c, v) => v switch
                {
                    string s when s.Equals("same", StringComparison.OrdinalIgnoreCase) => c with { CursorStyleAtPrompt = null },
                    _ when CursorStyle(v) is { } style => c with { CursorStyleAtPrompt = style },
                    _ => null,
                }),
            },
        new("appearance", "Window", "window.appearance", "Light or dark",
            "Windows, menus and dialogs. The terminal keeps its color scheme. Restart Zinc to apply.", SettingKind.Choice)
            {
                Choices =
                [
                    new("Same as Windows", "system", "Windows Settings > Personalization > Colors > Choose your mode."),
                    new("Light", "light"),
                    new("Dark", "dark"),
                ],
                Binding = Bind.Enum<ChromeAppearance>((c, v) => c with { Appearance = v }),
            },
        new("appearance", "Window", "window.accent", "Accent color",
            "Selections, focus lines, switches and buttons. Restart Zinc to apply.", SettingKind.Choice)
            {
                Choices =
                [
                    new("Zinc green", "zinc"),
                    new("Windows' accent", "windows", "The one in Windows Settings > Personalization > Colors."),
                ],
                Binding = Bind.Custom((c, v) => v switch
                {
                    "zinc" => c with { ZincAccent = true },
                    "windows" => c with { ZincAccent = false },
                    _ => null,
                }),
            },
        new("appearance", "Background", "window.backdrop", "Window background", "What's behind everything in the window: tabs, sidebar and terminal.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Mica", "mica", "Not see-through. It takes a tint from your wallpaper, as Windows apps do."),
                    new("Acrylic (frosted)", "acrylic", "The windows behind show, blurred."),
                    new("Clear", "none", "The windows behind show as they are."),
                ],
                Binding = Bind.Enum<WindowBackdrop>((c, v) => c with { Backdrop = v }),
            },
        new("appearance", "Background", "window.backdrop-tint", "Window tint",
            "How much color an acrylic or clear background carries. Lower is more see-through.",
            SettingKind.Number)
            {
                Minimum = 0, Maximum = 1, Step = 0.05,
                Binding = Bind.Number(c => c.BackdropTint, (c, v) => c with { BackdropTint = v }),
            },
        new("appearance", "Background", "window.backdrop-color", "Window tint color",
            "The color an acrylic or clear background is tinted with.", SettingKind.Color)
            {
                NullLabel = "Use the terminal's background", Placeholder = "The terminal's background",
                Binding = Bind.Color((c, v) => c with { BackdropColor = v }),
            },
        new("appearance", "Background", "colors.background-opacity", "Terminal background opacity",
            "Lower lets the window background show behind the text (1: solid). Colored cells stay solid.",
            SettingKind.Number)
            {
                Minimum = 0, Maximum = 1, Step = 0.05,
                Binding = Bind.Number(c => c.BackgroundOpacity, (c, v) => c with { BackgroundOpacity = v }),
            },
        new("appearance", "Shaders", "shaders.files", "Custom shaders",
            "Effects drawn over the terminal, such as CRT screens, glow and cursor trails. They're GLSL files written for Ghostty or Shadertoy, and they run in order.",
            SettingKind.Files)
            {
                Expected = "a file path or a list of them",
                Binding = Bind.Paths(Shaders.ShaderPaths.Resolve, (c, v) => c with { ShaderFiles = v }),
            },
        new("appearance", "Shaders", "shaders.animation", "Animation",
            "For animated shaders. The others redraw only when the terminal changes.", SettingKind.Choice)
        {
            Choices =
            [
                new("While focused", true, "Unfocused, they pause where they are. The text under them still updates."),
                new("Always", "always", "Every terminal with shaders keeps redrawing, focused or not."),
                new("Never", false, "They hold still, cursor effects included, and are drawn only when the terminal changes."),
            ],
            // true and false as booleans or as text, and "always" as text.
            Binding = Bind.Custom((c, v) => (v is string s ? s.ToLowerInvariant() : v) switch
            {
                true or "true" => c with { ShaderAnimation = ShaderAnimation.Focused },
                false or "false" => c with { ShaderAnimation = ShaderAnimation.Never },
                "always" => c with { ShaderAnimation = ShaderAnimation.Always },
                _ => null,
            }),
        },
        new("appearance", "Spacing", "terminal.padding-x", "Padding left and right", "Between the window's edge and the text.", SettingKind.Number)
        {
            Unit = "pt", Maximum = MaxPadding,
            Binding = Padding(c => c.PaddingLeft, (c, a, b) => c with { PaddingLeft = a, PaddingRight = b }),
        },
        new("appearance", "Spacing", "terminal.padding-y", "Padding top and bottom", "Between the window's edge and the text.", SettingKind.Number)
        {
            Unit = "pt", Maximum = MaxPadding,
            Binding = Padding(c => c.PaddingTop, (c, a, b) => c with { PaddingTop = a, PaddingBottom = b }),
        },
        new("appearance", "Spacing", "terminal.padding-balance", "Even padding",
            "Splits the leftover space between both sides. When off, all of it goes to the right and bottom.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.PaddingBalance, (c, v) => c with { PaddingBalance = v }) },

        // ---- Layout
        new("layout", "Workspaces", "window.workspace-tabs", "Workspaces", "Tabs along the top, or a list in a sidebar. Applies to windows opened from now on.", SettingKind.Choice)
            {
                Choices = [new("Along the top", "top"), new("In a sidebar", "left")],
                Binding = Bind.Enum<WorkspaceTabs>((c, v) => c with { WorkspaceTabs = v }),
            },
        new("layout", "Workspaces", "window.sidebar", "Sidebar", "Only when \"Workspaces\" is \"In a sidebar\".", SettingKind.Choice)
            {
                Choices = [new("Full", "full"), new("Icons only", "rail"), new("Auto-hide", "auto-hide", "Slides out when the pointer reaches the window's left edge.")],
                Binding = Bind.Enum<SidebarMode>((c, v) => c with { Sidebar = v }),
            },
        new("layout", "Workspaces", "window.title-bar", "Title bar", "Only when \"Workspaces\" is \"In a sidebar\".", SettingKind.Choice)
            {
                Choices = [new("Shown", "show"), new("Auto-hide", "auto-hide", "Slides down when the pointer reaches the window's top edge.")],
                Binding = Bind.Enum<TitleBarMode>((c, v) => c with { TitleBar = v }),
            },
        new("layout", "Workspaces", "window.new-workspace-page", "Start page for new workspaces",
            "Choose a profile, a folder and a name when you open a workspace. Enter starts the default profile.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.NewWorkspacePage, (c, v) => c with { NewWorkspacePage = v }) },
        new("layout", "Workspaces", "window.workspace-colors", "Colors for new workspaces", "For the ones you don't color yourself.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("None", "off"),
                    new("By folder", "folder", "A workspace with a folder gets that folder's color, the same one every time."),
                    new("One after another", "cycle", "Each new workspace takes the next color."),
                ],
                Binding = Bind.Enum<Panes.WorkspaceColorMode>((c, v) => c with { WorkspaceColors = v }),
            },
        new("layout", "Panes", "window.start-in", "New tabs and splits start",
            "New tabs in a workspace with its own folder start there. Without \"Tabs inside panes\", this is for splits only, because a new tab is a new workspace.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Where you are", "current", "In the focused terminal's current folder."),
                    new("In the profile's folder", "profile", "Or in the workspace's folder, when it has one."),
                ],
                Binding = Bind.Enum<Terminal.NewTerminalFolder>((c, v) => c with { StartIn = v }),
            },
        new("layout", "Panes", "window.pane-tabs", "Tabs inside panes", "Each pane can hold several tabs.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.TabGroups, (c, v) => c with { TabGroups = v }) },
        new("layout", "Panes", "window.pane-tabs-as", "Show pane tabs as", "Only with \"Tabs inside panes\" on.", SettingKind.Choice)
            {
                Choices =
                [
                    new("Tabs", "tabs", "A row of tabs. When they don't fit, they shorten, then show only their icons, then fold into a menu."),
                    new("A menu", "menu", "The shown tab, then a count (\"2 of 5\") that opens a menu of every tab. Switch tabs with the menu or with keys."),
                ],
                Binding = Bind.Enum<PaneTabsAs>((c, v) => c with { PaneTabsAs = v }),
            },
        new("layout", "Panes", "window.pane-headers", "Pane headers", "The row above a pane. Its tabs, or its title when it has one tab.", SettingKind.Choice)
            {
                Choices =
                [
                    new("Automatic", "auto", "On every pane once a workspace is split, and on any pane with two or more tabs."),
                    new("With several tabs", "with-tabs", "On panes with two or more tabs."),
                    new("Always", "always"),
                    new("Never", "never"),
                ],
                Binding = Bind.Enum<PaneHeaders>((c, v) => c with { PaneHeaders = v }),
            },
        new("layout", "Panes", "window.pane-header-buttons", "Header buttons", "Split, maximize and more on the right of a pane's header, with a program's progress beside them.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Always", "always", "On every pane."),
                    new("Focused pane", "focused", "On the focused pane, and on the others while the pointer is over their header."),
                    new("On hover", "hover",
                        "On a pane while the pointer is over its header. Touch and pen have no hover. The command palette and a pane's right-click menu have every action."),
                    new("Hide", "hide"),
                ],
                Binding = Bind.Enum<PaneHeaderButtons>((c, v) => c with { PaneHeaderButtons = v }),
            },
        new("layout", "Panes", "window.pane-gap", "Gap between panes",
            "Above 0, panes are rounded cards with space between them. At 0 they meet at a thin divider.", SettingKind.Number)
            {
                Unit = "px", Minimum = 0, Maximum = 32, Step = 1,
                Binding = Bind.Number(c => c.PaneGap, (c, v) => c with { PaneGap = (int)v }, whole: true),
            },
        new("layout", "Panes", "window.pane-gap-edges", "Gap at the workspace edges",
            "The same gap around the outside, split or not, so splitting never moves a pane's outer edges. Only with a \"Gap between panes\" above 0.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.PaneGapEdges, (c, v) => c with { PaneGapEdges = v }) },
        new("layout", "Panes", "window.pane-focus", "Show focus with", "How the focused pane stands out from the others.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Ring", "ring", "An accent-colored outline around the focused pane."),
                    new("Tab accent", "tab-accent", "An accent-colored line along the top of the focused pane."),
                    new("None", "none",
                        "Only the dimming of other panes (\"Unfocused panes\") marks the focused pane. With \"Unfocused panes\" at 1, nothing does."),
                ],
                Binding = Bind.Enum<PaneFocus>((c, v) => c with { PaneFocus = v }),
            },
        new("layout", "Panes", "window.pane-animations", "Pane animations",
            "Panes slide into place when you split, close, maximize or move them. They don't while \"Animation effects\" is off in Windows Settings.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.PaneAnimations, (c, v) => c with { PaneAnimations = v }) },
        new("layout", "Panes", "window.pane-resize-step", "Resize step",
            "How far a resize key moves the divider, as a share of the panes it splits.", SettingKind.Number)
            {
                Unit = "%", Minimum = 1, Maximum = 50, Step = 1,
                Binding = Bind.Number(c => c.PaneResizeStep * 100, (c, v) => c with { PaneResizeStep = v / 100 }),
            },
        new("layout", "Panes", "window.unfocused-pane-opacity", "Unfocused panes",
            "How bright unfocused panes stay (1: not dimmed).", SettingKind.Number)
            {
                Minimum = 0.15, Maximum = 1, Step = 0.05,
                Binding = Bind.Number(c => c.UnfocusedSplitOpacity, (c, v) => c with { UnfocusedSplitOpacity = v }, clamp: true),
            },
        new("layout", "Panes", "window.unfocused-pane-fill", "Dimming color", "What unfocused panes fade toward.", SettingKind.Color)
            { Placeholder = "The pane's background", Binding = Bind.Color((c, v) => c with { UnfocusedSplitFill = v }) },
        new("layout", "Panes", "window.divider-color", "Divider color", "The line between split panes, when \"Gap between panes\" is 0.", SettingKind.Color)
            { Placeholder = "Zinc's grey", Binding = Bind.Color((c, v) => c with { SplitDividerColor = v }) },
        new("layout", "When you come back", "window.save-state", "Reopen windows",
            "Windows, workspaces, splits and tabs come back in their folders.", SettingKind.Choice)
            {
                Choices = [new("Reopen them", "always"), new("Start fresh", "never")],
                Binding = Bind.Custom((c, v) => v switch
                {
                    true or "always" or "default" => c with { RestoreSession = true },
                    false or "never" => c with { RestoreSession = false },
                    _ => null,
                }),
            },
        new("layout", "When you come back", "window.quit-after-last-window-closed", "When the last window closes",
            "", SettingKind.Choice)
            {
                Choices =
                [
                    new("Automatic", null, "While the quick terminal's hotkey is set, it stays in the tray so the hotkey keeps working. Otherwise it quits."),
                    new("Quit", true),
                    new("Stay in the tray", false, "Click the tray icon to open a window again."),
                ],
                Binding = Bind.Custom((c, v) => v is bool b ? c with { QuitAfterLastWindowClosed = b } : null),
            },
        new("layout", "When you come back", "window.launch-at-login", "When you sign in to Windows", "", SettingKind.Choice)
            {
                Choices =
                [
                    new("Don't start Zinc", "off"),
                    new("Start in the tray", "tray", "No window opens. The quick terminal's hotkey works, and the tray icon opens your windows."),
                    new("Open my windows", "windows", "As they were when you left, or a new one."),
                ],
                Binding = Bind.Custom((c, v) => v switch
                {
                    true => c with { LaunchAtLogin = LaunchAtLogin.Windows },
                    false => c with { LaunchAtLogin = LaunchAtLogin.Off },
                    string s when Config.TryMode<LaunchAtLogin>(s, out var login) => c with { LaunchAtLogin = login },
                    _ => null,
                }),
            },
        new("layout", "When you come back", "window.explorer-menu", "Open in Zinc from File Explorer",
            "Adds \"Open in Zinc\" to the right-click menu of folders and drives. On Windows 11 it's under \"Show more options\".",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.ExplorerMenu, (c, v) => c with { ExplorerMenu = v }) },
        new("layout", "Closing", "window.confirm-close", "Ask before closing",
            "When a tab, pane, workspace or window you close still runs a command, or holds two or more tabs. Zinc knows a command is running through shell integration.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.ConfirmClose, (c, v) => c with { ConfirmClose = v }) },
        new("layout", "Closing", "window.confirm-quit", "Ask before quitting with a running command",
            "When you quit Zinc (or restart it for an update) while a command still runs.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.ConfirmQuit, (c, v) => c with { ConfirmQuit = v }) },

        // ---- Terminal
        new("terminal", "Shell", "terminal.shell-integration", "Shell integration",
            "Marks prompts and commands in PowerShell, bash and zsh (WSL too), so you can jump between prompts, copy a command's output and see which commands failed.",
            SettingKind.Choice)
            {
                Choices = [new("On", "auto"), new("Off", "off")],
                Binding = Bind.Enum<ShellIntegrationMode>((c, v) => c with { ShellIntegration = v }),
            },
        new("terminal", "Shell", "terminal.command-marks", "Command marks", "The bar beside a command's output.", SettingKind.Choice)
            {
                Choices = [new("Failed commands", "failures"), new("Every command", "all"), new("None", "none")],
                Binding = Bind.Enum<CommandMarks>((c, v) => c with { CommandMarks = v }),
            },
        new("terminal", "Shell", "terminal.close-on-exit", "When the program ends", "", SettingKind.Choice)
            {
                Choices =
                [
                    new("Close if it succeeded", "success", "A failure stays on screen so you can read why. Any key closes it."),
                    new("Always close", "always"),
                    new("Keep it open", "never", "Any key closes it."),
                ],
                Binding = Bind.Enum<CloseOnExit>((c, v) => c with { CloseOnExit = v }),
            },
        new("terminal", "Shell", "terminal.reload-environment", "Pick up environment changes",
            "New tabs read PATH and other variables from Windows as they start, so an installer's changes work without restarting Zinc.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.ReloadEnvironment, (c, v) => c with { ReloadEnvironment = v }) },
        new("terminal", "Shell", "terminal.program-titles", "Program titles",
            "Shells and programs set the tab's title (often the folder or the running command). When off, tabs show the profile's name.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.ProgramTitles, (c, v) => c with { ProgramTitles = v }) },
        new("terminal", "Shell", "terminal.shell-titles", "Folder and command as the title",
            "The folder at the prompt, the running command otherwise. A program's own title still wins. Needs shell integration.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.ShellTitles, (c, v) => c with { ShellTitles = v }) },
        new("terminal", "Scrollback", "terminal.scrollback-lines", "Scrollback lines", "How far back you can scroll.", SettingKind.Number)
        {
            Minimum = 0, Maximum = Config.MaxScrollbackLines, Step = 1000,
            Binding = Bind.Number(c => c.ScrollbackLines, (c, v) => c with { ScrollbackLines = (int)v }, whole: true),
        },
        new("terminal", "Mouse", "terminal.mouse-pointer", "Pointer over text", "", SettingKind.Choice)
            {
                Choices = [new("Arrow", "arrow"), new("I-beam", "text")],
                Binding = Bind.Custom((c, v) => v switch
                {
                    "arrow" => c with { TextPointer = false },
                    "text" => c with { TextPointer = true },
                    _ => null,
                }),
            },
        new("terminal", "Mouse", "terminal.word-boundaries", "Word boundaries",
            "Characters that end a word when you double-click (space and tab always do). Empty: the ones shown.", SettingKind.Text)
            {
                Placeholder = "'\"│`|:;,()[]{}<>$",
                // Empty means the built-in set, so it's accepted too.
                Binding = Bind.Custom((c, v) => v is string s ? c with { WordBoundaries = s.Length > 0 ? s : null } : null),
            },
        new("terminal", "Mouse", "terminal.click-to-move", "Click to move the cursor",
            "At a shell prompt, clicking in the command you're typing moves the cursor there. Needs shell integration.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.ClickToMove, (c, v) => c with { ClickToMove = v }) },
        new("terminal", "Mouse", "terminal.right-click", "Right-click", "Shift+F10 or the Menu key opens the menu anywhere.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Menu", "menu", "Copy, paste, find, the link or command you clicked, split and close."),
                    new("Copy or paste", "copy-paste", "Copies a selection, or pastes when there's none."),
                    new("Paste", "paste"),
                ],
                Binding = Bind.Enum<RightClick>((c, v) => c with { RightClick = v }),
            },
        new("terminal", "Mouse", "terminal.link-click", "Open links with", "Web links and files shown in the terminal.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Ctrl+click", "ctrl", "Holding Ctrl underlines the links."),
                    new("Alt+click", "alt", "Holding Alt underlines the links."),
                    new("A click", "click", "Dragging across a link still selects it. A program using the mouse still gets the click."),
                ],
                Binding = Bind.Enum<LinkClick>((c, v) => c with { LinkClick = v }),
            },
        new("terminal", "Mouse", "terminal.middle-click-paste", "Middle-click pastes",
            "Unless the program uses the mouse.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.MiddleClickPaste, (c, v) => c with { MiddleClickPaste = v }) },
        new("terminal", "Mouse", "window.focus-follows-mouse", "Focus follows mouse",
            "Moving the pointer into a pane focuses it.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.FocusFollowsMouse, (c, v) => c with { FocusFollowsMouse = v }) },
        new("terminal", "Clipboard", "terminal.copy-on-select", "Copy on select",
            "Copies text as soon as you select it. The selection stays.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.CopyOnSelect, (c, v) => c with { CopyOnSelect = v }) },
        new("terminal", "Clipboard", "terminal.copy-formatting", "Copy with formatting",
            "Copies colors and styles too, for documents and email. Apps that take plain text still get plain text.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.CopyFormatting, (c, v) => c with { CopyFormatting = v }) },
        new("terminal", "Clipboard", "terminal.paste-warning", "Paste warning", "Ask before pasting text with more than one line.",
            SettingKind.Choice)
            {
                Binding = Bind.Enum<Terminal.PasteWarning>((c, v) => c with { PasteWarning = v }),
                Choices =
                [
                    new("Automatic", "auto", "When the program would take each line as Enter and run it. A program that marks pastes (bracketed paste) gets no warning."),
                    new("Always", "multiline", "Whenever a paste has more than one line."),
                    new("Never", "never"),
                ],
            },
        new("terminal", "Clipboard", "terminal.large-paste-warning", "Large pastes", "Ask before pasting more than 5 KB.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.LargePasteWarning, (c, v) => c with { LargePasteWarning = v }) },
        new("terminal", "Clipboard", "terminal.clipboard-write", "Programs may copy",
            "Copying with OSC 52, as tmux, neovim and remote shells do.", SettingKind.Toggle)
            {
                // true or false, or "allow" or "deny" as the read setting takes them.
                Binding = Bind.Custom((c, v) => v switch
                {
                    true or "allow" => c with { ClipboardWrite = true },
                    false or "deny" => c with { ClipboardWrite = false },
                    _ => null,
                }, c => c.ClipboardWrite),
            },
        new("terminal", "Clipboard", "terminal.clipboard-read", "Programs reading the clipboard",
            "Anything running, including a remote host, could read it.", SettingKind.Choice)
            {
                Choices =
                [
                    new("Ask each time", "ask", "Allow once, for the tab, or not."),
                    new("Allow", "allow"),
                    new("Deny", "deny"),
                ],
                // "ask", "allow" or "deny", or true and false for the last two.
                Binding = Bind.Custom((c, v) => v switch
                {
                    true => c with { ClipboardRead = ClipboardAccess.Allow },
                    false => c with { ClipboardRead = ClipboardAccess.Deny },
                    string s when Config.TryMode<ClipboardAccess>(s, out var access) => c with { ClipboardRead = access },
                    _ => null,
                }),
            },
        new("terminal", "Bell", "terminal.bell", "Bell", "When a program rings it, e.g. at a failed tab completion.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Tab dot", "attention", "A dot on its tab, and the taskbar button flashes, when you aren't looking at that pane."),
                    new("Flash the pane", "flash", "Marks the tab too."),
                    new("None", "none"),
                ],
                Binding = Bind.Enum<BellMode>((c, v) => c with { Bell = v }),
            },
        new("terminal", "Bell", "terminal.bell-sound", "Bell sound", "Plays Windows' default sound too.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.BellSound, (c, v) => c with { BellSound = v }) },
        new("terminal", "Bell", "terminal.screen-flash-bell", "Visual bell",
            "Programs that flash the screen instead of beeping (Git Bash does) ring Zinc's bell, as set in \"Bell\".", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.ScreenFlashBell, (c, v) => c with { ScreenFlashBell = v }) },
        new("terminal", "Command line", "cli.remote-control", "Remote control",
            "Lets zn, Zinc's command-line tool, open, arrange and close tabs, type into them and read them. Any program running as you could use it.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.RemoteControl, (c, v) => c with { RemoteControl = v }) },
        new("terminal", "Command line", "cli.path", "Use zn in other terminals",
            "Adds zn to your PATH, so other terminals find it too (Zinc's own terminals always do). Shells started from now on see it.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.ZnOnPath, (c, v) => c with { ZnOnPath = v }) },
        new("terminal", "Troubleshooting", "terminal.input-method", "Input methods",
            "Type Japanese, Chinese, Korean and more with Windows' input methods (IME). Turn it off if keys behave oddly.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.InputMethod, (c, v) => c with { InputMethod = v }) },
        new("about", "Updates", "updates.check", "Check for updates automatically",
            "Once a day. New versions download in the background, and you choose when to restart. Only for Zinc installed with its installer.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.UpdateCheck, (c, v) => c with { UpdateCheck = v }) },
        new("about", "Troubleshooting", "debug.logging", "Debug log",
            "Keeps a record of what Zinc does, to send with a bug report. Turn it on, make the problem happen, then copy the log.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.Logging, (c, v) => c with { Logging = v }) },
        new("terminal", "Troubleshooting", "window.low-latency-drawing", "Low-latency drawing",
            "What you type shows up about one screen refresh sooner. Turn it off if a terminal draws wrong or stops drawing, and tell us. Applies to terminals opened from now on.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.LowLatencyDrawing, (c, v) => c with { LowLatencyDrawing = v }) },
        new("terminal", "Troubleshooting", "window.free-hidden-memory", "Free memory of hidden tabs",
            "Background tabs, minimized windows and the dismissed quick terminal give back their graphics memory and rebuild it when you return. Turn it off if switching tabs flashes. Only with \"Low-latency drawing\".",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.FreeHiddenMemory, (c, v) => c with { FreeHiddenMemory = v }) },
        new("terminal", "Troubleshooting", "window.free-hidden-after", "Free it after",
            "Seconds, or with a unit: 45s, 2m.", SettingKind.Text)
            {
                Placeholder = "60", NumbersAsNumbers = true,
                Binding = Bind.Custom(
                    (c, v) => Bind.AsText(v) is { } s && Config.TryDuration(s, out var d) && d >= TimeSpan.FromSeconds(1)
                        ? c with { FreeHiddenAfter = d }
                        : null,
                    c => Bind.Tidy(c.FreeHiddenAfter.TotalSeconds)),
            },
        new("terminal", "Troubleshooting", "window.software-rendering", "Software rendering",
            "Draws with the processor instead of the graphics card, which is slower. Use it for broken graphics drivers, some virtual machines and remote desktop glitches. Applies to terminals opened from now on.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.SoftwareRendering, (c, v) => c with { SoftwareRendering = v }) },

        // ---- Notifications
        new("notifications", "Long commands", "notify.command-finished", "Long command finishes",
            "In a pane you weren't looking at.", SettingKind.Choice)
            { Choices = NotifyChoices, Binding = Bind.Enum<NotifyMode>((c, v) => c with { NotifyCommandFinished = v }) },
        new("notifications", "Long commands", "notify.command-finished-after", "Counts as long after",
            "Seconds, or with a unit: 45s, 2m.", SettingKind.Text)
            {
                Placeholder = "10", NumbersAsNumbers = true,
                Binding = Bind.Custom(
                    (c, v) => Bind.AsText(v) is { } s && Config.TryDuration(s, out var d)
                        ? c with { NotifyCommandFinishedAfter = d }
                        : null,
                    c => Bind.Tidy(c.NotifyCommandFinishedAfter.TotalSeconds)),
            },
        new("notifications", "Long commands", "notify.command-finished-sound", "Sound",
            "Plays when a long command finishes out of view (a Windows notification has its own).", SettingKind.Choice)
            {
                Choices = [new("None", "off"), new("Windows' notification sound", "notification"), new("Windows' default beep", "beep")],
                Binding = Bind.Enum<FinishedSound>((c, v) => c with { CommandFinishedSound = v }),
            },
        new("notifications", "Programs", "notify.programs", "Program notifications",
            "When a program asks for one, e.g. a build script finishing (OSC 9 or 777).", SettingKind.Choice)
            { Choices = NotifyChoices, Binding = Bind.Enum<NotifyMode>((c, v) => c with { NotifyPrograms = v }) },
        new("notifications", "Dot colors", "notify.finished-color", "Finished",
            "A command finished, or a program sent a notification.", SettingKind.Color)
            {
                NullLabel = "Use Zinc's green", Placeholder = "Zinc's green",
                Binding = Bind.Color((c, v) => c with { DotFinishedColor = v }),
            },
        new("notifications", "Dot colors", "notify.bell-color", "Bell", "A program rang the bell.", SettingKind.Color)
            {
                NullLabel = "Use Zinc's amber", Placeholder = "Zinc's amber",
                Binding = Bind.Color((c, v) => c with { DotBellColor = v }),
            },
        new("notifications", "Dot colors", "notify.failed-color", "Failed", "A command failed, or a program exited with an error.", SettingKind.Color)
            {
                NullLabel = "Use Zinc's red", Placeholder = "Zinc's red",
                Binding = Bind.Color((c, v) => c with { DotFailedColor = v }),
            },

        // ---- Quick terminal
        new("quick", "Summon", "quick-terminal.hotkey", "Hotkey", "Shows and hides the quick terminal from anywhere, e.g. win+grave or ctrl+alt+t.", SettingKind.Text)
            {
                Binding = Bind.Custom(
                    (c, v) => v is string s && Config.TryHotkey(s) ? Quick(c, q => q with { Hotkey = s }) : null,
                    c => c.QuickTerminal.Hotkey),
            },
        new("quick", "Placement", "quick-terminal.position", "Position", "", SettingKind.Choice)
            {
                Choices = [new("Top", "top"), new("Bottom", "bottom"), new("Left", "left"), new("Right", "right"), new("Center", "center")],
                Binding = Bind.Enum<QuickTerminalPosition>((c, v) => Quick(c, q => q with { Position = v })),
            },
        new("quick", "Placement", "quick-terminal.screen", "Screen", "", SettingKind.Choice)
            {
                Choices = [new("With the pointer", "mouse", "Opens on the screen the pointer is on."), new("Main", "main", "Always opens on the main screen.")],
                Binding = Bind.Enum<QuickTerminalScreen>((c, v) => Quick(c, q => q with { Screen = v })),
            },
        new("quick", "Placement", "quick-terminal.size", "Size",
            "A percentage or pixels (50%, 600px) along its edge. Empty: 50%, or 70% by 60% at \"Center\".", SettingKind.Text)
            {
                Placeholder = "50%",
                Binding = Bind.Custom((c, v) => QuickSize(v) is { } s && Config.TryQuickSize(s, out var a, out var b)
                    ? Quick(c, q => q with { Primary = a, Secondary = b })
                    : null),
            },
        new("quick", "Behavior", "quick-terminal.autohide", "Hide when it loses focus", "", SettingKind.Toggle)
            {
                Binding = Bind.Bool(c => c.QuickTerminal.Autohide, (c, v) => Quick(c, q => q with { Autohide = v })),
            },
        new("quick", "Behavior", "quick-terminal.animation-duration", "Animation", "How long the quick terminal takes to fade in and out. At 0 it appears at once.",
            SettingKind.Number)
            {
                Unit = "seconds", Minimum = 0, Maximum = 2, Step = 0.05,
                Binding = Bind.Number(c => c.QuickTerminal.AnimationDuration, (c, v) => Quick(c, q => q with { AnimationDuration = v })),
            },
    ];

    /// <summary>A profile's own fields (keys relative to its <c>[profile.&lt;id&gt;]</c> section).</summary>
    public static IReadOnlyList<SettingDef> ProfileFields { get; } =
    [
        new("profiles", "General", "name", "Name", "Shown in menus and tabs.", SettingKind.Text, null),
        new("profiles", "General", "command", "Command", "What runs in its tabs: a shell, or any program.", SettingKind.Text, null),
        new("profiles", "General", "working-directory", "Starting folder", "~ (your home), a path, or %VARIABLES%.", SettingKind.Text, null)
            { Placeholder = "~" },
        new("profiles", "General", "icon", "Icon", "An image file for its tabs (default: the program's own icon).", SettingKind.Text, null),
        new("profiles", "General", "hidden", "Hidden", "Kept out of the menus (its tabs still reopen).", SettingKind.Toggle, false),
    ];

    /// <summary>Where Zinc looks for shells (<c>[discover]</c>): each on by default.</summary>
    public static IReadOnlyList<SettingDef> Discovery { get; } =
    [
        new("profiles", "Find shells on this PC", "discover.wsl", "WSL distributions", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.powershell", "PowerShell 7 and Windows PowerShell", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.cmd", "Command Prompt", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.git-bash", "Git Bash", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.msys2", "MSYS2", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.visual-studio", "Visual Studio developer shells", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.ssh", "SSH hosts", "From ~/.ssh/config.", SettingKind.Toggle, true),
    ];

    /// <summary>Settings the window leaves to config.toml, with what to tell people about them.</summary>
    public static IReadOnlyDictionary<string, string> FileOnly { get; } = new Dictionary<string, string>
    {
        ["colors.palette"] = "Palette colors by number (palette.1 = \"#AC4242\") are set in config.toml.",
        ["shaders.folders"] = "Folders of shaders to choose from. Add and remove them on the shaders page (the library's … menu).",
    };

    public static SettingDef? Find(string key) => All.FirstOrDefault(s => s.Key == key);

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
