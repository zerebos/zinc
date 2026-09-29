using System.Globalization;
using Phosphor.Panes;

namespace Phosphor.Input;

/// <summary>
/// Every action by name: what the <c>[keys]</c> table binds, what the command palette shows,
/// and what the README lists. An action is a name plus at most one argument, written as one
/// string: <c>"split right"</c>, <c>"go-to-workspace 3"</c>, <c>"new-tab wsl:Fedora"</c>.
/// </summary>
public static class Actions
{
    private enum Arg
    {
        None,
        Direction,

        /// <summary>1-based workspace number, or "last".</summary>
        Workspace,

        /// <summary>Optional: a profile id, or its 1-based position in the menu.</summary>
        OptionalProfile,

        /// <summary>Optional: "if-selection".</summary>
        CopyMode,

        /// <summary>Optional: "rail" or "auto-hide" (toggled with full); none cycles.</summary>
        OptionalSidebarMode,

        /// <summary>"previous" or "next".</summary>
        PromptDirection,
    }

    private sealed record Definition(string Name, TerminalAction Action, Arg Arg, string Title, string Description);

    private static readonly Definition[] Definitions =
    [
        new("copy", TerminalAction.Copy, Arg.CopyMode, "Copy",
            "Copy the selection. \"copy if-selection\" lets the key through to the app when nothing is selected."),
        new("paste", TerminalAction.Paste, Arg.None, "Paste", "Paste the clipboard."),
        new("find", TerminalAction.Find, Arg.None, "Find", "Open the find box."),
        new("increase-font-size", TerminalAction.FontSizeIncrease, Arg.None, "Increase font size", "Zoom in."),
        new("decrease-font-size", TerminalAction.FontSizeDecrease, Arg.None, "Decrease font size", "Zoom out."),
        new("reset-font-size", TerminalAction.FontSizeReset, Arg.None, "Reset font size", "Back to the configured size."),
        new("scroll-page-up", TerminalAction.ScrollPageUp, Arg.None, "Scroll page up", "Scroll back a page."),
        new("scroll-page-down", TerminalAction.ScrollPageDown, Arg.None, "Scroll page down", "Scroll forward a page."),
        new("scroll-to-top", TerminalAction.ScrollToTop, Arg.None, "Scroll to top", "Scroll to the start of the scrollback."),
        new("scroll-to-bottom", TerminalAction.ScrollToBottom, Arg.None, "Scroll to bottom", "Scroll to the prompt."),
        new("jump-to-prompt", TerminalAction.JumpToPrompt, Arg.PromptDirection, "Jump to prompt",
            "Scroll to the previous or next prompt: \"jump-to-prompt previous\" / \"next\". Needs shell integration; without it, scrolls a line."),
        new("select-output", TerminalAction.SelectOutput, Arg.None, "Select command output",
            "Select the output of the command at the top of the view (or the last command). Needs shell integration."),
        new("copy-output", TerminalAction.CopyOutput, Arg.None, "Copy command output",
            "Copy the output of the command at the top of the view (or the last command). Needs shell integration."),
        new("new-tab", TerminalAction.NewTab, Arg.OptionalProfile, "New tab",
            "Open a tab (in the focused pane, with tab groups). With a profile: that profile, by id or menu position."),
        new("new-workspace", TerminalAction.NewWorkspace, Arg.None, "New workspace", "Open a workspace with the default profile."),
        new("new-window", TerminalAction.NewWindow, Arg.None, "New window", "Open a window with the default profile."),
        new("duplicate-tab", TerminalAction.DuplicateTab, Arg.None, "Duplicate tab", "Open the focused tab's command again, next to it."),
        new("close-tab", TerminalAction.ClosePane, Arg.None, "Close tab", "Close the focused tab (its pane goes with its last tab)."),
        new("next-tab", TerminalAction.NextTab, Arg.None, "Next tab", "The pane's next tab, or the next workspace."),
        new("previous-tab", TerminalAction.PreviousTab, Arg.None, "Previous tab", "The pane's previous tab, or the previous workspace."),
        new("go-to-workspace", TerminalAction.SwitchToTab, Arg.Workspace, "Go to workspace", "Show workspace N (from 1), or \"last\"."),
        new("split", TerminalAction.SplitPane, Arg.Direction, "Split", "Split the focused pane: right, down, left or up."),
        new("duplicate-pane", TerminalAction.DuplicatePane, Arg.None, "Duplicate pane", "Split, running the focused tab's command again."),
        new("focus", TerminalAction.MoveFocus, Arg.Direction, "Focus pane", "Focus the neighboring pane: left, right, up or down."),
        new("resize", TerminalAction.ResizePane, Arg.Direction, "Resize pane", "Move the focused pane's divider: left, right, up or down."),
        new("profile-menu", TerminalAction.OpenNewTabMenu, Arg.None, "Open the profile menu", "The ▾ menu of profiles."),
        new("command-palette", TerminalAction.OpenCommandPalette, Arg.None, "Command palette", "Search every command."),
        new("tab-switcher", TerminalAction.TabSwitcher, Arg.None, "Go to tab…",
            "Search this window's tabs by title, folder or running command (the palette's \"@\"); tabs with a dot come first."),
        new("next-attention", TerminalAction.NextAttention, Arg.None, "Go to next tab needing attention",
            "Go to the next tab with a dot: failed commands first, then programs' notifications, then finished commands."),
        new("toggle-sidebar", TerminalAction.ToggleSidebar, Arg.OptionalSidebarMode, "Toggle sidebar",
            "With workspace-tabs = \"left\": cycle the workspace list through full, rail and auto-hide. \"toggle-sidebar rail\" (or auto-hide) switches between that and full."),
        new("toggle-title-bar", TerminalAction.ToggleTitleBar, Arg.None, "Toggle title bar",
            "With workspace-tabs = \"left\": auto-hide the title bar, or show it again."),
        new("focus-mode", TerminalAction.FocusMode, Arg.None, "Focus mode",
            "With workspace-tabs = \"left\": auto-hide the title bar and the workspace list; again to bring them back."),
        new("reload-config", TerminalAction.ReloadConfig, Arg.None, "Reload config", "Read config.toml again."),
        new("open-settings", TerminalAction.OpenSettings, Arg.None, "Settings", "Open the settings window."),
        new("quit", TerminalAction.Quit, Arg.None, "Quit Phosphor", "Close every window and exit."),
    ];

    /// <summary>Unbinds a chord in <c>[keys]</c>.</summary>
    public const string None = "none";

    /// <summary>Every action name, with what its argument is and does (for docs and errors).</summary>
    public static IEnumerable<(string Name, string Description)> All => Definitions.Select(d => (d.Name, d.Description));

    /// <summary>
    /// Parses an action string. Null (with <paramref name="error"/>) if it's no action;
    /// <see cref="None"/> is handled by the caller.
    /// </summary>
    public static ActionInvocation? Parse(string text, out string? error)
    {
        error = null;
        var trimmed = text.Trim();
        var space = trimmed.IndexOf(' ');
        var name = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var arg = space < 0 ? "" : trimmed[(space + 1)..].Trim();

        // CopyIfSelection's old spelling, for anyone coming from Ghostty/WT naming.
        if (name == "copy-if-selection")
        {
            (name, arg) = ("copy", "if-selection");
        }

        if (Definitions.FirstOrDefault(d => d.Name == name) is not { } def)
        {
            error = $"unknown action '{name}'";
            return null;
        }

        switch (def.Arg)
        {
            case Arg.None when arg.Length > 0:
                error = $"'{name}' takes no argument";
                return null;
            case Arg.None:
                return new ActionInvocation(def.Action);
            case Arg.CopyMode when arg.Length == 0:
                return new ActionInvocation(TerminalAction.Copy);
            case Arg.CopyMode when arg.Equals("if-selection", StringComparison.OrdinalIgnoreCase):
                return new ActionInvocation(TerminalAction.CopyIfSelection);
            case Arg.CopyMode:
                error = $"'copy' takes nothing or \"if-selection\", not '{arg}'";
                return null;
            case Arg.Direction when Enum.TryParse<PaneDirection>(arg, true, out var dir) && !int.TryParse(arg, out _):
                return new ActionInvocation(def.Action, Direction: dir);
            case Arg.Direction:
                error = $"'{name}' takes left, right, up or down{(arg.Length > 0 ? $", not '{arg}'" : "")}";
                return null;
            case Arg.Workspace when arg.Equals("last", StringComparison.OrdinalIgnoreCase):
                return new ActionInvocation(def.Action, -1);
            case Arg.Workspace when int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1:
                return new ActionInvocation(def.Action, n - 1);
            case Arg.Workspace:
                error = $"'{name}' takes a workspace number from 1, or \"last\"";
                return null;
            case Arg.OptionalProfile when arg.Length == 0:
                return new ActionInvocation(def.Action);
            case Arg.OptionalProfile when int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1:
                return new ActionInvocation(TerminalAction.NewTabFromProfile, n - 1);
            case Arg.OptionalProfile when arg == "0":
                error = "profile positions start at 1";
                return null;
            case Arg.OptionalProfile:
                return new ActionInvocation(TerminalAction.NewTabFromProfile, Profile: arg);
            case Arg.OptionalSidebarMode when arg.Length == 0:
                return new ActionInvocation(def.Action, -1);
            case Arg.OptionalSidebarMode when Config.TryMode<SidebarMode>(arg, out var mode) && mode != SidebarMode.Full:
                return new ActionInvocation(def.Action, (int)mode);
            case Arg.OptionalSidebarMode:
                error = $"'{name}' takes nothing, \"rail\" or \"auto-hide\", not '{arg}'";
                return null;
            case Arg.PromptDirection when arg.Equals("previous", StringComparison.OrdinalIgnoreCase):
                return new ActionInvocation(def.Action, -1);
            case Arg.PromptDirection when arg.Equals("next", StringComparison.OrdinalIgnoreCase):
                return new ActionInvocation(def.Action, 1);
            case Arg.PromptDirection:
                error = $"'{name}' takes \"previous\" or \"next\"{(arg.Length > 0 ? $", not '{arg}'" : "")}";
                return null;
        }

        error = $"unsupported argument for '{name}'";
        return null;
    }

    /// <summary>An invocation written the way <see cref="Parse"/> reads it (e.g. "split right").</summary>
    public static string Format(ActionInvocation invocation) => invocation.Action switch
    {
        TerminalAction.CopyIfSelection => "copy if-selection",
        TerminalAction.NewTabFromProfile => $"new-tab {invocation.Profile ?? (invocation.Index + 1).ToString(CultureInfo.InvariantCulture)}",
        _ => Definitions.First(d => d.Action == invocation.Action) switch
        {
            { Arg: Arg.Direction } d => $"{d.Name} {invocation.Direction.ToString().ToLowerInvariant()}",
            { Arg: Arg.Workspace } d => $"{d.Name} {(invocation.Index < 0 ? "last" : (invocation.Index + 1).ToString(CultureInfo.InvariantCulture))}",
            { Arg: Arg.OptionalSidebarMode } d when invocation.Index >= 0 => $"{d.Name} {SidebarModeName(invocation.Index)}",
            { Arg: Arg.PromptDirection } d => $"{d.Name} {(invocation.Index < 0 ? "previous" : "next")}",
            var d => d.Name,
        },
    };

    /// <summary>The command palette's title for an invocation (e.g. "Split right", "Focus pane left").</summary>
    public static string Title(ActionInvocation invocation) => invocation.Action switch
    {
        TerminalAction.CopyIfSelection => "Copy",
        TerminalAction.NewTabFromProfile => $"New tab: {invocation.Profile ?? $"profile {invocation.Index + 1}"}",
        _ => Definitions.First(d => d.Action == invocation.Action) switch
        {
            { Arg: Arg.Direction } d => $"{d.Title} {invocation.Direction.ToString().ToLowerInvariant()}",
            { Arg: Arg.Workspace } d => invocation.Index < 0 ? "Go to the last workspace" : $"{d.Title} {invocation.Index + 1}",
            { Arg: Arg.OptionalSidebarMode } d when invocation.Index >= 0 => $"{d.Title} {SidebarModeName(invocation.Index)}",
            { Arg: Arg.PromptDirection } => invocation.Index < 0 ? "Jump to previous prompt" : "Jump to next prompt",
            var d => d.Title,
        },
    };

    private static string SidebarModeName(int mode) => (SidebarMode)mode == SidebarMode.AutoHide ? "auto-hide" : "rail";
}
