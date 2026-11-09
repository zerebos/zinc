namespace Zinc.Settings;

public static partial class SettingsCatalog
{
    /// <summary>The settings in the Layout section, in the order the window shows them.</summary>
    private static SettingDef[] LayoutSettings() =>
    [
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
    ];
}
