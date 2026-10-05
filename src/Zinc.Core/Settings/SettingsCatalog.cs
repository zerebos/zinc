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

    private static readonly SettingChoice[] NotifyChoices =
    [
        new("Tab dot", "badge", "A dot on its tab; the taskbar button flashes if Zinc is in the background."),
        new("Notification", "toast", "A dot on its tab, and a Windows notification if Zinc is in the background."),
        new("Always notify", "toast-always", "A Windows notification whenever you aren't looking at that pane, even in Zinc."),
        new("None", "never", "No dot, no notification."),
    ];

    public static IReadOnlyList<SettingDef> All { get; } =
    [
        // ---- Appearance
        new("appearance", "Font", "font.family", "Font", "Any monospace font; Nerd Fonts show prompt icons.", SettingKind.Font, "Cascadia Mono NF"),
        new("appearance", "Font", "font.size", "Size", "Ctrl+0 returns to this size.", SettingKind.Number, 10.5)
            { Unit = "pt", Minimum = 4, Maximum = 72, Step = 0.5 },
        new("appearance", "Font", "font.adjust-cell-height", "Line height",
            "More room between lines: a percentage (20%) or pixels (2). Negative for less. Text stays centered.", SettingKind.Text, null)
            { Placeholder = "0", NumbersAsNumbers = true },
        new("appearance", "Font", "font.builtin-glyphs", "Built-in glyphs",
            "Zinc draws box lines, blocks and Powerline shapes itself, so they join up in any font.",
            SettingKind.Toggle, true),
        new("appearance", "Font", "font.adjust-box-thickness", "Line thickness",
            "Thicker or thinner built-in lines: pixels (1, -1) or a percentage (20%).", SettingKind.Text, null)
            { Placeholder = "0", NumbersAsNumbers = true },
        new("appearance", "Colors", "colors.theme", "Color scheme",
            "About 650 are included; yours go in the themes folder next to config.toml.", SettingKind.Scheme, null),
        new("appearance", "Colors", "colors.palette-generate", "Match 256 colors to the scheme",
            "Programs that use the extended 256 colors (vim and htop themes) get shades made from the scheme's 16, not the standard ones.",
            SettingKind.Toggle, false),
        new("appearance", "Colors", "colors.palette-harmonious", "Light schemes: shades from background to text",
            "Off: dark to light, as usual, which programs expect.", SettingKind.Toggle, false),
        new("appearance", "Colors", "colors.background", "Background", "", SettingKind.Color, null),
        new("appearance", "Colors", "colors.foreground", "Text", "", SettingKind.Color, null),
        new("appearance", "Colors", "colors.cursor", "Cursor", "", SettingKind.Color, null),
        new("appearance", "Colors", "colors.cursor-text", "Cursor text",
            "The character under a block cursor (without either, the background color).", SettingKind.Color, null),
        new("appearance", "Colors", "colors.selection-background", "Selection",
            "Behind selected text (without either, selection inverts the colors).", SettingKind.Color, null),
        new("appearance", "Colors", "colors.selection-foreground", "Selected text",
            "Without either, the text keeps its color.", SettingKind.Color, null),
        new("appearance", "Readability", "colors.minimum-contrast", "Minimum contrast",
            "Text that reads poorly against its background turns black or white. 1: off; 4.5: the usual accessibility level; 21: always black or white.",
            SettingKind.Number, 1.0)
            { Minimum = 1, Maximum = 21, Step = 0.5 },
        new("appearance", "Readability", "colors.bold-is-bright", "Bold in bright colors",
            "Bold text in the first eight colors shows their bright versions, as older terminals did.", SettingKind.Toggle, false),
        new("appearance", "Readability", "colors.bold", "Bold text color", "For bold text in the default text color.", SettingKind.Color, null)
            { Placeholder = "Same as the text" },
        new("appearance", "Readability", "colors.faint-opacity", "Faint text",
            "How bright dim text is (1: as bright as other text).", SettingKind.Number, 0.5)
            { Minimum = 0.1, Maximum = 1, Step = 0.05 },
        new("appearance", "Readability", "colors.find-highlight", "Find highlight", "Behind each match of Find.",
            SettingKind.Color, null) { Placeholder = "Zinc's yellow" },
        new("appearance", "Readability", "colors.find-highlight-current", "Current match", "Behind the match Find is on.",
            SettingKind.Color, null) { Placeholder = "Zinc's orange" },
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
            "While the shell waits for a command. A program's own cursor still wins. Needs shell integration.",
            SettingKind.Choice, "same")
            {
                Choices =
                [
                    new("Same as above", "same"), new("Bar", "bar", "Like a text box."), new("Block", "block"),
                    new("Underline", "underline"), new("Hollow block", "block-hollow"),
                ],
            },
        new("appearance", "Window", "window.appearance", "Light or dark",
            "Windows, menus and dialogs. The terminal keeps its color scheme. Restart Zinc to apply.", SettingKind.Choice, "system")
            {
                Choices =
                [
                    new("Same as Windows", "system", "Windows Settings > Personalization > Colors > Choose your mode."),
                    new("Light", "light"),
                    new("Dark", "dark"),
                ],
            },
        new("appearance", "Window", "window.accent", "Accent color",
            "Selections, focus lines, switches and buttons. Restart Zinc to apply.", SettingKind.Choice, "zinc")
            {
                Choices =
                [
                    new("Zinc green", "zinc"),
                    new("Windows' accent", "windows", "The one in Windows Settings > Personalization > Colors."),
                ],
            },
        new("appearance", "Background", "window.backdrop", "Window background", "What's behind everything in the window: tabs, sidebar and terminal.",
            SettingKind.Choice, "mica")
            {
                Choices =
                [
                    new("Mica", "mica", "Not see-through: your wallpaper's tint, as Windows apps use."),
                    new("Acrylic (frosted)", "acrylic", "The windows behind show, blurred."),
                    new("Clear", "none", "The windows behind show as they are."),
                ],
            },
        new("appearance", "Background", "window.backdrop-tint", "Window tint",
            "How much color an acrylic or clear background carries: lower is more see-through.",
            SettingKind.Number, 0.3)
            { Minimum = 0, Maximum = 1, Step = 0.05 },
        new("appearance", "Background", "window.backdrop-color", "Window tint color",
            "The color an acrylic or clear background is tinted with.", SettingKind.Color, null)
            { NullLabel = "Use the terminal's background", Placeholder = "The terminal's background" },
        new("appearance", "Background", "colors.background-opacity", "Terminal background opacity",
            "Lower lets the window background show behind the text (1: solid). Colored cells stay solid.",
            SettingKind.Number, 1.0)
            { Minimum = 0, Maximum = 1, Step = 0.05 },
        new("appearance", "Shaders", "shaders.files", "Custom shaders",
            "Effects drawn over the terminal (CRT screens, glow, cursor trails): GLSL files written for Ghostty or Shadertoy, run in order.",
            SettingKind.Files, null),
        new("appearance", "Shaders", "shaders.animation", "Animation",
            "For animated shaders. The others redraw only when the terminal changes.", SettingKind.Choice, true)
        {
            Choices =
            [
                new("While focused", true, "Unfocused, they pause where they are; the text under them still updates."),
                new("Always", "always", "Every terminal with shaders keeps redrawing, focused or not."),
                new("Never", false, "They hold still, cursor effects included, and are drawn only when the terminal changes."),
            ],
        },
        new("appearance", "Spacing", "terminal.padding-x", "Padding left and right", "Between the window's edge and the text.", SettingKind.Number, 6L)
            { Unit = "pt", Maximum = 100 },
        new("appearance", "Spacing", "terminal.padding-y", "Padding top and bottom", "Between the window's edge and the text.", SettingKind.Number, 6L)
            { Unit = "pt", Maximum = 100 },
        new("appearance", "Spacing", "terminal.padding-balance", "Even padding",
            "Splits the leftover space between both sides. Off: it all goes to the right and bottom.",
            SettingKind.Toggle, false),

        // ---- Layout
        new("layout", "Workspaces", "window.workspace-tabs", "Workspaces", "Tabs along the top, or a list in a sidebar. Applies to windows opened from now on.", SettingKind.Choice, "top")
            { Choices = [new("Along the top", "top"), new("In a sidebar", "left")] },
        new("layout", "Workspaces", "window.sidebar", "Sidebar", "Only when \"Workspaces\" is \"In a sidebar\".", SettingKind.Choice, "full")
            { Choices = [new("Full", "full"), new("Icons only", "rail"), new("Auto-hide", "auto-hide", "Slides out when the pointer reaches the window's left edge.")] },
        new("layout", "Workspaces", "window.title-bar", "Title bar", "Only when \"Workspaces\" is \"In a sidebar\".", SettingKind.Choice, "show")
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
        new("layout", "Panes", "window.pane-resize-step", "Resize step",
            "How far a resize key moves the divider, as a share of the panes it splits.", SettingKind.Number, 5L)
            { Unit = "%", Minimum = 1, Maximum = 50, Step = 1 },
        new("layout", "Panes", "window.unfocused-split-opacity", "Unfocused panes",
            "How bright unfocused panes stay (1: not dimmed).", SettingKind.Number, 0.7)
            { Minimum = 0.15, Maximum = 1, Step = 0.05 },
        new("layout", "Panes", "window.unfocused-split-fill", "Dimming color", "What unfocused panes fade toward.", SettingKind.Color, null)
            { Placeholder = "The pane's background" },
        new("layout", "Panes", "window.divider-color", "Divider color", "The line between split panes.", SettingKind.Color, null)
            { Placeholder = "Zinc's grey" },
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
                    new("Don't start Zinc", "off"),
                    new("Start in the tray", "tray", "No window: the quick terminal's hotkey works, and the tray icon opens your windows."),
                    new("Open my windows", "windows", "As they were when you left, or a new one."),
                ],
            },
        new("layout", "When you come back", "window.explorer-menu", "Open in Zinc from File Explorer",
            "Adds \"Open in Zinc\" to the right-click menu of folders and drives. On Windows 11 it's under \"Show more options\".",
            SettingKind.Toggle, false),
        new("layout", "Closing", "window.confirm-close", "Ask before closing a running command",
            "When you close a tab, pane or window while a command still runs. Needs shell integration to know.", SettingKind.Toggle, true),
        new("layout", "Closing", "window.confirm-quit", "Ask before quitting with a running command",
            "When you quit Zinc (or restart it for an update) while a command still runs.", SettingKind.Toggle, true),

        // ---- Terminal
        new("terminal", "Shell", "terminal.shell-integration", "Shell integration",
            "Marks prompts and commands in PowerShell, bash and zsh (WSL too), so you can jump between prompts, copy a command's output and see which commands failed.",
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
        new("terminal", "Shell", "terminal.reload-environment", "Pick up environment changes",
            "New tabs read PATH and other variables from Windows as they start, so an installer's changes work without restarting Zinc.",
            SettingKind.Toggle, true),
        new("terminal", "Shell", "terminal.program-titles", "Program titles",
            "Shells and programs set the tab's title (often the folder or the running command). Off: the profile's name.",
            SettingKind.Toggle, true),
        new("terminal", "Shell", "terminal.shell-titles", "Folder and command as the title",
            "The folder at the prompt, the running command otherwise. A program's own title still wins. Needs shell integration.",
            SettingKind.Toggle, true),
        new("terminal", "Scrollback", "terminal.scrollback-lines", "Scrollback lines", "How far back you can scroll.",
            SettingKind.Number, (long)Config.DefaultScrollbackLines)
            { Minimum = 0, Maximum = Config.MaxScrollbackLines, Step = 1000 },
        new("terminal", "Mouse", "terminal.mouse-pointer", "Pointer over text", "", SettingKind.Choice, "arrow")
            { Choices = [new("Arrow", "arrow"), new("I-beam", "text")] },
        new("terminal", "Mouse", "terminal.word-boundaries", "Word boundaries",
            "Characters that end a word when you double-click (space and tab always do). Empty: the ones shown.", SettingKind.Text, null)
            { Placeholder = "'\"│`|:;,()[]{}<>$" },
        new("terminal", "Mouse", "terminal.click-to-move", "Click to move the cursor",
            "At a shell prompt, clicking in the command you're typing moves the cursor there. Needs shell integration.",
            SettingKind.Toggle, true),
        new("terminal", "Mouse", "terminal.link-click", "Open links with", "Web links and files shown in the terminal.",
            SettingKind.Choice, "ctrl")
            {
                Choices =
                [
                    new("Ctrl+click", "ctrl", "Holding Ctrl underlines the links."),
                    new("Alt+click", "alt", "Holding Alt underlines the links."),
                    new("A click", "click", "Dragging across a link still selects it; a program using the mouse still gets the click."),
                ],
            },
        new("terminal", "Mouse", "terminal.middle-click-paste", "Middle-click pastes",
            "Unless the program uses the mouse.", SettingKind.Toggle, false),
        new("terminal", "Mouse", "window.focus-follows-mouse", "Focus follows mouse",
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
        new("terminal", "Bell", "terminal.bell", "Bell", "When a program rings it, e.g. at a failed tab completion.",
            SettingKind.Choice, "attention")
            {
                Choices =
                [
                    new("Tab dot", "attention", "A dot on its tab, and the taskbar button flashes, when you aren't looking at it."),
                    new("Flash the pane", "flash", "Marks the tab too."),
                    new("None", "none"),
                ],
            },
        new("terminal", "Bell", "terminal.bell-sound", "Bell sound", "Plays Windows' default sound too.", SettingKind.Toggle, false),
        new("terminal", "Bell", "terminal.screen-flash-bell", "Visual bell",
            "Programs that flash the screen instead of beeping (Git Bash does) ring Zinc's bell instead, as set in \"Bell\".", SettingKind.Toggle, true),
        new("terminal", "Command line", "cli.remote-control", "Remote control",
            "Lets zn, Zinc's command-line tool, open, arrange and close tabs, type into them and read them. Any program running as you could use it.",
            SettingKind.Toggle, false),
        new("terminal", "Command line", "cli.path", "Use zn in other terminals",
            "Adds zn to your PATH, so other terminals find it too (Zinc's own terminals always do). Shells started from now on see it.",
            SettingKind.Toggle, false),
        new("terminal", "Troubleshooting", "terminal.input-method", "Input methods",
            "Type Japanese, Chinese, Korean and more with Windows' input methods (IME). Turn it off if keys behave oddly.",
            SettingKind.Toggle, true),
        new("about", "Updates", "updates.check", "Check for updates automatically",
            "Once a day, and new versions download in the background; you choose when to restart. Only for Zinc installed with its installer.",
            SettingKind.Toggle, true),
        new("about", "Troubleshooting", "debug.logging", "Debug log",
            "Keeps a record of what Zinc does, to send with a bug report. Turn it on, make the problem happen, then copy the log.",
            SettingKind.Toggle, false),
        new("terminal", "Troubleshooting", "window.low-latency-drawing", "Low-latency drawing",
            "What you type shows up about one screen refresh sooner. Turn it off if a terminal draws wrong or stops drawing, and tell us. Applies to terminals opened from now on.",
            SettingKind.Toggle, true),
        new("terminal", "Troubleshooting", "window.free-hidden-memory", "Free memory of hidden tabs",
            "Background tabs give back their graphics memory and rebuild it when you return. Turn it off if switching tabs flashes. Only with \"Low-latency drawing\".",
            SettingKind.Toggle, true),
        new("terminal", "Troubleshooting", "window.free-hidden-after", "Free it after",
            "Seconds, or with a unit: 45s, 2m.", SettingKind.Text, 60L)
            { Placeholder = "60", NumbersAsNumbers = true },
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
        new("notifications", "Long commands", "notify.command-finished-sound", "Sound",
            "Plays when a long command finishes out of view (a Windows notification has its own).", SettingKind.Choice, "off")
            {
                Choices = [new("None", "off"), new("Windows' notification sound", "notification"), new("Windows' default beep", "beep")],
            },
        new("notifications", "Programs", "notify.programs", "Program notifications",
            "When a program asks for one, e.g. a build script finishing (OSC 9 or 777).", SettingKind.Choice, "badge")
            { Choices = NotifyChoices },
        new("notifications", "Dot colors", "notify.finished-color", "Finished",
            "A command finished, or a program sent a notification.", SettingKind.Color, null)
            { NullLabel = "Use Zinc's green", Placeholder = "Zinc's green" },
        new("notifications", "Dot colors", "notify.bell-color", "Bell", "A program rang the bell.", SettingKind.Color, null)
            { NullLabel = "Use Zinc's amber", Placeholder = "Zinc's amber" },
        new("notifications", "Dot colors", "notify.failed-color", "Failed", "A command failed.", SettingKind.Color, null)
            { NullLabel = "Use Zinc's red", Placeholder = "Zinc's red" },

        // ---- Quick terminal
        new("quick", "Summon", "quick-terminal.hotkey", "Hotkey", "Shows and hides it from anywhere, e.g. win+grave or ctrl+alt+t.", SettingKind.Text, "win+grave"),
        new("quick", "Placement", "quick-terminal.position", "Position", "", SettingKind.Choice, "center")
            { Choices = [new("Top", "top"), new("Bottom", "bottom"), new("Left", "left"), new("Right", "right"), new("Center", "center")] },
        new("quick", "Placement", "quick-terminal.screen", "Screen", "", SettingKind.Choice, "mouse")
            { Choices = [new("With the pointer", "mouse", "Opens on the screen the pointer is on."), new("Main", "main", "Always opens on the main screen.")] },
        new("quick", "Placement", "quick-terminal.size", "Size",
            "A percentage or pixels (50%, 600px) along its edge. Empty: 50%, or 70% by 60% at \"Center\".", SettingKind.Text, null)
            { Placeholder = "50%" },
        new("quick", "Behavior", "quick-terminal.autohide", "Hide when it loses focus", "", SettingKind.Toggle, true),
        new("quick", "Behavior", "quick-terminal.animation-duration", "Animation", "How long it takes to fade in and out; 0 makes it appear at once.",
            SettingKind.Number, 0.2) { Unit = "seconds", Minimum = 0, Maximum = 2, Step = 0.05 },
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
        ["shaders.folders"] = "Folders of shaders to choose from: added and taken out on the shaders page (the library's … menu).",
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
