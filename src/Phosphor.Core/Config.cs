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

/// <summary>
/// User configuration from <c>%APPDATA%\Phosphor\config</c>, in Ghostty's syntax:
/// one <c>key = value</c> per line, <c>#</c> comments, later lines win. Keys and
/// units follow Ghostty so lines can be copied from a Ghostty config.
/// </summary>
public sealed record Config
{
    /// <summary>Default font size in points (≈14 DIPs).</summary>
    public const float DefaultFontSize = 10.5f;

    /// <summary>Primary font family; glyphs it lacks fall back through DirectWrite.</summary>
    public string FontFamily { get; init; } = "Cascadia Mono";

    /// <summary>Font size in points, like Ghostty and Windows Terminal.</summary>
    public float FontSize { get; init; } = DefaultFontSize;

    /// <summary>Font size in DIPs (96 per inch; a point is 1/72 inch).</summary>
    public float FontSizeDip => FontSize * 96f / 72f;

    /// <summary>Command line to run; null means pick a default shell.</summary>
    public string? Command { get; init; }

    public TerminalTheme Theme { get; init; } = TerminalTheme.Default;

    /// <summary>
    /// Pointer over the terminal: an arrow (default, like Windows Terminal) or a text
    /// I-beam (like Ghostty, kitty, WezTerm). Config: <c>mouse-pointer = arrow | text</c>.
    /// </summary>
    public bool TextPointer { get; init; }

    /// <summary>Programs may set the clipboard with OSC 52. Config: <c>clipboard-write = allow | deny</c>.</summary>
    public bool ClipboardWrite { get; init; } = true;

    /// <summary>
    /// Programs may read the clipboard with OSC 52 (off by default: anything running in the
    /// terminal, e.g. over ssh, could read it). Config: <c>clipboard-read = allow | deny</c>.
    /// </summary>
    public bool ClipboardRead { get; init; }

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

    /// <summary>When a pane shows its own tab strip. Config: <c>group-tab-strip = auto | always</c> (global).</summary>
    public GroupTabStrip GroupTabStrip { get; init; }

    /// <summary>Where a window's workspaces are listed. Config: <c>workspace-tabs = top | left</c> (global).</summary>
    public WorkspaceTabs WorkspaceTabs { get; init; }

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

    /// <summary>What unfocused panes are dimmed toward; null = each pane's background. Config: <c>unfocused-split-fill</c> (global).</summary>
    public GhosttyColorRgb? UnfocusedSplitFill { get; init; }

    /// <summary>The quick terminal (global only; ignored inside profiles).</summary>
    public QuickTerminalSettings QuickTerminal { get; init; } = new();

    /// <summary>Padding around the grid, in DIPs.</summary>
    public float PaddingLeft { get; init; } = 8;
    public float PaddingRight { get; init; } = 8;
    public float PaddingTop { get; init; } = 8;
    public float PaddingBottom { get; init; } = 8;

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Phosphor", "config");

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
            "font-size" when TryFloat(value, out var size) && size > 0 => this with { FontSize = size },
            "command" when value.Length > 0 => this with { Command = value },
            "working-directory" when value.Length > 0 => this with { WorkingDirectory = value },
            "icon" when value.Length > 0 => this with { Icon = value },
            "mouse-pointer" when value is "arrow" or "text" => this with { TextPointer = value == "text" },
            "quit-after-last-window-closed" when bool.TryParse(value, out var quit) =>
                this with { QuitAfterLastWindowClosed = quit },
            "tab-groups" when bool.TryParse(value, out var groups) => this with { TabGroups = groups },
            "group-tab-strip" when Enum.TryParse<GroupTabStrip>(value, true, out var strip) && !int.TryParse(value, out _) =>
                this with { GroupTabStrip = strip },
            "unfocused-split-opacity" when double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var opacity) =>
                this with { UnfocusedSplitOpacity = Math.Clamp(opacity, 0.15, 1.0) },
            "window-save-state" when value is "default" or "always" or "never" => this with { RestoreSession = value != "never" },
            "unfocused-split-fill" when TryColor(value, out var fill) => this with { UnfocusedSplitFill = fill },
            "workspace-tabs" when Enum.TryParse<WorkspaceTabs>(value, true, out var tabs) && !int.TryParse(value, out _) =>
                this with { WorkspaceTabs = tabs },
            "clipboard-write" when value is "allow" or "deny" => this with { ClipboardWrite = value == "allow" },
            "clipboard-read" when value is "allow" or "deny" => this with { ClipboardRead = value == "allow" },
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

    internal static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

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
