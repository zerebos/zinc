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

/// <summary>
/// A choice's label (short: it's a menu item), the value written for it (null: the default, written
/// as nothing), and what it means in more words, shown under the setting's name while it's chosen.
/// </summary>
public sealed record SettingChoice(string Label, object? Value, string? Detail = null);

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

    /// <summary>A color setting's "no color" choice, if not "Use the scheme's color".</summary>
    public string? NullLabel { get; init; }

    /// <summary>For Text: "10" is written as the number 10, "30s" as a string.</summary>
    public bool NumbersAsNumbers { get; init; }

    /// <summary>The key's path in config.toml.</summary>
    public IReadOnlyList<string> Path => Key.Split('.');

    /// <summary>Whether a profile can have its own value (the [font], [colors] and [terminal] groups).</summary>
    public bool ProfileOverridable => Path[0] is "font" or "colors" or "terminal";
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

    private static readonly SettingChoice[] NotifyChoices =
    [
        new("Tab dot", "badge", "A dot on its tab; the taskbar button flashes if Phosphor is in the background."),
        new("Notification", "toast", "A dot on its tab, and a Windows notification if Phosphor is in the background."),
        new("Always notify", "toast-always", "A Windows notification whenever you aren't looking at that pane, even in Phosphor."),
        new("Nothing", "never", "No dot, no notification."),
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
        new("appearance", "Colors", "colors.cursor-text", "Cursor text",
            "The character under a block cursor, instead of the scheme's (without either, the background color).", SettingKind.Color, null),
        new("appearance", "Colors", "colors.selection-background", "Selection",
            "Behind selected text, instead of the scheme's (without either, selection inverts the colors).", SettingKind.Color, null),
        new("appearance", "Colors", "colors.selection-foreground", "Selected text",
            "Instead of the scheme's (without either, the text keeps its color).", SettingKind.Color, null),
        new("appearance", "Readability", "colors.minimum-contrast", "Minimum contrast",
            "Text that reads poorly against its background turns black or white. 1: off; 4.5: the usual accessibility level; 21: always black or white.",
            SettingKind.Number, 1.0)
            { Minimum = 1, Maximum = 21, Step = 0.5 },
        new("appearance", "Readability", "colors.bold-is-bright", "Bold in bright colors",
            "Bold text in the first eight colors shows their bright versions, as older terminals did.", SettingKind.Toggle, false),
        new("appearance", "Readability", "colors.bold", "Bold text color", "For bold text in the default text color.", SettingKind.Color, null)
            { Placeholder = "Same as the text" },
        new("appearance", "Readability", "colors.faint-opacity", "Faint text",
            "How much of faint (dim) text's color shows (1: as bright as other text).", SettingKind.Number, 0.5)
            { Minimum = 0.1, Maximum = 1, Step = 0.05 },
        new("appearance", "Readability", "colors.find-highlight", "Find highlight", "Behind each match of Find (default: a light yellow).",
            SettingKind.Color, null) { Placeholder = "Phosphor's yellow" },
        new("appearance", "Readability", "colors.find-highlight-current", "Current match", "Behind the match Find is on (default: orange).",
            SettingKind.Color, null) { Placeholder = "Phosphor's orange" },
        new("appearance", "Cursor", "terminal.cursor-style", "Cursor shape", "Until a program picks its own (vim's insert mode often does).",
            SettingKind.Choice, "block")
            {
                Choices = [new("Block", "block"), new("Bar", "bar"), new("Underline", "underline"), new("Hollow block", "block-hollow")],
            },
        new("appearance", "Cursor", "terminal.cursor-blink", "Blinking cursor", "Until a program says otherwise.", SettingKind.Toggle, false),
        new("appearance", "Cursor", "colors.cursor-opacity", "Cursor opacity",
            "1: solid. Lower lets the character under a block cursor show in its own color.", SettingKind.Number, 1.0)
            { Minimum = 0.1, Maximum = 1, Step = 0.05 },
        new("appearance", "Cursor", "terminal.cursor-style-at-prompt", "Cursor at the prompt",
            "While the shell waits for a command (needs shell integration). Programs and shell modes that pick their own still do.",
            SettingKind.Choice, "same")
            {
                Choices =
                [
                    new("Same as above", "same"), new("Bar", "bar", "Like a text box: Ghostty's default."), new("Block", "block"),
                    new("Underline", "underline"), new("Hollow block", "block-hollow"),
                ],
            },
        new("appearance", "Spacing", "terminal.padding-x", "Padding left and right", "Between the window's edge and the text.", SettingKind.Number, 6L)
            { Unit = "pt", Maximum = 100 },
        new("appearance", "Spacing", "terminal.padding-y", "Padding top and bottom", "Between the window's edge and the text.", SettingKind.Number, 6L)
            { Unit = "pt", Maximum = 100 },

        // ---- Layout
        new("layout", "Workspaces", "window.workspace-tabs", "Workspaces", "Tabs along the top, or a list in a sidebar. Applies to windows opened from now on.", SettingKind.Choice, "top")
            { Choices = [new("Along the top", "top"), new("In a sidebar", "left")] },
        new("layout", "Workspaces", "window.sidebar", "Sidebar", "With workspaces in a sidebar.", SettingKind.Choice, "full")
            { Choices = [new("Full", "full"), new("Icons only", "rail"), new("Auto-hide", "auto-hide", "Slides out when the pointer reaches the window's left edge.")] },
        new("layout", "Workspaces", "window.title-bar", "Title bar", "With workspaces in a sidebar.", SettingKind.Choice, "show")
            { Choices = [new("Shown", "show"), new("Auto-hide", "auto-hide", "Slides down when the pointer reaches the window's top edge.")] },
        new("layout", "Workspaces", "window.new-workspace-page", "Start page for new workspaces",
            "Choose a profile, a folder and a name when you open a workspace; Enter starts the default.", SettingKind.Toggle, true),
        new("layout", "Workspaces", "window.workspace-colors", "Colors for new workspaces", "For the ones you don't color yourself.",
            SettingKind.Choice, "off")
            {
                Choices =
                [
                    new("None", "off"),
                    new("By folder", "folder", "A workspace with a folder gets that folder's color, the same one every time."),
                    new("One after another", "cycle", "Each new workspace takes the next color."),
                ],
            },
        new("layout", "Panes", "window.tab-groups", "Tabs inside panes", "Each pane can hold several tabs.", SettingKind.Toggle, true),
        new("layout", "Panes", "window.group-tab-strip", "Pane tab strip", "The row of tabs above a pane.", SettingKind.Choice, "auto")
            { Choices = [new("Automatic", "auto", "Shown when a pane has two or more tabs."), new("Always", "always")] },
        new("layout", "Panes", "window.unfocused-split-opacity", "Unfocused panes",
            "How much of an unfocused pane shows through (1: not dimmed).", SettingKind.Number, 0.7)
            { Minimum = 0.15, Maximum = 1, Step = 0.05 },
        new("layout", "Panes", "window.unfocused-split-fill", "Dimming color", "What unfocused panes fade toward (default: their background).", SettingKind.Color, null)
            { Placeholder = "The pane's" },
        new("layout", "Panes", "window.divider-color", "Divider color", "The line between split panes.", SettingKind.Color, null)
            { Placeholder = "Phosphor's grey" },
        new("layout", "When you come back", "window.save-state", "Reopen windows",
            "Windows, workspaces, splits and tabs come back in their folders.", SettingKind.Choice, "always")
            { Choices = [new("Reopen them", "always"), new("Start fresh", "never")] },
        new("layout", "When you come back", "window.quit-after-last-window-closed", "When the last window closes",
            "", SettingKind.Choice, null)
            {
                Choices =
                [
                    new("Automatic", null, "Stays in the tray while the quick terminal's hotkey is set, so it keeps working; otherwise quits."),
                    new("Quit", true),
                    new("Stay in the tray", false, "Click the tray icon to open a window again."),
                ],
            },
        new("layout", "When you come back", "window.launch-at-login", "When you sign in to Windows", "", SettingKind.Choice, "off")
            {
                Choices =
                [
                    new("Don't start Phosphor", "off"),
                    new("Start in the tray", "tray", "No window: the quick terminal's hotkey works, and the tray icon opens your windows."),
                    new("Open my windows", "windows", "As they were when you left, or a new one."),
                ],
            },

        // ---- Terminal
        new("terminal", "Shell", "terminal.shell-integration", "Shell integration",
            "Marks prompts and commands in PowerShell, bash and zsh (WSL too), for jumping, copying output and failure bars.",
            SettingKind.Choice, "auto")
            { Choices = [new("On", "auto"), new("Off", "off")] },
        new("terminal", "Shell", "terminal.command-marks", "Command marks", "The bar beside a command's output.", SettingKind.Choice, "failures")
            { Choices = [new("Failed commands", "failures"), new("Every command", "all"), new("None", "none")] },
        new("terminal", "Shell", "terminal.close-on-exit", "When the program ends", "", SettingKind.Choice, "success")
            {
                Choices =
                [
                    new("Close if it succeeded", "success", "A failure stays on screen so you can read why; any key closes it."),
                    new("Always close", "always"),
                    new("Keep it open", "never", "Any key closes it."),
                ],
            },
        new("terminal", "Shell", "terminal.program-titles", "Programs name the tab",
            "Shells and programs set the tab's title (often the folder or the running command). Off: the profile's name.",
            SettingKind.Toggle, true),
        new("terminal", "Scrollback", "terminal.scrollback-lines", "Scrollback lines", "How far back you can scroll.",
            SettingKind.Number, (long)Config.DefaultScrollbackLines)
            { Minimum = 0, Maximum = Config.MaxScrollbackLines, Step = 1000 },
        new("terminal", "Mouse", "terminal.mouse-pointer", "Pointer over text", "", SettingKind.Choice, "arrow")
            { Choices = [new("Arrow", "arrow", "Like Windows Terminal."), new("Text I-beam", "text", "Like Ghostty.")] },
        new("terminal", "Mouse", "terminal.word-boundaries", "Word boundaries",
            "Characters that end a word when you double-click (space and tab always do). Empty: Ghostty's set.", SettingKind.Text, null)
            { Placeholder = "'\"│`|:;,()[]{}<>$" },
        new("terminal", "Mouse", "terminal.click-to-move", "Click to move the cursor",
            "At a shell prompt, clicking in the command you're typing moves the cursor there. Needs shell integration.",
            SettingKind.Toggle, true),
        new("terminal", "Mouse", "window.focus-follows-mouse", "Focus follows the pointer",
            "Moving the pointer into a pane focuses it.", SettingKind.Toggle, false),
        new("terminal", "Clipboard", "terminal.copy-on-select", "Copy on select",
            "Copies text as soon as you select it; the selection stays.", SettingKind.Toggle, false),
        new("terminal", "Clipboard", "terminal.paste-warning", "Paste warning", "Ask before pasting text with more than one line.",
            SettingKind.Choice, "auto")
            {
                Choices =
                [
                    new("Automatic", "auto", "When the program would take each line as Enter and run it; not when it marks pastes (bracketed paste)."),
                    new("Always", "multiline", "Whenever a paste has more than one line."),
                    new("Never", "never"),
                ],
            },
        new("terminal", "Clipboard", "terminal.large-paste-warning", "Large pastes", "Ask before pasting more than 5 KB.",
            SettingKind.Toggle, true),
        new("terminal", "Clipboard", "terminal.clipboard-write", "Programs may copy",
            "Copying with OSC 52, as tmux, neovim and remote shells do.", SettingKind.Toggle, true),
        new("terminal", "Clipboard", "terminal.clipboard-read", "Programs reading the clipboard",
            "Anything running, including a remote host, could read it.", SettingKind.Choice, "ask")
            {
                Choices =
                [
                    new("Ask each time", "ask", "Allow once, for the tab, or not."),
                    new("Allow", "allow"),
                    new("Deny", "deny"),
                ],
            },
        new("terminal", "Bell", "terminal.bell", "Bell", "When a program rings it (the BEL character), e.g. at a failed tab completion.",
            SettingKind.Choice, "attention")
            {
                Choices =
                [
                    new("Mark the tab", "attention", "A dot on its tab, and the taskbar button flashes, when you aren't looking at it."),
                    new("Flash the pane", "flash", "Marks the tab too."),
                    new("Nothing", "none"),
                ],
            },
        new("terminal", "Bell", "terminal.bell-sound", "Bell sound", "Plays Windows' default sound too.", SettingKind.Toggle, false),
        new("terminal", "Bell", "terminal.screen-flash-bell", "Screen flashes ring the bell",
            "Programs that flash the whole screen instead of ringing (Git Bash does) get the bell above.", SettingKind.Toggle, true),
        new("terminal", "Troubleshooting", "terminal.input-method", "Input methods",
            "Type Japanese, Chinese, Korean and more with Windows' input methods (IME). New: off by default for now.",
            SettingKind.Toggle, false),
        new("terminal", "Troubleshooting", "window.software-rendering", "Software rendering",
            "Draws with the processor instead of the graphics card: for broken graphics drivers, some virtual machines and remote desktop glitches. Slower. Applies to terminals opened from now on.",
            SettingKind.Toggle, false),

        // ---- Notifications
        new("notifications", "Long commands", "notify.command-finished", "Long command finishes",
            "In a pane you weren't looking at.", SettingKind.Choice, "badge")
            { Choices = NotifyChoices },
        new("notifications", "Long commands", "notify.command-finished-after", "Counts as long after",
            "Seconds, or with a unit: 45s, 2m.", SettingKind.Text, 10L)
            { Placeholder = "10", NumbersAsNumbers = true },
        new("notifications", "Programs", "notify.programs", "Program notifications",
            "Sent with OSC 9 or OSC 777, e.g. by a build script.", SettingKind.Choice, "badge")
            { Choices = NotifyChoices },

        // ---- Quick terminal
        new("quick", "Summon", "quick-terminal.hotkey", "Hotkey", "Shows and hides it from anywhere, e.g. win+grave or ctrl+alt+t.", SettingKind.Text, "win+grave"),
        new("quick", "Placement", "quick-terminal.position", "Position", "", SettingKind.Choice, "top")
            { Choices = [new("Top", "top"), new("Bottom", "bottom"), new("Left", "left"), new("Right", "right"), new("Center", "center")] },
        new("quick", "Placement", "quick-terminal.screen", "Screen", "", SettingKind.Choice, "mouse")
            { Choices = [new("With the pointer", "mouse", "Opens on the screen the pointer is on."), new("Main", "main", "Always opens on the main screen.")] },
        new("quick", "Placement", "quick-terminal.size", "Size",
            "A percentage or pixels (50%, 600px) along its edge; defaults: 50%, or 70% by 60% centered.", SettingKind.Text, null)
            { Placeholder = "50%" },
        new("quick", "Behavior", "quick-terminal.autohide", "Hide when it loses focus", "", SettingKind.Toggle, true),
    ];

    /// <summary>A profile's own fields (keys relative to its <c>[profile.&lt;id&gt;]</c> section).</summary>
    public static IReadOnlyList<SettingDef> ProfileFields { get; } =
    [
        new("profiles", "General", "name", "Name", "Shown in menus and tabs.", SettingKind.Text, null),
        new("profiles", "General", "command", "Command", "What runs in its tabs: a shell, or any program.", SettingKind.Text, null),
        new("profiles", "General", "working-directory", "Starting folder", "~ (your home), a path, or %VARIABLES%.", SettingKind.Text, null)
            { Placeholder = "~" },
        new("profiles", "General", "icon", "Icon", "An image file for its tabs (default: the program's own icon).", SettingKind.Text, null),
        new("profiles", "General", "hidden", "Hide it", "Kept out of the menus (its tabs still reopen).", SettingKind.Toggle, false),
    ];

    /// <summary>Where Phosphor looks for shells (<c>[discover]</c>): each on by default.</summary>
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
    };

    public static SettingDef? Find(string key) => All.FirstOrDefault(s => s.Key == key);

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
        new(null, null, "window.tab-groups", ["window.group-tab-strip"]),
        new(null, null, "window.unfocused-split-opacity", ["window.unfocused-split-fill"]),
        new(null, null, "terminal.paste-warning", ["terminal.large-paste-warning"]),
        new("Programs using the clipboard", "Copying and reading with OSC 52, as tmux, neovim and remote shells do.", null,
            ["terminal.clipboard-write", "terminal.clipboard-read"]),
        new(null, null, "notify.command-finished", ["notify.command-finished-after"]),
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
