namespace Zinc.Settings;

public static partial class SettingsCatalog
{
    /// <summary>The settings in the Terminal section, in the order the window shows them.</summary>
    private static SettingDef[] TerminalSettings() =>
    [
        new("terminal", "Shell", "terminal.shell-integration", "Shell integration",
            "Marks prompts and commands in PowerShell, bash, and zsh (WSL too), so you can jump between prompts, copy a command's output, and see which commands failed.",
            SettingKind.Choice)
            {
                Choices = [new("On", "auto"), new("Off", "off")],
                Binding = Bind.Enum<ShellIntegrationMode>((c, v) => c with { ShellIntegration = v }),
            },
        new("terminal", "Shell", "terminal.command-marks", "Command marks", "Shows a bar beside the output of these commands.", SettingKind.Choice)
            {
                Choices = [new("Failed commands", "failures"), new("Every command", "all"), new("None", "none")],
                Binding = Bind.Enum<CommandMarks>((c, v) => c with { CommandMarks = v }),
            },
        new("terminal", "Shell", "terminal.close-on-exit", "When the program ends", "", SettingKind.Choice)
            {
                Choices =
                [
                    new("Close if it succeeded", "success", "When the program fails, the pane stays open so you can read why. Any key then closes the pane."),
                    new("Always close", "always"),
                    new("Keep it open", "never", "Any key closes the pane."),
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
            "Shows the folder at the prompt, and the running command otherwise. A program's own title still wins. Needs shell integration.",
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
                }, [Accepts.Choices]),
            },
        new("terminal", "Mouse", "terminal.word-boundaries", "Word boundaries",
            "These characters end a word when you double-click. Space and tab always do. When this is empty, Zinc uses the characters shown.", SettingKind.Text)
            {
                Placeholder = "'\"│`|:;,()[]{}<>$", DefaultText = "Zinc's own set",
                // Empty means the built-in set, so it's accepted too.
                Binding = Bind.Custom((c, v) => v is string s ? c with { WordBoundaries = s.Length > 0 ? s : null } : null, [Accepts.Text()]),
            },
        new("terminal", "Mouse", "terminal.click-to-move", "Click to move the cursor",
            "At a shell prompt, clicking in the command you're typing moves the cursor there. Needs shell integration.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.ClickToMove, (c, v) => c with { ClickToMove = v }) },
        new("terminal", "Mouse", "terminal.right-click", "Right-click", "Shift+F10 or the Menu key opens the menu anywhere.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Menu", "menu", "A right-click opens the menu too. It has copy, paste, find, the link or command you clicked, split, and close."),
                    new("Copy or paste", "copy-paste", "A right-click copies the selection, or pastes when nothing is selected."),
                    new("Paste", "paste"),
                ],
                Binding = Bind.Enum<RightClick>((c, v) => c with { RightClick = v }),
            },
        new("terminal", "Mouse", "terminal.link-click", "Open links with", "Web links and files in the terminal open with this.",
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
            "Pastes the clipboard unless the program uses the mouse.", SettingKind.Toggle)
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
        new("terminal", "Clipboard", "terminal.paste-warning", "Paste warning", "Asks before pasting text with more than one line.",
            SettingKind.Choice)
            {
                Binding = Bind.Enum<Terminal.PasteWarning>((c, v) => c with { PasteWarning = v }),
                Choices =
                [
                    new("Automatic", "auto", "Zinc asks only when the program would take each line as Enter and run it. A program that marks pastes (bracketed paste) gets no warning."),
                    new("Always", "multiline", "Zinc asks even when the program marks pastes (bracketed paste)."),
                    new("Never", "never"),
                ],
            },
        new("terminal", "Clipboard", "terminal.large-paste-warning", "Large pastes", "Asks before pasting more than 5 KB.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.LargePasteWarning, (c, v) => c with { LargePasteWarning = v }) },
        new("terminal", "Clipboard", "terminal.clipboard-write", "Programs may copy",
            "Copying with OSC 52, as tmux, Neovim, and remote shells do.", SettingKind.Toggle)
            {
                // true or false, or "allow" or "deny" as the read setting takes them.
                Binding = Bind.Custom((c, v) => v switch
                {
                    true or "allow" => c with { ClipboardWrite = true },
                    false or "deny" => c with { ClipboardWrite = false },
                    _ => null,
                }, [Accepts.Bool], c => c.ClipboardWrite),
            },
        new("terminal", "Clipboard", "terminal.clipboard-read", "Programs reading the clipboard",
            "Anything running, including a remote host, could read the clipboard.", SettingKind.Choice)
            {
                Choices =
                [
                    new("Ask each time", "ask", "Zinc asks whether to let the program read the clipboard once, for the tab, or not at all."),
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
                }, [Accepts.Choices]),
            },
        new("terminal", "Bell", "terminal.bell", "Bell", "Says what happens when a program rings the bell, for example at a failed tab completion.",
            SettingKind.Choice)
            {
                Choices =
                [
                    new("Tab dot", "attention", "When you aren't looking at the program's pane, a dot shows on the pane's tab and the taskbar button flashes."),
                    new("Flash the pane", "flash", "The program's pane flashes, and its tab also gets a dot, as with \"Tab dot\"."),
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
            "Lets zn, Zinc's command-line tool, open, arrange, and close tabs, type into them, and read them. Any program running as you could use zn.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.RemoteControl, (c, v) => c with { RemoteControl = v }) },
        new("terminal", "Command line", "cli.path", "Use zn in other terminals",
            "Adds zn to your PATH, so other terminals find zn too. Zinc's own terminals always do. Shells started from now on see the change.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.ZnOnPath, (c, v) => c with { ZnOnPath = v }) },
        new("terminal", "Troubleshooting", "terminal.input-method", "Input methods",
            "Type Japanese, Chinese, Korean, and more with Windows' input methods (IME). Turn it off if keys behave oddly.",
            SettingKind.Toggle) { Binding = Bind.Bool(c => c.InputMethod, (c, v) => c with { InputMethod = v }) },
        new("terminal", "Troubleshooting", "window.low-latency-drawing", "Low-latency drawing",
            "What you type shows up about one screen refresh sooner. Turn it off if a terminal draws wrong or stops drawing, and open an issue. Applies to terminals opened from now on.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.LowLatencyDrawing, (c, v) => c with { LowLatencyDrawing = v }) },
        new("terminal", "Troubleshooting", "window.free-hidden-memory", "Free memory of hidden tabs",
            "Background tabs, minimized windows, and the dismissed quick terminal give back their graphics memory and rebuild it when you return. Turn it off if switching tabs flashes. Applies only while \"Low-latency drawing\" is on.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.FreeHiddenMemory, (c, v) => c with { FreeHiddenMemory = v }) },
        new("terminal", "Troubleshooting", "window.free-hidden-after", "Free memory after",
            "This is in seconds unless you add a unit, like 45s or 2m.", SettingKind.Text)
            {
                Placeholder = "60", NumbersAsNumbers = true,
                Binding = Bind.Custom(
                    (c, v) => Bind.AsText(v) is { } s && Config.TryDuration(s, out var d) && d >= TimeSpan.FromSeconds(1)
                        ? c with { FreeHiddenAfter = d }
                        : null,
                    [Accepts.Number(1, null), Accepts.Text()],
                    c => Bind.Tidy(c.FreeHiddenAfter.TotalSeconds)),
            },
        new("terminal", "Troubleshooting", "window.software-rendering", "Software rendering",
            "Draws with the processor instead of the graphics card, which is slower. Use it for broken graphics drivers, some virtual machines, and remote desktop glitches. Applies to terminals opened from now on.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.SoftwareRendering, (c, v) => c with { SoftwareRendering = v }) },
    ];
}
