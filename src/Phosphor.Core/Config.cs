using System.Globalization;
using Phosphor.Ghostty;
using Phosphor.Terminal;

namespace Phosphor;

public enum GroupTabStrip
{
    /// <summary>Only on panes holding two or more tabs.</summary>
    Auto,

    /// <summary>On every pane.</summary>
    Always,
}

public enum WorkspaceTabs
{
    /// <summary>In the title bar.</summary>
    Top,

    /// <summary>In a column on the window's left, under a plain title bar.</summary>
    Left,
}

/// <summary>Whether Phosphor loads its shell integration into the shells it starts. Config: <c>shell-integration</c>.</summary>
public enum ShellIntegrationMode
{
    /// <summary>PowerShell, bash and zsh get it (after the user's own startup files).</summary>
    Auto,

    /// <summary>Shells start exactly as they would anywhere else.</summary>
    Off,
}

/// <summary>Which commands get a bar in the pane's left edge (shell integration). Config: <c>command-marks</c>.</summary>
public enum CommandMarks
{
    /// <summary>Failed commands only (and scrollbar ticks for every prompt).</summary>
    Failures,

    /// <summary>Every command that ran: green, or red if it failed.</summary>
    All,

    /// <summary>No bars and no ticks.</summary>
    None,
}

/// <summary>
/// How Phosphor tells you about something that happened out of view (a long command finishing, a
/// program's notification). Config: <c>[notify] command-finished</c> and <c>programs</c>.
/// </summary>
public enum NotifyMode
{
    /// <summary>A dot on its tab; the taskbar button flashes if the window is in the background.</summary>
    Badge,

    /// <summary>The dot, plus a Windows notification when Phosphor is in the background.</summary>
    Toast,

    /// <summary>The dot, plus a Windows notification whenever it's out of view, Phosphor in front or not.</summary>
    ToastAlways,
    Never,
}

/// <summary>What happens to a pane when its program ends. Config: <c>close-on-exit = success | always | never</c>.</summary>
public enum CloseOnExit
{
    /// <summary>Closes after a clean exit (code 0); a failure stays on screen so it can be read.</summary>
    Success,
    Always,
    Never,
}

/// <summary>Starting when you sign in. Config: <c>launch-at-login = off | tray | windows</c>.</summary>
public enum LaunchAtLogin
{
    Off,

    /// <summary>No window: the tray icon and the quick terminal's hotkey are ready.</summary>
    Tray,

    /// <summary>The windows you had open (or a new one).</summary>
    Windows,
}

/// <summary>Programs reading the clipboard (OSC 52). Config: <c>clipboard-read = ask | allow | deny</c>.</summary>
public enum ClipboardAccess
{
    /// <summary>Each read asks: allow once, for the tab, or deny.</summary>
    Ask,
    Allow,
    Deny,
}

/// <summary>What the bell (BEL) does. Config: <c>bell = attention | flash | none</c>.</summary>
public enum BellMode
{
    /// <summary>A dot on the tab (and the taskbar button flashes) when you aren't looking at it.</summary>
    Attention,

    /// <summary>The pane flashes too.</summary>
    Flash,
    None,
}

/// <summary>The title bar with <c>workspace-tabs = left</c>. Config: <c>title-bar = show | auto-hide</c>.</summary>
public enum TitleBarMode
{
    Show,

    /// <summary>Hidden until the pointer reaches the window's top edge.</summary>
    AutoHide,
}

/// <summary>The workspace list with <c>workspace-tabs = left</c>. Config: <c>sidebar = full | rail | auto-hide</c>.</summary>
public enum SidebarMode
{
    Full,

    /// <summary>A narrow column of workspace icons.</summary>
    Rail,

    /// <summary>Hidden until the pointer reaches the window's left edge.</summary>
    AutoHide,
}

/// <summary>
/// One resolved set of settings (the global ones, or a profile's). <see cref="ConfigFile"/>
/// reads config.toml into it; <see cref="Apply"/> parses one value by its Ghostty-style key,
/// which is also how color scheme files (Ghostty's theme format) are read.
/// </summary>
public sealed record Config
{
    /// <summary>Default font size in points (≈14 DIPs).</summary>
    public const float DefaultFontSize = 10.5f;

    /// <summary>Primary font family; glyphs it lacks fall back through DirectWrite.</summary>
    public string FontFamily { get; init; } = "Cascadia Mono NF"; // ships with Phosphor (Fonts)

    /// <summary>
    /// Box drawing, block elements, Symbols for Legacy Computing, Powerline and Braille drawn by
    /// Ghostty's sprite rasterizer (seamless in any font) rather than taken from the font.
    /// Config: <c>[font] builtin-glyphs = true | false</c>.
    /// </summary>
    public bool BuiltinGlyphs { get; init; } = true;

    /// <summary>
    /// Makes the built-in glyphs' lines thicker or thinner than the font's underline (Ghostty's
    /// adjust-box-thickness): whole pixels, or a percentage. Config: <c>[font] adjust-box-thickness = 1 | "20%"</c>.
    /// </summary>
    public (double Amount, bool Percent) AdjustBoxThickness { get; init; }

    /// <summary><paramref name="thickness"/> pixels, adjusted by <see cref="AdjustBoxThickness"/> (at least 1).</summary>
    public int AdjustedBoxThickness(int thickness) => Math.Max(1, AdjustBoxThickness switch
    {
        (0, _) => thickness,
        (var percent, true) => (int)Math.Round(thickness * (1 + percent / 100)),
        (var pixels, false) => thickness + (int)Math.Round(pixels),
    });

    /// <summary>Font size in points, like Ghostty and Windows Terminal.</summary>
    public float FontSize { get; init; } = DefaultFontSize;

    /// <summary>Font size in DIPs (96 per inch; a point is 1/72 inch).</summary>
    public float FontSizeDip => FontSize * 96f / 72f;

    /// <summary>Command line to run; null means pick a default shell.</summary>
    public string? Command { get; init; }

    public TerminalTheme Theme { get; init; } = TerminalTheme.Default;

    /// <summary>With a light/dark theme pair: whether its dark side is the one in force. Null for a single theme.</summary>
    public bool? PairedDark { get; init; }

    /// <summary>
    /// Whether programs are told the colors are dark (CSI ? 996 n, mode 2031): the side of a
    /// light/dark pair in force, else whether the background is dark.
    /// </summary>
    public bool IsDark => PairedDark ?? 0.2126 * Theme.Background.R + 0.7152 * Theme.Background.G + 0.0722 * Theme.Background.B < 128;

    /// <summary>
    /// Pointer over the terminal: an arrow (default, like Windows Terminal) or a text
    /// I-beam (like Ghostty, kitty, WezTerm). Config: <c>mouse-pointer = arrow | text</c>.
    /// </summary>
    public bool TextPointer { get; init; }

    /// <summary>Programs may set the clipboard with OSC 52. Config: <c>clipboard-write = allow | deny</c>.</summary>
    public bool ClipboardWrite { get; init; } = true;

    /// <summary>A selection is copied as soon as it's made (and stays selected). Config: <c>copy-on-select = true</c>.</summary>
    public bool CopyOnSelect { get; init; }

    /// <summary>When a paste asks first. Config: <c>paste-warning = auto | multiline | never</c>.</summary>
    public Phosphor.Terminal.PasteWarning PasteWarning { get; init; }

    /// <summary>A paste over 5 KB asks first. Config: <c>large-paste-warning = true | false</c>.</summary>
    public bool LargePasteWarning { get; init; } = true;

    /// <summary>
    /// Characters that end a word when you double-click (space and tab always do); null: Ghostty's
    /// own set. Config: <c>word-boundaries = "'\"|:;,()[]{}&lt;&gt;$"</c>.
    /// </summary>
    public string? WordBoundaries { get; init; }

    /// <summary>Moving the pointer into a pane focuses it. Config: <c>[window] focus-follows-mouse = true</c> (global).</summary>
    public bool FocusFollowsMouse { get; init; }

    /// <summary>The cursor until a program picks its own. Config: <c>cursor-style = block | bar | underline | block-hollow</c>.</summary>
    public Phosphor.Ghostty.TerminalCursorStyle CursorStyle { get; init; } = Phosphor.Ghostty.TerminalCursorStyle.Block;

    /// <summary>Whether that cursor blinks. Config: <c>cursor-blink = true</c>.</summary>
    public bool CursorBlink { get; init; }

    /// <summary>Lines kept above the screen. Config: <c>scrollback-lines = 10000</c>.</summary>
    public int ScrollbackLines { get; init; } = DefaultScrollbackLines;

    public const int DefaultScrollbackLines = 10_000;

    public const int MaxScrollbackLines = 100_000;

    /// <summary>What happens when the program ends. Config: <c>close-on-exit</c>.</summary>
    public CloseOnExit CloseOnExit { get; init; }

    /// <summary>What the bell does. Config: <c>bell</c>.</summary>
    public BellMode Bell { get; init; }

    /// <summary>The bell also plays Windows' default sound. Config: <c>bell-sound = true</c>.</summary>
    public bool BellSound { get; init; }

    /// <summary>A program's screen flash (a "visible bell") rings the bell instead. Config: <c>screen-flash-bell = false</c>.</summary>
    public bool ScreenFlashBell { get; init; } = true;

    /// <summary>Programs may set the tab's title (OSC 0/2); off: the profile's name. Config: <c>program-titles = false</c>.</summary>
    public bool ProgramTitles { get; init; } = true;

    /// <summary>
    /// Whether programs may read the clipboard with OSC 52: anything running in the terminal, e.g.
    /// over ssh, could read it, so by default each read asks. Config: <c>clipboard-read = ask | allow | deny</c>.
    /// </summary>
    public ClipboardAccess ClipboardRead { get; init; } = ClipboardAccess.Ask;

    /// <summary>
    /// Whether closing the last window quits Phosphor (global only). Null (the default): keep
    /// running in the tray while the quick terminal hotkey works, so it still summons one.
    /// Config: <c>quit-after-last-window-closed = true | false</c>.
    /// </summary>
    public bool? QuitAfterLastWindowClosed { get; init; }

    /// <summary>
    /// A pane can hold several tabs (true, the default); false keeps one terminal per pane, and
    /// "new tab" opens a workspace instead. Config: <c>tab-groups = true | false</c> (global).
    /// </summary>
    public bool TabGroups { get; init; } = true;

    /// <summary>
    /// Draw with the CPU (WARP) instead of the graphics card: for broken GPU drivers, some virtual
    /// machines and remote desktop glitches. Config: <c>[window] software-rendering = true</c> (global).
    /// </summary>
    public bool SoftwareRendering { get; init; }

    /// <summary>When a pane shows its own tab strip. Config: <c>group-tab-strip = auto | always</c> (global).</summary>
    public GroupTabStrip GroupTabStrip { get; init; }

    /// <summary>Where a window's workspaces are listed. Config: <c>workspace-tabs = top | left</c> (global).</summary>
    public WorkspaceTabs WorkspaceTabs { get; init; }

    /// <summary>Config: <c>[notify] command-finished = badge | toast | toast-always | never</c> (global).</summary>
    public NotifyMode NotifyCommandFinished { get; init; }

    /// <summary>Notifications programs send (OSC 9, OSC 777). Config: <c>[notify] programs = badge | toast | toast-always | never</c> (global).</summary>
    public NotifyMode NotifyPrograms { get; init; }

    /// <summary>How long a command must run to count as long. Config: <c>[notify] command-finished-after = 10</c> (seconds, or "30s", "2m"; global).</summary>
    public TimeSpan NotifyCommandFinishedAfter { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Marks for shell-integrated commands. Config: <c>command-marks = failures | all | none</c>.</summary>
    public CommandMarks CommandMarks { get; init; }

    /// <summary>Phosphor's shell integration, loaded automatically. Config: <c>shell-integration = auto | off</c>.</summary>
    public ShellIntegrationMode ShellIntegration { get; init; }

    /// <summary>The title bar with the workspace list. Config: <c>title-bar = show | auto-hide</c> (global).</summary>
    public TitleBarMode TitleBar { get; init; }

    /// <summary>How the workspace list shows. Config: <c>sidebar = full | rail | auto-hide</c> (global).</summary>
    public SidebarMode Sidebar { get; init; }

    /// <summary>
    /// How visible panes without focus stay, 0.15–1 (1 = not dimmed). Config, as in Ghostty and
    /// with its default: <c>unfocused-split-opacity = 0.7</c> (global).
    /// </summary>
    public double UnfocusedSplitOpacity { get; init; } = 0.7;

    /// <summary>
    /// Whether windows, workspaces and tabs reopen as they were (session.json; not their
    /// contents). Config, as in Ghostty: <c>window-save-state = default | always | never</c>
    /// (global; default = always).
    /// </summary>
    public bool RestoreSession { get; init; } = true;

    /// <summary>
    /// A new workspace opens on a page for choosing its profile, folder and name (Enter starts the
    /// default). Config: <c>[window] new-workspace-page = false</c> starts the default at once.
    /// </summary>
    public bool NewWorkspacePage { get; init; } = true;

    /// <summary>Colors for new workspaces you don't color yourself. Config: <c>[window] workspace-colors = off | folder | cycle</c>.</summary>
    public Phosphor.Panes.WorkspaceColorMode WorkspaceColors { get; init; }

    /// <summary>Starting Phosphor when you sign in to Windows. Config: <c>[window] launch-at-login = off | tray | windows</c>.</summary>
    public LaunchAtLogin LaunchAtLogin { get; init; }

    /// <summary>What unfocused panes are dimmed toward; null = each pane's background. Config: <c>unfocused-split-fill</c> (global).</summary>
    public GhosttyColorRgb? UnfocusedSplitFill { get; init; }

    /// <summary>The quick terminal (global only; ignored inside profiles).</summary>
    public QuickTerminalSettings QuickTerminal { get; init; } = new();

    /// <summary>Padding around the grid, in DIPs.</summary>
    public float PaddingLeft { get; init; } = 8;
    public float PaddingRight { get; init; } = 8;
    public float PaddingTop { get; init; } = 8;
    public float PaddingBottom { get; init; } = 8;

    public static string DefaultPath => ConfigFile.DefaultPath;

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
            "builtin-glyphs" when bool.TryParse(value, out var builtin) => this with { BuiltinGlyphs = builtin },
            "adjust-box-thickness" when TryAdjustment(value, out var adjust) => this with { AdjustBoxThickness = adjust },
            "font-size" when TryFloat(value, out var size) && size > 0 => this with { FontSize = size },
            "command" when value.Length > 0 => this with { Command = value },
            "working-directory" when value.Length > 0 => this with { WorkingDirectory = value },
            "icon" when value.Length > 0 => this with { Icon = value },
            "mouse-pointer" when value is "arrow" or "text" => this with { TextPointer = value == "text" },
            "quit-after-last-window-closed" when bool.TryParse(value, out var quit) =>
                this with { QuitAfterLastWindowClosed = quit },
            "tab-groups" when bool.TryParse(value, out var groups) => this with { TabGroups = groups },
            "software-rendering" when bool.TryParse(value, out var software) => this with { SoftwareRendering = software },
            "group-tab-strip" when Enum.TryParse<GroupTabStrip>(value, true, out var strip) && !int.TryParse(value, out _) =>
                this with { GroupTabStrip = strip },
            "unfocused-split-opacity" when double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var opacity) =>
                this with { UnfocusedSplitOpacity = Math.Clamp(opacity, 0.15, 1.0) },
            "workspace-colors" when TryMode<Phosphor.Panes.WorkspaceColorMode>(value, out var colors) => this with { WorkspaceColors = colors },
            "new-workspace-page" when bool.TryParse(value, out var page) => this with { NewWorkspacePage = page },
            "launch-at-login" when TryMode<LaunchAtLogin>(value, out var login) => this with { LaunchAtLogin = login },
            "window-save-state" when value is "default" or "always" or "never" => this with { RestoreSession = value != "never" },
            "unfocused-split-fill" when TryColor(value, out var fill) => this with { UnfocusedSplitFill = fill },
            "workspace-tabs" when Enum.TryParse<WorkspaceTabs>(value, true, out var tabs) && !int.TryParse(value, out _) =>
                this with { WorkspaceTabs = tabs },
            "title-bar" when TryMode<TitleBarMode>(value, out var titleBar) => this with { TitleBar = titleBar },
            "sidebar" when TryMode<SidebarMode>(value, out var sidebar) => this with { Sidebar = sidebar },
            "command-marks" when TryMode<CommandMarks>(value, out var marks) => this with { CommandMarks = marks },
            "shell-integration" when TryMode<ShellIntegrationMode>(value, out var integration) => this with { ShellIntegration = integration },
            "notify-command-finished" when TryMode<NotifyMode>(value, out var notify) => this with { NotifyCommandFinished = notify },
            "notify-programs" when TryMode<NotifyMode>(value, out var programs) => this with { NotifyPrograms = programs },
            "notify-command-finished-after" when TryDuration(value, out var after) => this with { NotifyCommandFinishedAfter = after },
            "clipboard-write" when value is "allow" or "deny" => this with { ClipboardWrite = value == "allow" },
            "copy-on-select" when bool.TryParse(value, out var copyOnSelect) => this with { CopyOnSelect = copyOnSelect },
            "paste-warning" when Enum.TryParse<Phosphor.Terminal.PasteWarning>(value, true, out var paste) && !int.TryParse(value, out _) =>
                this with { PasteWarning = paste },
            "large-paste-warning" when bool.TryParse(value, out var large) => this with { LargePasteWarning = large },
            "word-boundaries" => this with { WordBoundaries = value.Length > 0 ? value : null },
            "focus-follows-mouse" when bool.TryParse(value, out var follows) => this with { FocusFollowsMouse = follows },
            "cursor-style" when TryMode<Phosphor.Ghostty.TerminalCursorStyle>(value.Replace('_', '-'), out var cursorStyle) =>
                this with { CursorStyle = cursorStyle },
            "cursor-blink" when bool.TryParse(value, out var blink) => this with { CursorBlink = blink },
            "scrollback-lines" when int.TryParse(value, out var lines) && lines is >= 0 and <= MaxScrollbackLines =>
                this with { ScrollbackLines = lines },
            "close-on-exit" when TryMode<CloseOnExit>(value, out var close) => this with { CloseOnExit = close },
            "bell" when TryMode<BellMode>(value, out var bell) => this with { Bell = bell },
            "bell-sound" when bool.TryParse(value, out var sound) => this with { BellSound = sound },
            "screen-flash-bell" when bool.TryParse(value, out var screenFlash) => this with { ScreenFlashBell = screenFlash },
            "program-titles" when bool.TryParse(value, out var titles) => this with { ProgramTitles = titles },
            "selection-background" when TryColor(value, out var c) => this with { Theme = theme with { SelectionBackground = c } },
            "selection-foreground" when TryColor(value, out var c) => this with { Theme = theme with { SelectionForeground = c } },
            "clipboard-read" when TryMode<ClipboardAccess>(value, out var read) => this with { ClipboardRead = read },
            "quick-terminal-position" when Enum.TryParse<QuickTerminalPosition>(value, true, out var position) =>
                this with { QuickTerminal = QuickTerminal with { Position = position } },
            "quick-terminal-screen" when Enum.TryParse<QuickTerminalScreen>(value, true, out var screen) =>
                this with { QuickTerminal = QuickTerminal with { Screen = screen } },
            "quick-terminal-size" when TryQuickSize(value, out var primary, out var secondary) =>
                this with { QuickTerminal = QuickTerminal with { Primary = primary, Secondary = secondary } },
            "quick-terminal-autohide" when bool.TryParse(value, out var autohide) =>
                this with { QuickTerminal = QuickTerminal with { Autohide = autohide } },
            "quick-terminal-hotkey" when TryHotkey(value) =>
                this with { QuickTerminal = QuickTerminal with { Hotkey = value } },
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

    private static bool TryQuickSize(string value, out QuickTerminalLength? primary, out QuickTerminalLength? secondary)
    {
        primary = secondary = null;
        var parts = value.Split(',');
        if (parts.Length is < 1 or > 2 || !QuickTerminalLength.TryParse(parts[0], out var p))
        {
            return false;
        }

        primary = p;
        if (parts.Length == 2)
        {
            if (!QuickTerminalLength.TryParse(parts[1], out var s))
            {
                return false;
            }

            secondary = s;
        }

        return true;
    }

    /// <summary>Parses a duration: seconds ("10", "2.5"), or with units ("30s", "2m", "1m30s", "1h").</summary>
    public static bool TryDuration(string value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var s = value.Trim().ToLowerInvariant();
        if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            duration = TimeSpan.FromSeconds(seconds);
            return seconds >= 0;
        }

        var parts = System.Text.RegularExpressions.Regex.Matches(s, @"(\d+(?:\.\d+)?)\s*(h|m|s)");
        if (parts.Count == 0 || string.Concat(parts.Select(p => p.Value)).Replace(" ", "") != s.Replace(" ", ""))
        {
            return false;
        }

        foreach (System.Text.RegularExpressions.Match part in parts)
        {
            var n = double.Parse(part.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            duration += part.Groups[2].Value switch { "h" => TimeSpan.FromHours(n), "m" => TimeSpan.FromMinutes(n), _ => TimeSpan.FromSeconds(n) };
        }

        return true;
    }

    /// <summary>Parses a kebab-case mode name ("auto-hide" → AutoHide).</summary>
    /// <summary>A metric adjustment as Ghostty writes one: whole pixels ("1", "-1", "1px") or a percentage ("20%").</summary>
    private static bool TryAdjustment(string value, out (double Amount, bool Percent) adjustment)
    {
        var s = value.Trim();
        var percent = s.EndsWith('%');
        s = percent ? s[..^1] : s.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? s[..^2] : s;
        var ok = double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var amount);
        adjustment = (amount, percent);
        return ok;
    }

    public static bool TryMode<T>(string value, out T mode) where T : struct, Enum
    {
        mode = default;
        var name = value.Replace("-", "");
        return name.Length > 0 && name.All(char.IsLetter) && Enum.TryParse(name, true, out mode);
    }

    private static bool TryHotkey(string value)
    {
        try
        {
            GlobalHotkey.Parse(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

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
