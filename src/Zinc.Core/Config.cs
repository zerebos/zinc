using System.Globalization;
using Zinc.Ghostty;
using Zinc.Terminal;

namespace Zinc;

/// <summary>Which panes show a header. Config: <c>[window] pane-headers</c>.</summary>
public enum PaneHeaders
{
    /// <summary>On panes holding two or more tabs (the old <c>pane-tab-strip = "auto"</c>).</summary>
    WithTabs,

    /// <summary>On every pane of a split workspace, and on any pane with two or more tabs.</summary>
    Auto,

    /// <summary>On every pane.</summary>
    Always,

    /// <summary>On no pane.</summary>
    Never,
}

/// <summary>Which panes show their header's buttons. Config: <c>[window] pane-header-buttons</c>.</summary>
public enum PaneHeaderButtons
{
    /// <summary>None.</summary>
    Hide,

    /// <summary>The focused pane's, and another pane's while the pointer is over its header.</summary>
    Focused,

    /// <summary>A pane's while the pointer is over its header.</summary>
    Hover,

    /// <summary>Every pane's.</summary>
    Always,
}

/// <summary>How the focused pane is marked. Config: <c>[window] pane-focus</c>.</summary>
public enum PaneFocus
{
    /// <summary>An accent line on the shown tab, or along the top of a pane without a header.</summary>
    TabAccent,

    /// <summary>An accent ring around the pane.</summary>
    Ring,

    /// <summary>No mark. When unfocused panes are dimmed, that still shows which one is focused.</summary>
    None,
}

/// <summary>How a pane shows its tabs. Config: <c>[window] pane-tabs-as</c>.</summary>
public enum PaneTabsAs
{
    /// <summary>A row of tabs, shortened, then icons, then folded into a count as the pane narrows.</summary>
    Tabs,

    /// <summary>The shown tab and a count ("2 of 5") that opens a menu of them all, at every width.</summary>
    Menu,
}

/// <summary>Where a window lists its workspaces. Config: <c>[window] workspace-tabs = top | left</c>.</summary>
public enum WorkspaceTabs
{
    /// <summary>In the title bar.</summary>
    Top,

    /// <summary>In a column on the window's left, under a plain title bar.</summary>
    Left,
}

/// <summary>What opens a link under the pointer. Config: <c>[terminal] link-click = ctrl | alt | click</c>.</summary>
public enum LinkClick
{
    /// <summary>Ctrl+click (links show underlined while Ctrl is held).</summary>
    Ctrl,

    /// <summary>Alt+click.</summary>
    Alt,

    /// <summary>A click without dragging. A drag still selects, and a program using the mouse still gets the click.</summary>
    Click,
}

/// <summary>What a right-click in a terminal does. Config: <c>[terminal] right-click = menu | copy-paste | paste</c>.</summary>
public enum RightClick
{
    /// <summary>A menu with copy, paste, find, the link or command under the pointer, split and close.</summary>
    Menu,

    /// <summary>Copies a selection, or pastes when there's none (the Windows console's way).</summary>
    CopyPaste,

    /// <summary>Pastes.</summary>
    Paste,
}

/// <summary>Whether Zinc loads its shell integration into the shells it starts. Config: <c>[terminal] shell-integration</c>.</summary>
public enum ShellIntegrationMode
{
    /// <summary>PowerShell, bash and zsh get it (after the user's own startup files).</summary>
    Auto,

    /// <summary>Shells start exactly as they would anywhere else.</summary>
    Off,
}

/// <summary>Which commands get a bar in the pane's left edge (shell integration). Config: <c>[terminal] command-marks</c>.</summary>
public enum CommandMarks
{
    /// <summary>Failed commands only (and scrollbar ticks for every prompt).</summary>
    Failures,

    /// <summary>Every command that ran, in green, or in red if it failed.</summary>
    All,

    /// <summary>No bars and no ticks.</summary>
    None,
}

/// <summary>The sound for a finished long command. Config: <c>[notify] command-finished-sound</c>.</summary>
public enum FinishedSound
{
    Off,

    /// <summary>Windows' notification sound.</summary>
    Notification,

    /// <summary>Windows' default beep.</summary>
    Beep,
}

/// <summary>
/// How Zinc tells you about something that happened out of view (a long command finishing, a
/// program's notification). Config: <c>[notify] command-finished</c> and <c>programs</c>.
/// </summary>
public enum NotifyMode
{
    /// <summary>A dot on its tab. The taskbar button also flashes if the window is in the background.</summary>
    Badge,

    /// <summary>The dot, plus a Windows notification when Zinc is in the background.</summary>
    Toast,

    /// <summary>The dot, plus a Windows notification whenever it's out of view, Zinc in front or not.</summary>
    ToastAlways,
    Never,
}

/// <summary>What happens to a pane when its program ends. Config: <c>[terminal] close-on-exit = success | always | never</c>.</summary>
public enum CloseOnExit
{
    /// <summary>Closes after a clean exit (code 0). A failure stays on screen so it can be read.</summary>
    Success,
    Always,
    Never,
}

/// <summary>Starting when you sign in. Config: <c>[window] launch-at-login = off | tray | windows</c>.</summary>
public enum LaunchAtLogin
{
    Off,

    /// <summary>No window opens. The tray icon and the quick terminal's hotkey are ready.</summary>
    Tray,

    /// <summary>The windows you had open (or a new one).</summary>
    Windows,
}

/// <summary>Programs reading the clipboard (OSC 52). Config: <c>[terminal] clipboard-read = ask | allow | deny</c>.</summary>
public enum ClipboardAccess
{
    /// <summary>Each read asks whether to allow it once, allow it for the tab, or deny it.</summary>
    Ask,
    Allow,
    Deny,
}

/// <summary>What the bell (BEL) does. Config: <c>[terminal] bell = attention | flash | none</c>.</summary>
public enum BellMode
{
    /// <summary>A dot on the tab (and the taskbar button flashes) when you aren't looking at it.</summary>
    Attention,

    /// <summary>The pane flashes too.</summary>
    Flash,
    None,
}

/// <summary>
/// When custom shaders that use time (iTime, iFrame...) redraw on their own (Ghostty's <c>custom-shader-animation</c>);
/// otherwise a frame is drawn only when the terminal changes. Config: <c>[shaders] animation = true | false | "always"</c>.
/// </summary>
public enum ShaderAnimation
{
    /// <summary>While the terminal has focus (true, the default).</summary>
    Focused,

    /// <summary>
    /// Never (false). They hold still, with their clock stopped and cursor effects finished, and are drawn only when
    /// something on screen changes.
    /// </summary>
    Never,

    /// <summary>Focused or not.</summary>
    Always,
}

/// <summary>
/// Light or dark windows, menus, and dialogs, and the side of a light and dark color scheme pair the terminal uses.
/// Config: <c>[window] appearance</c>.
/// </summary>
public enum ChromeAppearance
{
    /// <summary>As Windows' app mode is when Zinc starts.</summary>
    System,

    Light,

    Dark,
}

/// <summary>
/// What a window shows behind its content, and through a see-through terminal background. Config:
/// <c>[window] backdrop</c>.
/// </summary>
public enum WindowBackdrop
{
    /// <summary>Mica, which takes a tint from the wallpaper, as Windows apps do.</summary>
    Mica,

    /// <summary>The windows behind, blurred.</summary>
    Acrylic,

    /// <summary>Nothing, so the windows behind show as they are.</summary>
    None,
}

/// <summary>The title bar with <c>workspace-tabs = left</c>. Config: <c>[window] title-bar = show | auto-hide</c>.</summary>
public enum TitleBarMode
{
    Show,

    /// <summary>Hidden until the pointer reaches the window's top edge.</summary>
    AutoHide,
}

/// <summary>The workspace list with <c>workspace-tabs = left</c>. Config: <c>[window] sidebar = full | rail | auto-hide</c>.</summary>
public enum SidebarMode
{
    Full,

    /// <summary>A narrow column of workspace icons.</summary>
    Rail,

    /// <summary>Hidden until the pointer reaches the window's left edge.</summary>
    AutoHide,
}

/// <summary>
/// One resolved set of settings (the global ones, or a profile's). <see cref="ConfigFile"/> reads config.toml into
/// it, one setting at a time, through the setting's binding in <see cref="Zinc.Settings.SettingsCatalog"/>. A
/// property's initializer is the setting's default. The <c>Try…</c> methods parse the forms values are written in.
/// </summary>
public sealed record Config
{
    /// <summary>Default font size in points (≈14 DIPs).</summary>
    public const float DefaultFontSize = 10.5f;

    /// <summary>The font family. Glyphs it lacks fall back through DirectWrite. Config: <c>[font] family</c>.</summary>
    public string FontFamily { get; init; } = "Cascadia Mono NF"; // ships with Zinc (Fonts)

    /// <summary>
    /// The font's ligatures and contextual alternates (=> != -> in fonts that have them), each still one cell. Broken
    /// under the cursor and at a selection's edges. Config: <c>[font] ligatures = true</c>.
    /// </summary>
    public bool FontLigatures { get; init; }

    /// <summary>The weight of normal text (100-900, 400 = normal). Config: <c>[font] weight = "light"</c> (or a number).</summary>
    public int FontWeight { get; init; } = 400;

    /// <summary>The weight of bold text (700 = bold). Config: <c>[font] bold-weight = "semibold"</c> (or a number).</summary>
    public int BoldWeight { get; init; } = 700;

    /// <summary>
    /// Families tried, in order, for characters the font lacks, before Windows' own fallback: "Symbols Nerd Font, Noto
    /// Sans JP". Empty: Windows' alone. Config: <c>[font] fallback = ["…", "…"]</c> (or one string, comma-separated).
    /// </summary>
    public string FontFallback { get; init; } = "";

    /// <summary><see cref="FontFallback"/> as a list.</summary>
    public IReadOnlyList<string> FontFallbacks =>
        FontFallback.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// <c>[font] fallback</c> in the form <see cref="FontFallback"/> keeps it ("A, B"), made from a list of names or from
    /// one string with commas. Null when it's neither.
    /// </summary>
    public static string? NormalizeFallback(object value)
    {
        var text = value switch
        {
            List<object> list when list.All(x => x is string) => string.Join(",", list),
            string s => s,
            _ => null,
        };
        return text == null ? null : string.Join(", ", text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// A font weight by name, as CSS and DirectWrite name them ("semibold" is 600, and "semi-bold" reads the same), or
    /// as a number from 1 to 999.
    /// </summary>
    public static bool TryWeight(string value, out int weight)
    {
        weight = value.Replace("-", "").Replace(" ", "").ToLowerInvariant() switch
        {
            "thin" => 100,
            "extralight" or "ultralight" => 200,
            "light" => 300,
            "semilight" => 350,
            "normal" or "regular" => 400,
            "medium" => 500,
            "semibold" or "demibold" => 600,
            "bold" => 700,
            "extrabold" or "ultrabold" => 800,
            "black" or "heavy" => 900,
            // The number is parsed from the value as written, because the switch strips its dashes and -1 would read as 1
            _ when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 999 => number,
            _ => 0,
        };
        return weight > 0;
    }

    /// <summary>
    /// Box drawing, block elements, Symbols for Legacy Computing, Powerline and Braille drawn by
    /// Ghostty's sprite rasterizer (seamless in any font) rather than taken from the font.
    /// Config: <c>[font] builtin-glyphs = true | false</c>.
    /// </summary>
    public bool BuiltinGlyphs { get; init; } = true;

    /// <summary>
    /// Makes the built-in glyphs' lines thicker or thinner than the font's underline, by whole pixels or by a
    /// percentage (Ghostty's adjust-box-thickness). Config: <c>[font] adjust-box-thickness = 1 | "20%"</c>.
    /// </summary>
    public (double Amount, bool Percent) AdjustBoxThickness { get; init; }

    /// <summary><paramref name="thickness"/> pixels, adjusted by <see cref="AdjustBoxThickness"/> (at least 1).</summary>
    public int AdjustedBoxThickness(int thickness) => Math.Max(1, AdjustBoxThickness switch
    {
        (0, _) => thickness,
        (var percent, true) => (int)Math.Round(thickness * (1 + percent / 100)),
        (var pixels, false) => thickness + (int)Math.Round(pixels),
    });

    /// <summary>
    /// Taller or shorter cells, by pixels at 100% scale or by a percentage of the font's own line height (Ghostty's
    /// adjust-cell-height). Text stays centered in them. Config: <c>[font] adjust-cell-height = 2 | "20%"</c>.
    /// </summary>
    public (double Amount, bool Percent) AdjustCellHeight { get; init; }

    /// <summary>
    /// A cell's height in device pixels, which is the font's <paramref name="natural"/> height adjusted by
    /// <see cref="AdjustCellHeight"/>, and never less than half of it.
    /// </summary>
    public uint AdjustedCellHeight(uint natural, float scale)
    {
        var extra = AdjustCellHeight switch
        {
            (0, _) => 0,
            (var percent, true) => Math.Round(natural * percent / 100),
            (var pixels, false) => Math.Round(pixels * scale),
        };
        return (uint)Math.Max(Math.Ceiling(natural / 2.0), natural + extra);
    }

    /// <summary>Font size in points, like Ghostty and Windows Terminal. Config: <c>[font] size</c>.</summary>
    public float FontSize { get; init; } = DefaultFontSize;

    /// <summary>Font size in DIPs. There are 96 DIPs to an inch, and 72 points.</summary>
    public float FontSizeDip => FontSize * 96f / 72f;

    /// <summary>The command line to run, or null to pick a default shell. Config: a profile's <c>command</c>.</summary>
    public string? Command { get; init; }

    public TerminalTheme Theme { get; init; } = TerminalTheme.Default;

    /// <summary>With a light/dark theme pair, whether its dark side is the one in force. Null for a single theme.</summary>
    public bool? PairedDark { get; init; }

    /// <summary>
    /// Whether programs are told the colors are dark (CSI ? 996 n, mode 2031). That's the side of a light/dark pair
    /// in force, or without a pair, whether the background is dark.
    /// </summary>
    public bool IsDark => PairedDark ?? 0.2126 * Theme.Background.R + 0.7152 * Theme.Background.G + 0.0722 * Theme.Background.B < 128;

    /// <summary>
    /// Whether the pointer over the terminal is a text I-beam (like Ghostty, kitty and WezTerm) instead of an arrow
    /// (the default, like Windows Terminal). Config: <c>[terminal] mouse-pointer = arrow | text</c>.
    /// </summary>
    public bool TextPointer { get; init; }

    /// <summary>Programs may set the clipboard with OSC 52. Config: <c>[terminal] clipboard-write = true | false</c>.</summary>
    public bool ClipboardWrite { get; init; } = true;

    /// <summary>A selection is copied as soon as it's made (and stays selected). Config: <c>[terminal] copy-on-select = true</c>.</summary>
    public bool CopyOnSelect { get; init; }

    /// <summary>
    /// Copying also puts the selection's colors and styles on the clipboard (HTML next to the text), for documents and
    /// email. Config: <c>[terminal] copy-formatting = true</c>.
    /// </summary>
    public bool CopyFormatting { get; init; }

    /// <summary>What opens a link. Config: <c>[terminal] link-click</c>.</summary>
    public LinkClick LinkClick { get; init; }

    /// <summary>What a right-click does (unless a program uses the mouse). Config: <c>[terminal] right-click</c>.</summary>
    public RightClick RightClick { get; init; }

    /// <summary>The middle button pastes (unless a program uses the mouse). Config: <c>[terminal] middle-click-paste = true</c>.</summary>
    public bool MiddleClickPaste { get; init; }

    /// <summary>
    /// Clicking in a shell's command line moves its cursor there (with shell integration's marks).
    /// Config: <c>[terminal] click-to-move = false</c> (Ghostty's <c>cursor-click-to-move</c>).
    /// </summary>
    public bool ClickToMove { get; init; } = true;

    /// <summary>When a paste asks first. Config: <c>[terminal] paste-warning = auto | multiline | never</c>.</summary>
    public Zinc.Terminal.PasteWarning PasteWarning { get; init; }

    /// <summary>A paste over 5 KB asks first. Config: <c>[terminal] large-paste-warning = true | false</c>.</summary>
    public bool LargePasteWarning { get; init; } = true;

    /// <summary>
    /// Characters that end a word when you double-click (space and tab always do). Null means Ghostty's
    /// own set. Config: <c>[terminal] word-boundaries = "'\"|:;,()[]{}&lt;&gt;$"</c>.
    /// </summary>
    public string? WordBoundaries { get; init; }

    /// <summary>Moving the pointer into a pane focuses it. Config: <c>[window] focus-follows-mouse = true</c> (global).</summary>
    public bool FocusFollowsMouse { get; init; }

    /// <summary>The cursor until a program picks its own. Config: <c>[terminal] cursor-style = block | bar | underline | block-hollow</c>.</summary>
    public Zinc.Ghostty.TerminalCursorStyle CursorStyle { get; init; } = Zinc.Ghostty.TerminalCursorStyle.Block;

    /// <summary>Whether that cursor blinks. Config: <c>[terminal] cursor-blink = true</c>.</summary>
    public bool CursorBlink { get; init; }

    /// <summary>
    /// The cursor while a shell waits at its prompt (needs shell integration's marks). Null means the
    /// same as elsewhere. Config: <c>[terminal] cursor-style-at-prompt = same | block | bar | underline | block-hollow</c>.
    /// </summary>
    public Zinc.Ghostty.TerminalCursorStyle? CursorStyleAtPrompt { get; init; }

    /// <summary>Lines kept above the screen. Config: <c>[terminal] scrollback-lines = 10000</c>.</summary>
    public int ScrollbackLines { get; init; } = DefaultScrollbackLines;

    public const int DefaultScrollbackLines = 10_000;

    public const int MaxScrollbackLines = 100_000;

    /// <summary>What happens when the program ends. Config: <c>[terminal] close-on-exit</c>.</summary>
    public CloseOnExit CloseOnExit { get; init; }

    /// <summary>What the bell does. Config: <c>[terminal] bell</c>.</summary>
    public BellMode Bell { get; init; }

    /// <summary>The bell also plays Windows' default sound. Config: <c>[terminal] bell-sound = true</c>.</summary>
    public bool BellSound { get; init; }

    /// <summary>
    /// New tabs read environment variables from Windows when they start, as Windows Terminal does, so a PATH an
    /// installer changed works without restarting Zinc. They're merged over Zinc's own, and nothing is removed. Config:
    /// <c>[terminal] reload-environment</c>.
    /// </summary>
    public bool ReloadEnvironment { get; init; } = true;

    /// <summary>
    /// Variables set for the programs started, from <c>[environment]</c> with a profile's
    /// <c>[profile.x.environment]</c> over it. An empty value removes the variable.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = NoVariables;

    // One empty set for every config without variables, because configs compare as records and two separate empty
    // dictionaries wouldn't be equal.
    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>();

    /// <summary>A program's screen flash (a "visible bell") rings the bell instead. Config: <c>[terminal] screen-flash-bell = false</c>.</summary>
    public bool ScreenFlashBell { get; init; } = true;

    /// <summary>
    /// Input methods (IME) type into the terminal. A focused pane's keys pass through a hidden text box that Windows'
    /// input methods work with. Config: <c>[terminal] input-method = false</c> sends keys straight to the terminal.
    /// </summary>
    public bool InputMethod { get; init; } = true;

    /// <summary>
    /// Programs may set the tab's title (OSC 0/2). When off, the tab shows the profile's name. Config:
    /// <c>[terminal] program-titles = false</c>.
    /// </summary>
    public bool ProgramTitles { get; init; } = true;

    /// <summary>
    /// The tab shows the folder at the shell's prompt and the command while one runs, from the shell's marks. A title
    /// the running program sets still wins. Config: <c>[terminal] shell-titles</c>.
    /// </summary>
    public bool ShellTitles { get; init; } = true;

    /// <summary>
    /// Whether programs may read the clipboard with OSC 52. Anything running in the terminal could read it, including
    /// a program on the far end of ssh, so by default each read asks. Config:
    /// <c>[terminal] clipboard-read = ask | allow | deny</c>.
    /// </summary>
    public ClipboardAccess ClipboardRead { get; init; } = ClipboardAccess.Ask;

    /// <summary>
    /// Whether closing the last window quits Zinc (global only). Null, the default, keeps Zinc running in the tray
    /// while the quick terminal's hotkey is set, so the hotkey still works, and quits otherwise.
    /// Config: <c>[window] quit-after-last-window-closed = true | false</c>.
    /// </summary>
    public bool? QuitAfterLastWindowClosed { get; init; }

    /// <summary>
    /// A sound when a long command finishes out of view. It doesn't play with a Windows notification, which has its
    /// own. Config: <c>[notify] command-finished-sound</c>.
    /// </summary>
    public FinishedSound CommandFinishedSound { get; init; }

    /// <summary>How far the resize-pane action moves a divider, as a fraction of the split. Config: <c>[window] pane-resize-step</c> (percent).</summary>
    public double PaneResizeStep { get; init; } = 0.05;

    /// <summary>
    /// Closing a tab, pane, workspace or window asks first when a command is still running in it, or when it takes two or
    /// more tabs. Config: <c>[window] confirm-close</c>.
    /// </summary>
    public bool ConfirmClose { get; init; } = true;

    /// <summary>Quitting (or restarting for an update) asks first when a command is still running. Config: <c>[window] confirm-quit</c>.</summary>
    public bool ConfirmQuit { get; init; } = true;

    /// <summary>
    /// A pane can hold several tabs (true, the default). False keeps one terminal per pane, and
    /// "new tab" opens a workspace instead. Config: <c>[window] pane-tabs = true | false</c> (global).
    /// </summary>
    public bool PaneTabs { get; init; } = true;

    /// <summary>
    /// Draw with the CPU (WARP) instead of the graphics card, for broken GPU drivers, some virtual
    /// machines and remote desktop glitches. Config: <c>[window] software-rendering = true</c> (global).
    /// </summary>
    public bool SoftwareRendering { get; init; }

    /// <summary>
    /// Each terminal presents from a swap chain of its own as soon as a frame is ready, instead of waiting for the
    /// window's next render tick, so typing shows up about a refresh sooner. Off draws through a CanvasControl, which
    /// is kept as a fallback. Config: <c>[window] low-latency-drawing</c> (global, for terminals opened from then on).
    /// </summary>
    public bool LowLatencyDrawing { get; init; } = true;

    /// <summary>
    /// A tab hidden for <see cref="FreeHiddenAfter"/> gives its drawing surface's graphics memory back. That's two
    /// full-size buffers plus a shader pane's pictures, tens of MB for a maximized tab on a high-resolution screen. When
    /// it's shown again, it makes them anew for its first frame. Low-latency drawing only. Config:
    /// <c>[window] free-hidden-memory</c> (global).
    /// </summary>
    public bool FreeHiddenMemory { get; init; } = true;

    /// <summary>How long a tab is hidden before <see cref="FreeHiddenMemory"/> applies. Config: <c>[window] free-hidden-after</c>.</summary>
    public TimeSpan FreeHiddenAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Custom shaders (Ghostty/Shadertoy GLSL) run over the terminal's picture, in order, as full paths. Relative paths
    /// in the file are from the config file's folder. Needs low-latency drawing. Config: <c>[shaders] files =
    /// ["crt.glsl"]</c>.
    /// </summary>
    public ValueList<string> ShaderFiles { get; init; } = ValueList<string>.Empty;

    /// <summary>
    /// Folders of shaders the picker in Settings lists, such as a collection you cloned, as full paths. Relative paths
    /// in the file are from the config file's folder. Config: <c>[shaders] folders = ["~/dev/ghostty-shaders"]</c>.
    /// </summary>
    public ValueList<string> ShaderFolders { get; init; } = ValueList<string>.Empty;

    /// <summary>When time-based shaders redraw on their own. Config: <c>[shaders] animation</c>.</summary>
    public ShaderAnimation ShaderAnimation { get; init; }

    /// <summary>
    /// Which panes show a header. Config: <c>[window] pane-headers = auto | with-tabs | always | never</c>. The old
    /// <c>pane-tab-strip</c> is read as this key (<see cref="ConfigFile.ReplacedKeys"/>).
    /// </summary>
    public PaneHeaders PaneHeaders { get; init; } = PaneHeaders.Always;

    /// <summary>
    /// Which panes show their header's buttons. Config: <c>[window] pane-header-buttons = always | focused | hover |
    /// hide</c>.
    /// </summary>
    public PaneHeaderButtons PaneHeaderButtons { get; init; } = PaneHeaderButtons.Focused;

    /// <summary>
    /// Pixels between panes. At 0 the panes meet at a divider, a 5 px band with a 1 px line. Config:
    /// <c>[window] pane-gap</c>.
    /// </summary>
    public int PaneGap { get; init; } = 6;

    /// <summary>The gap at the workspace's outer edges too, split or not. Config: <c>[window] pane-gap-edges</c>.</summary>
    public bool PaneGapEdges { get; init; } = true;

    /// <summary>
    /// Panes slide into place when the layout changes (split, close, maximize, moves), unless Windows' Animation effects
    /// are off. Config: <c>[window] pane-animations</c>.
    /// </summary>
    public bool PaneAnimations { get; init; } = true;

    /// <summary>How the focused pane is marked. Config: <c>[window] pane-focus = ring | tab-accent | none</c>.</summary>
    public PaneFocus PaneFocus { get; init; } = PaneFocus.Ring;

    /// <summary>How a pane shows its tabs. Config: <c>[window] pane-tabs-as = tabs | menu</c>.</summary>
    public PaneTabsAs PaneTabsAs { get; init; }

    /// <summary>Where a window's workspaces are listed. Config: <c>[window] workspace-tabs = top | left</c> (global).</summary>
    public WorkspaceTabs WorkspaceTabs { get; init; } = WorkspaceTabs.Left;

    /// <summary>
    /// How Zinc tells you a long command finished out of view. Config: <c>[notify] command-finished = badge | toast |
    /// toast-always | never</c> (global).
    /// </summary>
    public NotifyMode NotifyCommandFinished { get; init; }

    /// <summary>
    /// Notifications programs send (OSC 9, OSC 777). Config: <c>[notify] programs = badge | toast | toast-always |
    /// never</c> (global).
    /// </summary>
    public NotifyMode NotifyPrograms { get; init; }

    /// <summary>
    /// How long a command must run to count as long. Config: <c>[notify] command-finished-after = 10</c>, in seconds, or
    /// with a unit ("30s", "2m"). Global.
    /// </summary>
    public TimeSpan NotifyCommandFinishedAfter { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Marks for shell-integrated commands. Config: <c>[terminal] command-marks = failures | all | none</c>.</summary>
    public CommandMarks CommandMarks { get; init; }

    /// <summary>Zinc's shell integration, loaded automatically. Config: <c>[terminal] shell-integration = auto | off</c>.</summary>
    public ShellIntegrationMode ShellIntegration { get; init; }

    /// <summary>The title bar with the workspace list. Config: <c>[window] title-bar = show | auto-hide</c> (global).</summary>
    public TitleBarMode TitleBar { get; init; }

    /// <summary>How the workspace list shows. Config: <c>[window] sidebar = full | rail | auto-hide</c> (global).</summary>
    public SidebarMode Sidebar { get; init; }

    /// <summary>
    /// How visible panes without focus stay, 0.15–1 (1 = not dimmed). Config, as in Ghostty and
    /// with its default: <c>[window] unfocused-pane-opacity = 0.7</c> (global).
    /// </summary>
    public double UnfocusedPaneOpacity { get; init; } = 0.7;

    /// <summary>
    /// Whether windows, workspaces and tabs reopen as they were, from session.json. Their contents don't come back.
    /// Config: <c>[window] save-state = always | never</c> (global). "default", Ghostty's third value, reads as always.
    /// </summary>
    public bool SaveState { get; init; } = true;

    /// <summary>
    /// A new workspace opens on a page for choosing its profile, folder and name, where Enter starts the
    /// default. Config: <c>[window] new-workspace-page = false</c> starts the default at once.
    /// </summary>
    public bool NewWorkspacePage { get; init; } = true;

    /// <summary>Colors for new workspaces you don't color yourself. Config: <c>[window] workspace-colors = off | folder | cycle</c>.</summary>
    public Zinc.Panes.WorkspaceColorMode WorkspaceColors { get; init; }

    /// <summary>
    /// Where new tabs start in a workspace without a folder of its own. A split always starts where you are.
    /// Config: <c>[window] start-in = current | profile</c>.
    /// </summary>
    public Zinc.Terminal.NewTerminalFolder StartIn { get; init; }

    /// <summary>Starting Zinc when you sign in to Windows. Config: <c>[window] launch-at-login = off | tray | windows</c>.</summary>
    public LaunchAtLogin LaunchAtLogin { get; init; }

    /// <summary>"Open in Zinc" on Explorer's menu for folders and drives. Config: <c>[window] explorer-menu = true</c>.</summary>
    public bool ExplorerMenu { get; init; }

    /// <summary>
    /// What unfocused panes are dimmed toward, or null for each pane's own background. Config:
    /// <c>[window] unfocused-pane-fill</c> (global).
    /// </summary>
    public GhosttyColorRgb? UnfocusedPaneFill { get; init; }

    /// <summary>The line between split panes, or null for Zinc's grey. Config: <c>[window] divider-color</c> (global).</summary>
    public GhosttyColorRgb? DividerColor { get; init; }

    /// <summary>
    /// The least contrast text may have against its background, as a WCAG ratio (1 allows any, 21 is black on
    /// white). Text below it turns black or white. Config: <c>[colors] minimum-contrast</c>.
    /// </summary>
    public double MinimumContrast { get; init; } = 1;

    /// <summary>Bold text in the default color takes this one, or with null keeps that color. Config: <c>[colors] bold</c>.</summary>
    public GhosttyColorRgb? BoldColor { get; init; }

    /// <summary>
    /// Pane headers take this fill, and cards' edges go a step further. With null, the fill is made from the
    /// background at <see cref="PaneHeaderLevel"/>. Config: <c>[colors] pane-header</c> = a color.
    /// </summary>
    public GhosttyColorRgb? PaneHeaderColor { get; init; }

    /// <summary>
    /// How far a made header fill stands from the background, without <see cref="PaneHeaderColor"/>. Config:
    /// <c>[colors] pane-header</c> = terminal | subtle | standard | strong.
    /// </summary>
    public Zinc.Panes.HeaderLevel PaneHeaderLevel { get; init; } = Zinc.Panes.HeaderLevel.Terminal;

    /// <summary>Bold text in palette colors 0-7 shows their bright versions (8-15). Config: <c>[colors] bold-is-bright</c>.</summary>
    public bool BoldIsBright { get; init; }

    /// <summary>Colors 16-255 made from the scheme's 16. Config: <c>[colors] palette-generate</c>, <c>palette-harmonious</c>.</summary>
    public PaletteGeneration PaletteGeneration { get; init; }

    /// <summary>How much of faint text's color shows over its background (0.1-1). Config: <c>[colors] faint-opacity</c> (Ghostty's 0.5).</summary>
    public double FaintOpacity { get; init; } = 0.5;

    /// <summary>
    /// How opaque the default background is, from 0 to 1. Below 1 the window's backdrop shows through, and at 0 only
    /// the backdrop shows. Cells with a background color of their own stay solid. Config:
    /// <c>[colors] background-opacity</c> (Ghostty's).
    /// </summary>
    public double BackgroundOpacity { get; init; } = 1;

    /// <summary>What windows show behind their content. Config: <c>[window] backdrop = mica | acrylic | none</c> (global).</summary>
    public WindowBackdrop Backdrop { get; init; }

    /// <summary>
    /// How much of <see cref="BackdropColor"/> frosted or clear glass carries, from 0 (none, the most see-through)
    /// to 1 (solid). Config: <c>[window] backdrop-tint</c> (global).
    /// </summary>
    public double BackdropTint { get; init; } = 0.3;

    /// <summary>
    /// Zinc's green as the app's accent (selections, focus lines, switches), or Windows' accent color
    /// (false). Config: <c>[window] accent = "zinc" | "windows"</c> (global, applied when Zinc starts).
    /// </summary>
    public bool ZincAccent { get; init; } = true;

    /// <summary>
    /// Light or dark windows, menus and dialogs. Config: <c>[window] appearance = "system" | "light" | "dark"</c>
    /// (global, applied when Zinc starts, because WinUI sets an app's theme once).
    /// </summary>
    public ChromeAppearance Appearance { get; init; }

    /// <summary>
    /// The tab dot for a command that finished or a program's notification, or null for Zinc's green (darker on light
    /// themes). Config: <c>[notify] finished-color</c> (global).
    /// </summary>
    public GhosttyColorRgb? DotFinishedColor { get; init; }

    /// <summary>The tab dot for the bell, or null for Zinc's amber. Config: <c>[notify] bell-color</c> (global).</summary>
    public GhosttyColorRgb? DotBellColor { get; init; }

    /// <summary>The tab dot for a failed command, or null for Zinc's red. Config: <c>[notify] failed-color</c> (global).</summary>
    public GhosttyColorRgb? DotFailedColor { get; init; }

    /// <summary>The glass's color, or null for the terminal's background. Config: <c>[window] backdrop-color</c> (global).</summary>
    public GhosttyColorRgb? BackdropColor { get; init; }

    /// <summary>Whether Zinc keeps a debug log (<see cref="Log.FilePath"/>). Config: <c>[debug] logging</c> (global).</summary>
    public bool Logging { get; init; }

    /// <summary>Whether an installed Zinc checks for updates on its own (once a day). Config: <c>[updates] check</c> (global).</summary>
    public bool UpdateCheck { get; init; } = true;

    /// <summary>Whether zn may control this Zinc (anything but <c>zn version</c>). Config: <c>[cli] remote-control</c> (global).</summary>
    public bool RemoteControl { get; init; }

    /// <summary>Whether zn's folder is on the user's PATH, for other terminals. Config: <c>[cli] path</c> (global).</summary>
    public bool ZnOnPath { get; init; }

    /// <summary>
    /// The cursor's opacity, from 0.1 to 1. Below 1, a block cursor shows the character under it in its own color.
    /// Config: <c>[colors] cursor-opacity</c>.
    /// </summary>
    public double CursorOpacity { get; init; } = 1;

    /// <summary>Find's highlight on matches, or null for Zinc's yellow. Config: <c>[colors] find-highlight</c>.</summary>
    public GhosttyColorRgb? SearchBackground { get; init; }

    /// <summary>
    /// Find's highlight on the current match, or null for Zinc's orange. Config: <c>[colors] find-highlight-current</c>.
    /// </summary>
    public GhosttyColorRgb? SearchSelectedBackground { get; init; }

    /// <summary>The quick terminal (global only; ignored inside profiles).</summary>
    public QuickTerminalSettings QuickTerminal { get; init; } = new();

    /// <summary>
    /// Padding around the grid, in DIPs, here and in the next three. Config: <c>[terminal] padding-x</c> and
    /// <c>padding-y</c>, in points.
    /// </summary>
    public float PaddingLeft { get; init; } = 8;
    public float PaddingRight { get; init; } = 8;
    public float PaddingTop { get; init; } = 8;
    public float PaddingBottom { get; init; } = 8;

    /// <summary>
    /// Splits the room left over by whole cells between both sides, instead of leaving it all on the right and bottom.
    /// Config: <c>[terminal] padding-balance</c>.
    /// </summary>
    public bool PaddingBalance { get; init; }

    public static string DefaultPath => ConfigFile.DefaultPath;

    /// <summary>Loads the global settings (see <see cref="ConfigFile"/> for profiles).</summary>
    public static Config Load(string? path = null) => ConfigFile.Load(path).Global;

    /// <summary>An image file for a profile's tabs. Only a profile sets it. Config: a profile's <c>icon</c>.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// The folder the command starts in, or null for the user's home. Config: a profile's <c>working-directory</c>.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// The folder to start in, which is <see cref="WorkingDirectory"/> with <c>~</c>, <c>home</c> and
    /// <c>%VARIABLES%</c> expanded, or the user's home.
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

    internal static bool TryQuickSize(string value, out QuickTerminalLength? primary, out QuickTerminalLength? secondary)
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

    /// <summary>Parses a duration in seconds ("10", "2.5") or with units ("30s", "2m", "1m30s", "1h").</summary>
    public static bool TryDuration(string value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var s = value.Trim().ToLowerInvariant();
        if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return TrySeconds(seconds, out duration);
        }

        var parts = System.Text.RegularExpressions.Regex.Matches(s, @"(\d+(?:\.\d+)?)\s*(h|m|s)");
        if (parts.Count == 0 || string.Concat(parts.Select(p => p.Value)).Replace(" ", "") != s.Replace(" ", ""))
        {
            return false;
        }

        var total = 0.0;
        foreach (System.Text.RegularExpressions.Match part in parts)
        {
            var n = double.Parse(part.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            total += n * part.Groups[2].Value switch { "h" => 3600, "m" => 60, _ => 1 };
        }

        return TrySeconds(total, out duration);
    }

    // From 0 to a thousand years, a safe cap well inside what a TimeSpan holds. Making one from infinity or NaN throws.
    private static bool TrySeconds(double seconds, out TimeSpan duration)
    {
        var ok = seconds is >= 0 and <= 1000 * 365.0 * 86400;
        duration = ok ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        return ok;
    }

    /// <summary>A metric adjustment as Ghostty writes one, in whole pixels ("1", "-1", "1px") or as a percentage ("20%").</summary>
    public static bool TryAdjustment(string value, out (double Amount, bool Percent) adjustment)
    {
        var s = value.Trim();
        var percent = s.EndsWith('%');
        s = percent ? s[..^1] : s.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? s[..^2] : s;
        var ok = double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var amount) &&
                 double.IsFinite(amount);
        adjustment = (amount, percent);
        return ok;
    }

    /// <summary>Parses a kebab-case mode name ("auto-hide" → AutoHide).</summary>
    public static bool TryMode<T>(string value, out T mode) where T : struct, Enum
    {
        mode = default;
        var name = value.Trim().Replace("-", "");
        return name.Length > 0 && name.All(char.IsLetter) && Enum.TryParse(name, true, out mode);
    }

    internal static bool TryHotkey(string value)
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
        // Six hex digits and nothing else. uint.TryParse alone would also take white space around fewer digits.
        if (s.Length != 6 || !s.All(char.IsAsciiHexDigit) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = GhosttyColorRgb.FromHex(rgb);
        return true;
    }

    /// <summary>Parses Ghostty's <c>N=#rrggbb</c> palette entry.</summary>
    internal static bool TryPaletteEntry(string s, out int index, out GhosttyColorRgb color)
    {
        color = default;
        index = -1;
        var eq = s.IndexOf('=');
        return eq > 0 &&
               int.TryParse(s[..eq].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) &&
               index is >= 0 and <= 255 &&
               TryColor(s[(eq + 1)..].Trim(), out color);
    }

    /// <summary>
    /// Parses Ghostty's padding in points, either <c>N</c> for both sides or <c>A,B</c>. The results are in DIPs.
    /// </summary>
    public static bool TryPadding(string s, out float first, out float second)
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
