using System.Globalization;

namespace Zinc.Settings;

public static partial class SettingsCatalog
{
    /// <summary>The settings in the Appearance section, in the order the window shows them.</summary>
    private static SettingDef[] AppearanceSettings() =>
    [
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
            "Fonts that have them (Cascadia Code, JetBrains Mono, Fira Code) join `=>`, `!=`, `->`, and the like into one shape. Each character keeps its own cell, and the character under the cursor isn't joined.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.FontLigatures, (c, v) => c with { FontLigatures = v }) },
        new("appearance", "Font", "font.fallback", "Fallback fonts",
            "Zinc tries these fonts in order for characters your font lacks, before Windows picks one. Separate them with commas.",
            SettingKind.Text)
            {
                Placeholder = "Symbols Nerd Font, Noto Sans JP", PlaceholderIsExample = true, DefaultText = "None, so Windows picks",
                Binding = Bind.Custom((c, v) => Config.NormalizeFallback(v) is { } fonts ? c with { FontFallback = fonts } : null,
                    [Accepts.Text(), Accepts.List([Accepts.Text()])], c => c.FontFallback is { Length: > 0 } fonts ? fonts : null),
            },
        new("appearance", "Font", "font.adjust-cell-height", "Line height",
            "Adds room between lines, as a percentage (20%) or in pixels (2). A negative value gives less room. Text stays centered.", SettingKind.Text)
            {
                Placeholder = "0", NumbersAsNumbers = true,
                Binding = Adjustment((c, a) => c with { AdjustCellHeight = a }),
            },
        new("appearance", "Font", "font.builtin-glyphs", "Built-in glyphs",
            "Zinc draws box lines, blocks, and Powerline shapes itself, so they join up in any font.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.BuiltinGlyphs, (c, v) => c with { BuiltinGlyphs = v }) },
        new("appearance", "Font", "font.adjust-box-thickness", "Line thickness",
            "Makes built-in lines thicker or thinner, in pixels (1, -1) or as a percentage (20%).", SettingKind.Text)
            {
                Placeholder = "0", NumbersAsNumbers = true,
                Binding = Adjustment((c, a) => c with { AdjustBoxThickness = a }),
            },
        new("appearance", "Colors", "colors.theme", "Color scheme",
            "About 650 schemes are included. Add your own to the themes folder next to config.toml.", SettingKind.Scheme, null)
            { DefaultText = $"{ThemeLibrary.ZincScheme}, or {ThemeLibrary.ZincLightScheme} in light mode" },
        new("appearance", "Colors", "colors.palette-generate", "Match 256 colors to the scheme",
            "Programs that use the extended 256 colors (vim and htop themes) get shades made from the scheme's 16 colors instead of the standard ones.",
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
            "Colors the character under a block cursor. If the scheme doesn't set one either, it's the background color.", SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { CursorText = v } }) },
        new("appearance", "Colors", "colors.selection-background", "Selection",
            "Colors the background of selected text. If the scheme doesn't set one either, selection inverts the colors.", SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { SelectionBackground = v } }) },
        new("appearance", "Colors", "colors.selection-foreground", "Selected text",
            "If the scheme doesn't set one either, the text keeps its color, or inverts with the selection when \"Selection\" has no color either.",
            SettingKind.Color)
            { Binding = Bind.Color((c, v) => c with { Theme = c.Theme with { SelectionForeground = v } }) },
        // A color, or how far the made fill stands from the background (`HeaderLevel`). The window shows the
        // choices in a list with "Custom color" last.
        new("appearance", "Colors", "colors.pane-header", "Pane headers",
            "Colors the row behind pane titles and tabs. On cards, the border is a step stronger.", SettingKind.Color)
        {
            Placeholder = "From the background",
            NullLabel = "Same as the terminal",
            Choices =
            [
                new("Same as the terminal", "terminal", "The row matches the terminal's background, and hidden tabs get a shade so the shown tab still stands out. Cards then have no border."),
                new("Subtle", "subtle"),
                new("Standard", "standard"),
                new("Strong", "strong"),
            ],
            Binding = Bind.Custom((c, v) => v switch
            {
                string s when Panes.PaneCard.TryLevel(s, out var level) => c with { PaneHeaderColor = null, PaneHeaderLevel = level },
                string s when Config.TryColor(s, out var color) =>
                    c with { PaneHeaderColor = color, PaneHeaderLevel = Panes.HeaderLevel.Standard },
                _ => null,
            }, [Accepts.Choices, Accepts.Text("^auto$"), Accepts.Color]), // "auto" is the older name for "standard"
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
        new("appearance", "Readability", "colors.bold", "Bold text color", "Colors bold text that's in the default text color.", SettingKind.Color)
            { Placeholder = "Same as the text", Binding = Bind.Color((c, v) => c with { BoldColor = v }) },
        new("appearance", "Readability", "colors.faint-opacity", "Faint text",
            "How bright dim text is. At 1, dim text is as bright as other text.", SettingKind.Number)
            {
                Minimum = 0.1, Maximum = 1, Step = 0.05,
                Binding = Bind.Number(c => c.FaintOpacity, (c, v) => c with { FaintOpacity = v }),
            },
        new("appearance", "Readability", "colors.find-highlight", "Find highlight", "Colors the background of each match of Find.", SettingKind.Color)
            { Placeholder = "Zinc's yellow", Binding = Bind.Color((c, v) => c with { SearchBackground = v }) },
        new("appearance", "Readability", "colors.find-highlight-current", "Current match", "Colors the background of the match Find is on.", SettingKind.Color)
            { Placeholder = "Zinc's orange", Binding = Bind.Color((c, v) => c with { SearchSelectedBackground = v }) },
        new("appearance", "Cursor", "terminal.cursor-style", "Cursor shape", "A program can pick its own shape, as vim's insert mode often does.",
            SettingKind.Choice)
            {
                Choices = [new("Block", "block"), new("Bar", "bar"), new("Underline", "underline"), new("Hollow block", "block-hollow")],
                Binding = Bind.Custom((c, v) => CursorStyle(v) is { } style ? c with { CursorStyle = style } : null, [Accepts.Choices]),
            },
        new("appearance", "Cursor", "terminal.cursor-blink", "Blinking cursor", "A program can turn blinking on or off for itself.", SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.CursorBlink, (c, v) => c with { CursorBlink = v }) },
        new("appearance", "Cursor", "colors.cursor-opacity", "Cursor opacity",
            "At 1, the cursor is solid. Lower values let the character under a block cursor show in its own color.", SettingKind.Number)
            {
                Minimum = 0.1, Maximum = 1, Step = 0.05,
                Binding = Bind.Number(c => c.CursorOpacity, (c, v) => c with { CursorOpacity = v }),
            },
        new("appearance", "Cursor", "terminal.cursor-style-at-prompt", "Cursor at the prompt",
            "The cursor takes this shape while the shell waits for a command. A program's own cursor still wins. Needs shell integration.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Same as above", "same"), new("Bar", "bar", "A bar looks like the cursor in a text box."), new("Block", "block"),
                    new("Underline", "underline"), new("Hollow block", "block-hollow"),
                ],
                Binding = Bind.Custom((c, v) => v switch
                {
                    string s when s.Equals("same", StringComparison.OrdinalIgnoreCase) => c with { CursorStyleAtPrompt = null },
                    _ when CursorStyle(v) is { } style => c with { CursorStyleAtPrompt = style },
                    _ => null,
                }, [Accepts.Choices]),
            },
        new("appearance", "Window", "window.appearance", "Light or dark",
            "Sets the look of Zinc's windows, menus, and dialogs, and which scheme the terminal uses while \"Follow light and dark mode\" is on. The terminal changes right away, and the windows after you restart Zinc.", SettingKind.Choice)
            {
                Choices =
                [
                    new("Same as Windows", "system", "Zinc follows \"Choose your mode\" in Windows Settings > Personalization > Colors."),
                    new("Light", "light"),
                    new("Dark", "dark"),
                ],
                Binding = Bind.Enum<ChromeAppearance>((c, v) => c with { Appearance = v }),
            },
        new("appearance", "Window", "window.accent", "Accent color",
            "Colors selections, focus lines, switches, and buttons. Restart Zinc to apply.", SettingKind.Choice)
            {
                Choices =
                [
                    new("Zinc green", "zinc"),
                    new("Windows' accent", "windows", "Zinc uses the accent color from Windows Settings > Personalization > Colors."),
                ],
                Binding = Bind.Custom((c, v) => v switch
                {
                    "zinc" => c with { ZincAccent = true },
                    "windows" => c with { ZincAccent = false },
                    _ => null,
                }, [Accepts.Choices]),
            },
        new("appearance", "Background", "window.backdrop", "Window background", "What's behind everything in the window: tabs, sidebar, and terminal.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Mica", "mica", "Mica isn't see-through. It takes a tint from your wallpaper, as Windows apps do."),
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
            "At 1, the background is solid. Lower values let the window background show behind the text. Colored cells stay solid.",
            SettingKind.Number)
            {
                Minimum = 0, Maximum = 1, Step = 0.05,
                Binding = Bind.Number(c => c.BackgroundOpacity, (c, v) => c with { BackgroundOpacity = v }),
            },
        new("appearance", "Shaders", "shaders.files", "Custom shaders",
            "Effects drawn over the terminal, such as CRT screens, glow, and cursor trails. They're GLSL files written for Ghostty or Shadertoy, and they run in order.",
            SettingKind.Files)
            {
                Expected = "a file path or a list of them",
                Binding = Bind.Paths(Shaders.ShaderPaths.Resolve, (c, v) => c with { ShaderFiles = v }),
            },
        new("appearance", "Shaders", "shaders.animation", "Animation",
            "Says when animated shaders move. The others redraw only when the terminal changes.", SettingKind.Choice)
        {
            Choices =
            [
                new("While focused", true, "Shaders in unfocused terminals pause where they are. The text under them still updates."),
                new("Always", "always", "Every terminal with shaders keeps redrawing, focused or not."),
                new("Never", false, "Animated shaders hold still too, cursor effects included, and redraw only when the terminal changes."),
            ],
            // true and false as booleans or as text, and "always" as text.
            Binding = Bind.Custom((c, v) => (v is string s ? s.Trim().ToLowerInvariant() : v) switch
            {
                true or "true" => c with { ShaderAnimation = ShaderAnimation.Focused },
                false or "false" => c with { ShaderAnimation = ShaderAnimation.Never },
                "always" => c with { ShaderAnimation = ShaderAnimation.Always },
                _ => null,
            }, [Accepts.Choices]),
        },
        new("appearance", "Spacing", "terminal.padding-x", "Padding left and right", "Adds space between the window's edge and the text.", SettingKind.Number)
        {
            Unit = "pt", Maximum = 100,
            Binding = Padding(c => c.PaddingLeft, (c, a, b) => c with { PaddingLeft = a, PaddingRight = b }),
        },
        new("appearance", "Spacing", "terminal.padding-y", "Padding top and bottom", "Adds space between the window's edge and the text.", SettingKind.Number)
        {
            Unit = "pt", Maximum = 100,
            Binding = Padding(c => c.PaddingTop, (c, a, b) => c with { PaddingTop = a, PaddingBottom = b }),
        },
        new("appearance", "Spacing", "terminal.padding-balance", "Even padding",
            "Splits the leftover space between both sides. When off, all of it goes to the right and bottom.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.PaddingBalance, (c, v) => c with { PaddingBalance = v }) },
    ];

    // A property, not a field, because a field in another partial file could be read before it's set.
    private static SettingChoice[] WeightChoices =>
    [
        new("Thin", "thin"), new("Extra light", "extra-light"), new("Light", "light"), new("Normal", "normal"),
        new("Medium", "medium"), new("Semibold", "semibold"), new("Bold", "bold"), new("Extra bold", "extra-bold"),
        new("Black", "black"),
    ];

    /// <summary>A font weight by name ("semibold") or number (600).</summary>
    private static SettingBinding Weight(Func<Config, int, Config> set) =>
        Bind.Custom((c, v) => Bind.AsText(v) is { } s && Config.TryWeight(s, out var weight) ? set(c, weight) : null,
            [Accepts.Choices, Accepts.Integer(1, 999)]);

    /// <summary>A size adjustment in pixels (1, -1) or as a percentage ("20%").</summary>
    private static SettingBinding Adjustment(Func<Config, (double Amount, bool Percent), Config> set) =>
        Bind.Custom((c, v) => Bind.AsText(v) is { } s && Config.TryAdjustment(s, out var adjustment) ? set(c, adjustment) : null,
            [Accepts.AnyNumber, Accepts.Text()]);

    /// <summary>A cursor shape's name, with "block_hollow" taken as "block-hollow" (Ghostty's spelling).</summary>
    private static Ghostty.CursorStyle? CursorStyle(object value) =>
        value is string s && Config.TryMode<Ghostty.CursorStyle>(s.Replace('_', '-'), out var style) ? style : null;

    /// <summary>
    /// Padding in points: one number for both sides, or two (<c>[left, right]</c>), each from 0 to the setting's
    /// maximum of 100. The config holds DIPs.
    /// </summary>
    private static SettingBinding Padding(Func<Config, float> first, Func<Config, float, float, Config> set) =>
        Bind.Custom((setting, c, v) =>
        {
            var text = v is List<object> { Count: 1 or 2 } pair ? string.Join(",", pair.Select(Bind.AsText)) : Bind.AsText(v);

            // The range is checked in points, as written, because converting the DIPs back gives 100.0000076 for 100.
            var inRange = text?.Split(',', StringSplitOptions.TrimEntries).All(p =>
                double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var points) &&
                points >= setting.Minimum && points <= setting.Maximum) == true;
            return inRange && Config.TryPadding(text!, out var a, out var b) ? set(c, a, b) : null;
        }, [Accepts.Number(), Accepts.List([Accepts.Number()], 1, 2)], c => Bind.Tidy(first(c) * 72 / 96));
}
