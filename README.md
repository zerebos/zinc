# Phosphor

A Windows terminal emulator built on **WinUI 3** (Windows App SDK 2.5) and
**libghostty-vt**, the terminal emulation core extracted from
[Ghostty](https://ghostty.org).

```
 keyboard/mouse ──► TerminalControl ──encode (libghostty key/mouse encoders)──► ConPTY ──► shell
                          ▲                                                       │
                     Win2D renderer ◄── render state ◄── libghostty-vt terminal ◄─┘ (reader thread)
```

- **libghostty-vt** (`ghostty-vt.dll`, C API via P/Invoke) does VT parsing,
  screen/scrollback state, reflow, selection, and key/mouse/paste encoding.
- **ConPTY** runs the child process. Phosphor ships Windows Terminal's
  out-of-box ConPTY (`conpty.dll` + `OpenConsole.exe` from the
  `Microsoft.Windows.Console.ConPTY` package) and falls back to the inbox one
  if those files are missing.
- **Win2D** renders the libghostty render state: backgrounds as merged
  rectangles, text as glyph runs with a fixed one-cell advance so every glyph
  sits exactly on the grid, and DirectWrite text layout as the fallback for
  glyphs the primary font lacks (emoji, CJK, clusters).

## Requirements

- Windows 10 1809+ (developed on Windows 11 24H2), x64 or ARM64
- .NET SDK 10
- Visual Studio Build Tools with the MSVC toolchain and a Windows 10/11 SDK
  (used by Zig to link the DLL)
- [Zig](https://ziglang.org) **0.16.x** on `PATH` (Ghostty's minimum)
- Git Bash (or any POSIX shell) for the build script

## Build and run

```bash
# 1. Build libghostty-vt at the pinned Ghostty commit -> native/x64/ghostty-vt.dll
scripts/build-libghostty.sh x64

# 2. Build and run the app
dotnet run --project src/Phosphor

# 3. Run the tests (ABI check against libghostty's manifest, headless ConPTY sessions)
dotnet test
```

The app is unpackaged and Windows App SDK self-contained, so
`src/Phosphor/bin/x64/<Config>/net10.0-windows10.0.26100.0/win-x64/Phosphor.exe`
runs directly.

## Installing locally

```bash
pwsh scripts/install.ps1                  # Release build -> Start Menu "Phosphor" (Raycast finds it)
pwsh scripts/install.ps1 -InstallGitHook  # also reinstall in the background after each commit
pwsh scripts/install.ps1 -Uninstall       # remove install, shortcut and hook (keeps config)
```

Each install publishes to a new folder under `%LOCALAPPDATA%\Programs\Phosphor\versions\`
and repoints the Start Menu shortcut, so open windows are never disturbed; the three
newest versions are kept (`-Keep N`). Requires .NET 10 (the runtime isn't bundled).

## Configuration

The first time Phosphor starts, a welcome page asks how you like to work (**Simple**: tabs along
the top, one terminal each; **Workspaces**: in a sidebar or along the top, each with its own tabs
and splits), a color scheme and font (with a preview; or two schemes that follow Windows' light
and dark mode), your default shell, and whether a long command finishing in the background shows
a Windows notification, and writes your answers to `config.toml`. Reopen it from the command palette (**Welcome…**): it changes only what
you change there and leaves the rest of the file as it is.

**Settings** (`Ctrl+,`, the command palette or the tray menu) edits the settings below in a window
of its own, next to your terminals: each change is written to `config.toml` right away and applied
as you make it (only that line changes; choosing a default removes the line), and **Undo changes**
(at the bottom while there are changes) puts the file back as it was when you opened the window.
Each setting shows its key in the file; the arrow beside a changed setting's name resets it.
While `config.toml` has an error, Settings says so and changes nothing. The window reopens where
you left it. Under **Profiles**: the default profile, every profile in your order (drag them, or
**⋯ → Move up/down**; the new-tab menu follows it), found on this PC or your own (add, delete, or
undo your changes to a found one), each one's name, command, folder, icon and visibility, and its
own font, colors and padding: each shows what it inherits until you change it there, and the
reset arrow makes it inherit again. Also which shells Phosphor looks for. Under **Keyboard**:
every action with its keys (filter by name or key); click a key to take it away, **+** to add one
by pressing it (it says if another action has that key, and takes it over), the reset arrow for
the defaults. Changes go in `[keys]`: taking away a default key writes `"chord" = "none"`. Key
sequences are listed but added in the file.

`%APPDATA%\Phosphor\config.toml` (override the path with `PHOSPHOR_CONFIG`), in
[TOML](https://toml.io): settings grouped into sections, `#` comments. Problems (unknown keys,
bad values, TOML mistakes) are shown in a bar naming the file and line; everything else
still applies.

Changes apply as soon as the file is saved (also after editing an included file or a scheme in
`themes\`), to every open window: colors, fonts, padding and profiles update in running tabs,
and a profile's command applies to tabs opened afterwards. Where workspace tabs sit (`top` /
`left`) applies to new windows. A save that isn't valid TOML, such as one in the middle of an
edit, is reported and ignored until the file is valid again. **Reload config** in the command
palette re-reads it on demand.

```toml
default-profile = "wsl:Fedora"

[font]
family = "JetBrainsMono NF"
size = 11                    # points, like Ghostty and Windows Terminal

[colors]
theme = "Dracula"            # a bundled scheme, or one of yours (below)
background = "#181818"       # individual colors override the theme

[window]
workspace-tabs = "left"

[profile."wsl:Fedora"]
name = "Fedora"
command = 'wsl.exe -d Fedora --cd ~'
colors.theme = "0x96f"
```

Single-quoted strings are literal, so Windows paths need no escaping: `'C:\msys64\ucrt64.ico'`.

**`[font]`**
- `family`: any installed monospace family; Nerd Fonts work. The default, **Cascadia Mono NF**
  (Microsoft's Nerd Font build of Windows Terminal's font, so prompt icons show out of the box),
  ships with Phosphor and needs no installing. A family that isn't installed falls back to it,
  then to Cascadia Mono and Consolas.
- `size`: in points; `Ctrl+0` resets to this.
- `builtin-glyphs`: `true` (default) draws box drawing, block elements, Symbols for Legacy
  Computing (sextants, wedges: what editors like flow draw their tabs with), Powerline
  separators and Braille with Ghostty's own sprite rasterizer instead of taking them from the
  font: pixel-exact for the cell, so lines, prompt segments and blocks meet without seams in any
  font, and characters most fonts lack still show. `false` uses the font's (as a font designed
  them); what the font lacks then comes from the bundled Cascadia Mono NF where it has it.
- `adjust-box-thickness`: the built-in glyphs' lines are as thick as the font's underline, rounded
  up to whole pixels (as in Ghostty; thin with light fonts like JetBrains Mono at small sizes).
  `1` adds a pixel, `-1` takes one away (never below 1), `"20%"` scales.

**`[colors]`**
- `theme`: a color scheme by name. About 650 come with Phosphor (the
  [iTerm2-Color-Schemes](https://github.com/mbadolato/iTerm2-Color-Schemes) collection);
  your own go in `%APPDATA%\Phosphor\themes\`, one file per scheme in Ghostty's theme format
  (`background = #181818`, `palette = 1=#AC4242`, ...), named as `theme` refers to them. Yours
  win over bundled ones with the same name.
  **Light and dark**: `theme = "light:Catppuccin Latte,dark:Catppuccin Mocha"` (Ghostty's syntax)
  follows Windows' app mode (Settings > Personalization > Colors), switching as it changes.
  Programs that ask whether the terminal is light or dark (`CSI ? 996 n`) are told the side in
  force (with a single scheme: whether its background is dark), and those that subscribe (mode
  2031, e.g. neovim) hear of each change, so they can switch their own colors too.
- `background`, `foreground`, `cursor`: `"#rrggbb"`.
- `palette`: an array of up to 256 colors from index 0, or single entries: `palette.1 = "#AC4242"`.

**`[terminal]`**
- `padding-x`, `padding-y`: points; one number, or `[left, right]` / `[top, bottom]`.
- `mouse-pointer`: `"arrow"` (default, like Windows Terminal) or `"text"` (an I-beam over text,
  like Ghostty). It's always an arrow over the scrollbar and while an app uses the mouse.
- `clipboard-write`: `true` (default) or `false`, for programs copying with OSC 52 (tmux,
  neovim, remote shells over ssh).
- `clipboard-read`: `false` (default) or `true`, for programs reading the clipboard with
  OSC 52. Off by default because anything running in the terminal, including a remote host,
  could read it.
- `command-marks`: `"failures"` (default), `"all"` or `"none"`: which commands get a bar beside
  them ([shell integration](#shell-integration)).
- `shell-integration`: `"auto"` (default) loads Phosphor's [shell integration](#shell-integration)
  into PowerShell, bash and zsh; `"off"` starts shells as they are.

**`[window]`**: `workspace-tabs`, `sidebar`, `title-bar`, `tab-groups`, `group-tab-strip`, `unfocused-split-opacity`,
`unfocused-split-fill`, `save-state` and `quit-after-last-window-closed`, described with the
features they control below.

**`[notify]`**: `command-finished`, `command-finished-after` (see
[Shell integration](#shell-integration)), and `programs`: notifications that programs send
(OSC 9 and OSC 777, e.g. a build script's `printf '\e]9;Build done\a'`). Out of view they put a
blue dot on the tab, like a finished command; `"toast"` also shows a Windows notification while
Phosphor is in the background (`"toast-always"`: whenever that pane is out of view), `"never"`
ignores them. Programs that report progress (OSC 9;4)
get a small ring on their tab and a progress bar on Phosphor's taskbar button.
Windows notifications come from Phosphor's tray icon and respect Do Not Disturb.

**`include`** (at the top): other files to read first, e.g. `include = ["profiles/*.toml"]`,
relative to this one. Settings in the including file win.

### Profiles

Each `[profile.<id>]` section is a profile, starting from the settings above and overriding
`[font]`, `[colors]` and `[terminal]` for itself (as subsections, or dotted keys like
`font.size = 12`). Profiles appear in the new-tab menu in file order. The id is what
`default-profile` and saved sessions refer to, so renaming a profile (`name`) breaks nothing;
quote ids with spaces or colons: `[profile."ssh:homelab"]`.

```toml
[profile.git-bash]
name = "Git Bash"
command = '"C:\Program Files\Git\bin\bash.exe" --login -i'
working-directory = "~/dev"

[profile.git-bash.font]
size = 12
```

- `default-profile` (at the top): the profile Phosphor starts with; defaults to the first.
- `profile-order` (at the top): profile ids in the order menus list them (Settings writes it when
  you reorder); profiles not in it follow in their usual order.
- `name`: shown in menus and tabs; defaults to the id.
- `command`: the command line; without one, PowerShell 7, else Windows PowerShell, else cmd.
- `icon`: image shown for the profile in tabs. Without one, Phosphor picks one like Windows
  Terminal: the WSL distro's registered icon, Git for Windows' icon for Git Bash, a network
  glyph for `ssh`, else the command's executable icon.
- `working-directory`: `"~"` (the default) or a path; `~/sub` and `%VARS%` expand.
- `hidden = true`: keeps a profile out of menus.
- Without any profiles, of your own or found, the settings above act as a single "Default" profile.

Phosphor also finds the shells installed on this machine every time it reads the config, as
Windows Terminal does, and lists them after your own profiles:

| Found | Id | Name |
| --- | --- | --- |
| WSL distros (the default one first; Docker Desktop's are skipped) | `wsl:<distro>` | the distro's name |
| PowerShell 7, Windows PowerShell, cmd | `pwsh`, `powershell`, `cmd` | PowerShell, Windows PowerShell, Command Prompt |
| Git for Windows | `git-bash` | Git Bash |
| MSYS2 (its UCRT64 environment) | `msys2:ucrt64` | MSYS2 UCRT64 |
| Visual Studio installs | `vs:<instance>:pwsh`, `vs:<instance>:cmd` | Developer PowerShell / Command Prompt for … |
| Hosts in `~/.ssh/config` (not patterns like `*`; `Include`s are followed) | `ssh:<host>` | SSH - &lt;host&gt; |

A `[profile]` section with a found profile's id changes it, starting from what was found, and
places it where the section is (an empty section only places it); `hidden = true` hides it. A section for a found profile that
isn't installed any more is hidden too. `[discover]` turns sources off:

```toml
[discover]
ssh = false            # also: wsl, powershell, cmd, git-bash, msys2, visual-studio

[profile."wsl:Ubuntu"]
colors.theme = "Ubuntu"

[profile."msys2:ucrt64"]
hidden = true
```

### Quick terminal

A global hotkey (default ``Win+` ``) summons a terminal window above other windows and
dismisses it again, like Ghostty's quick terminal. It stays out of the taskbar
and Alt+Tab, and it keeps its tabs while hidden. In `[quick-terminal]`:

- `hotkey`: e.g. `"win+grave"`, `"ctrl+alt+t"` (needs a modifier).
- `position`: `"top"` (default), `"bottom"`, `"left"`, `"right"`, `"center"`.
- `screen`: `"mouse"` (default; the monitor under the pointer) or `"main"`.
- `size`: `"N%"` / `"Npx"`, or `[primary, secondary]`. Primary is the height for top/bottom and
  the width for left/right/center; secondary is the other side. Defaults: 50% for the edges,
  `["70%", "60%"]` for center.
- `autohide`: `true` (default) hides it when it loses focus.

If another app holds the hotkey (Windows Terminal's quake mode uses ``Win+` ``), windows
show a notice, and Phosphor retries whenever one of its windows is activated.

### Tray icon and background

Only one Phosphor runs at a time: launching it again (Start menu, a launcher, the command line)
opens a new window in the running one — with the command given, if any — or, when it's in the
tray with no window open, reopens the saved windows.

Phosphor shows a notification-area icon while it runs. Clicking it brings back the most
recently used window (or the saved windows); right-clicking offers New window, Show/Hide quick
terminal, and Quit Phosphor, which closes every window, the hidden quick terminal included.
Quit Phosphor is also in the command palette.

When the last window closes, Phosphor keeps running in the tray while its quick terminal hotkey
works (or the quick terminal is showing), so the hotkey still summons it; otherwise it exits.
In `[window]`, `quit-after-last-window-closed = true` always exits, `false` always stays in the tray.

### Reopening where you left off

Phosphor remembers its windows: their size and place, their workspaces (titles, colors), split
layouts and tabs, and which were shown. Next time it starts (without a command on its command
line), they reopen with fresh shells in the same folders. What the terminals showed isn't kept.
The state is saved as you go (`%APPDATA%\Phosphor\session.json`), so a crash keeps it too;
closing the last window keeps that window for next time. `save-state = "never"` in `[window]` turns this
off (`"always"`, the default, keeps it on).

A tab reopens in the folder its shell last *reported* (OSC 7, or OSC 9;9 / OSC 1337).
PowerShell, bash and zsh report it through Phosphor's [shell integration](#shell-integration),
with nothing to set up; other shells need to send it themselves, e.g. from their prompt:
`printf '\e]7;file://%s%s\e\\' "$HOSTNAME" "$PWD"`. Without it, tabs reopen where they first
started. WSL tabs reopen through `wsl.exe --cd`, Git Bash paths (`/c/...`) map to their drive.

### Shell integration

A shell can mark its prompts and commands (OSC 133): where each prompt starts, where a command's
output starts, and how it ended. With those marks, Phosphor can:

- **jump between prompts**: `Ctrl+Shift+↑` / `Ctrl+Shift+↓` scroll the previous or next prompt to
  the top of the pane (without marks they scroll a line);
- **copy a command's output**: *Copy command output* and *Select command output* in the command
  palette (`copy-output`, `select-output` in `[keys]`) take the command at the top of the pane
  when you've scrolled back, otherwise the last one;
- **show what failed**: a red bar beside a failed command, and ticks on the scrollbar at each
  prompt (red for failures). `command-marks` in `[terminal]`: `"failures"` (default), `"all"`
  (successes get a green bar too) or `"none"`;
- **show what's running**: with workspaces on the left, the title bar shows the running command and
  how long it has been going;
- **tell you when a long command finishes out of view**: in another workspace, tab or pane
  (anywhere but the focused one), or while you're in another app, a command that ran 10 seconds
  or more puts a pulsing dot after its tab's title (on the icon in the rail): green, or red if it
  failed, until you focus it. A pane without a tab strip shows it in its top-right corner, and a
  workspace with several tabs to look at shows how many. The taskbar button flashes if Phosphor
  is in the background. In
  `[notify]`: `command-finished = "toast"` shows a Windows notification instead of the flash
  (click it to go to the pane), `"toast-always"` shows one even while you're in Phosphor (just
  not looking at that pane), `"never"` turns this off, and `command-finished-after` sets what
  counts as long (`30`, `"45s"`, `"2m"`);
- **ask before closing a running command**: closing a tab, pane, workspace or window, or
  quitting, asks first while a command is running there (never at an idle prompt).

The marks are invisible, and **PowerShell (7 and Windows PowerShell), bash and zsh get them
automatically**, in WSL, Git Bash and MSYS2 too, along with folder reports for
[reopening tabs](#reopening-where-you-left-off). Your startup files run as usual; Phosphor's
script (in `ShellIntegration\` next to Phosphor.exe) runs after them and changes nothing you see:

- **PowerShell** is started with `-NoExit -Command` running it after your profile. A profile
  started with `-Command` or `-File` of its own is left alone.
- **bash** finds it in `PROMPT_COMMAND`, which runs it once at the first prompt (then puts
  `PROMPT_COMMAND` back as your startup files left it).
- **zsh** finds it through `ZDOTDIR`: Phosphor's `.zshenv` puts your `ZDOTDIR` back, loads your
  `.zshenv` as zsh would have, and sets the rest up at the first prompt.
- **WSL** gets the same variables through `WSLENV`, so it works with whichever of these is your
  login shell.

Marks your prompt already sends are left to it: **powerlevel10k's** own integration
(`typeset -g POWERLEVEL9K_TERM_SHELL_INTEGRATION=true` in `p10k.zsh`; it knows its own prompt best) or a
hand-written one, and a folder report you send yourself. Phosphor then adds only what's missing
(powerlevel10k sends no folder report).

`shell-integration = "off"` in `[terminal]` (or in one profile) starts shells exactly as anywhere
else. If your startup files hand over to something else (tmux, or `exec` another shell), the
marks don't follow; the scripts in `ShellIntegration\` show what to send from there.

**cmd** can mark prompts (for jumping), but not commands or exit codes:
`setx PROMPT "$e]133;D$e\$e]133;A$e\$P$G$e]133;B$e\"`.

Other shells (fish, nushell and more) aren't covered yet. Recent fish versions send the prompt
marks on their own. For anything else, the scripts in `ShellIntegration\` show what to send.

## Choosing a shell

1. Arguments after the executable: `Phosphor.exe "C:\Program Files\Git\bin\bash.exe" --login -i`
2. The `PHOSPHOR_SHELL` environment variable (a full command line)
3. The default profile's `command` (tabs opened later use their profile's)
4. Otherwise `pwsh.exe`, then `powershell.exe`, then `cmd.exe`

Children get `COLORTERM=truecolor` and `TERM_PROGRAM=phosphor`.

## Keys and mouse

| Input | Action |
| --- | --- |
| `Ctrl+Shift+C`, or `Ctrl+C` with a selection | Copy selection |
| `Ctrl+V`, `Ctrl+Shift+V`, `Shift+Insert` | Paste (bracketed when the app enables it) |
| Right click | Copy the selection if there is one, otherwise paste |
| Drag / double-click / triple-click | Select cells / word / line |
| Mouse wheel, `Shift+PgUp/PgDn` | Scroll back (`Ctrl+Shift+Home/End` jump to top/bottom) |
| `Ctrl+=`, `Ctrl+-`, `Ctrl+0` | Font size bigger / smaller / reset |
| `Ctrl+Shift+P` | Command palette: every action, profile and workspace, fuzzy-searchable; recently used first. Type `@` to search this window's tabs instead |
| `Ctrl+Shift+O` | Go to tab: the palette's `@` (title, running command, folder, workspace; tabs with a dot first) |

Some palette commands ask for one more thing in the same box: **Change color scheme…** (this
tab only; the list previews each scheme on the tab as you move through it, Esc puts the old one
back; the tab keeps it across restarts; the scheme's colors replace all of that tab's, including
ones your config sets), **Set default color scheme…** (writes `colors.theme` in your config,
keeping the rest of the file as it was; colors the config sets itself, like `background`, keep
winning over the scheme, and the preview shows them that way), **New tab running…** (opens the focused
tab's shell in the same folder and types the command there, as if by hand: `htop` in a WSL tab,
`dir` in cmd, your aliases; the tab stays at the shell afterwards) and **New tab in folder…** (`~/src`, `D:\work`, or relative to where the
focused shell is; WSL tabs take Linux paths and `C:\...`).
| `Ctrl+Shift+F` | Find in the pane's scrollback |
| `Ctrl+Shift+↑` / `Ctrl+Shift+↓` | Previous / next prompt ([shell integration](#shell-integration)); a line without it |
| `Ctrl+Shift+B` | With workspaces on the left: sidebar full → rail → auto-hide |
| `Ctrl+Shift+T` / `Ctrl+Shift+1..9` | New tab in the focused pane (its profile / profile N) |
| `Ctrl+Alt+T` | New workspace (default profile) |
| `Ctrl+Shift+Space`, ▾ button | Profile menu: new workspace (Alt+click: open as a split; Shift+click: new window) |
| `Ctrl+Shift+D` | Duplicate tab (in the same pane) |
| `Ctrl+Shift+W` | Close tab (its pane, then workspace, when it's the last) |
| `Alt+Shift+Plus` / `Alt+Shift+Minus` | Split right / down (with the focused tab's profile) |
| `Alt+Shift+D` | Duplicate pane (splits along the longer side) |
| `Alt+Arrow` | Focus the pane in that direction |
| `Alt+Shift+Arrow` | Move the nearest divider (or drag it) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Next / previous tab in the focused pane; with one tab there, next / previous workspace |
| `Ctrl+Alt+1..8`, `Ctrl+Alt+9` | Go to workspace N / last workspace |
| `Ctrl+Shift+N` | New window |
| `Shift` + mouse | Bypass app mouse reporting to select text |
| `Ctrl` + hover / click | Underline / open the link under the pointer |

### Changing keys

The table above is the default set (Windows Terminal's). A `[keys]` table in the config changes
it: each line binds a key to an action, and `"none"` removes a default. Changes apply when the
file is saved, and the command palette and menus show your keys.

```toml
[keys]
"ctrl+shift+w" = "none"               # free it for something else
"ctrl+w" = "close-tab"
"alt+shift+plus" = "split down"
"ctrl+shift+1" = "new-tab wsl:Fedora" # a profile by id (or by menu position: "new-tab 1")
"ctrl+alt+r" = "reload-config"
```

Keys are modifiers (`ctrl`, `alt`, `shift`) plus one key: a letter or digit, `f1`–`f24`,
`plus`, `minus`, `grave`, `tab`, `enter`, `space`, `esc`, `backspace`, `insert`, `delete`,
`home`, `end`, `pgup`, `pgdn`, arrows (`up` …), `numpad0`–`numpad9`, `numpad_plus`,
`numpad_minus`. A key bound twice (e.g. `ctrl+shift+c` and `shift+ctrl+c`), an unknown key or
an unknown action is reported with its line, and the rest still applies.

**Key sequences**, like tmux's prefix: keys joined by `>` (spaces around it are fine), pressed one
after another.

```toml
[keys]
"ctrl+a > c" = "new-tab"
"ctrl+a > shift+5" = "split right"
"ctrl+a > w > n" = "new-window"   # as long as you like
```

The first key (the *leader*) only starts sequences: it doesn't reach the shell, and a key bound
on its own that is also a leader loses that binding (the config notice says so). While a sequence
waits, a hint at the bottom of the pane lists what can come next. There's no time limit. Escape,
or a key that continues no sequence, cancels it without typing anything. The leader pressed twice
types it once, so Ctrl+A still reaches the shell (start of line in bash and zsh) as Ctrl+A,
Ctrl+A, unless you bind that sequence too. The palette and menus show sequences as "Ctrl+A, C".

| Action | What it does |
| --- | --- |
| `copy` | Copy the selection. `copy if-selection` lets the key through to the app when nothing is selected (the default `Ctrl+C`). |
| `paste` | Paste the clipboard. |
| `find` | Open the find box. |
| `increase-font-size`, `decrease-font-size`, `reset-font-size` | Zoom in, out, or back to the configured size. |
| `scroll-page-up`, `scroll-page-down`, `scroll-to-top`, `scroll-to-bottom` | Scroll the scrollback. |
| `new-tab` | A new tab (in the focused pane, with tab groups). `new-tab <profile>`: that profile, by id or menu position. |
| `new-workspace` | A new workspace with the default profile. |
| `new-window` | A new window with the default profile. |
| `duplicate-tab` | The focused tab's command again, next to it. |
| `close-tab` | Close the focused tab (its pane goes with its last tab). |
| `next-tab`, `previous-tab` | The pane's next or previous tab; with one tab there, the next or previous workspace. |
| `go-to-workspace <n>` | Show workspace n (from 1), or `go-to-workspace last`. |
| `split <direction>` | Split the focused pane: `right`, `down`, `left` or `up`. |
| `duplicate-pane` | Split, running the focused tab's command again. |
| `focus <direction>` | Focus the neighboring pane. |
| `resize <direction>` | Move the focused pane's divider. |
| `profile-menu` | Open the ▾ menu of profiles. |
| `command-palette` | Open the command palette. |
| `tab-switcher` | Open the palette searching this window's tabs (its `@`). |
| `next-attention` | Go to the next tab with a dot: failed commands first, then programs' notifications, then finished commands. |
| `jump-to-prompt previous`, `jump-to-prompt next` | Scroll the previous or next prompt to the top (needs [shell integration](#shell-integration); without it, a line). |
| `select-output`, `copy-output` | Select or copy the output of the command at the top of the pane, or the last one ([shell integration](#shell-integration)). |
| `toggle-sidebar` | With workspaces on the left: cycle the sidebar through full, rail and auto-hide. `toggle-sidebar rail` (or `auto-hide`) switches between that and full. |
| `toggle-title-bar` | With workspaces on the left: auto-hide the title bar, or show it again. |
| `focus-mode` | With workspaces on the left: auto-hide the title bar and the sidebar; again to bring them back. |
| `reload-config` | Read the config again. |
| `open-settings` | Open the settings window (`Ctrl+,`). |
| `quit` | Close every window and exit. |

Links are OSC 8 hyperlinks (e.g. `ls --hyperlink`) and plain `http(s)`,
`ftp`, `mailto` and `file` URLs in the text. Hovering a hyperlink with Ctrl shows its
target in a tooltip. Web and mail links open in their default app. `file:` links are
shown in Explorer (the file selected) rather than opened, so a link can't run a program.
File links follow the pane's shell: in WSL panes, `/home/you` means
`\\wsl.localhost\<distro>\home\you` and `/mnt/c/..` means `C:\..`; in Git Bash and MSYS2,
`/c/..` means `C:\..` and `/usr/..` is under the installation. Links to files on other
machines (e.g. over ssh) are ignored.

Each pane keeps about 10,000 lines of scrollback.

Each pane has a scrollbar (thin until the pointer nears the right edge) while there is scrollback; it hides for
full-screen apps, whose alternate screen has none. The selected tab takes the focused
pane's background color unless you give the tab a color.

When an app turns on mouse reporting (vim, htop, tmux, flow), clicks, drags and
the wheel go to the app, and the pointer becomes an arrow instead of an I-beam. On the alternate screen with alternate-scroll mode, the
wheel sends arrow keys (`less`, `man`).

Images use the [kitty graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/):
direct placements (e.g. fastfetch `--kitty-direct`) and Unicode placeholders (e.g. yazi's
previews). Programs may send image data inline, or name a file to load
(or a temp file under `%TEMP%`); paths from WSL or a remote host don't resolve here, so
those programs must send data inline. Images keep their place in scrollback. Programs detect
support with the protocol's query; Phosphor answers it (and identifies as libghostty).

Find (`Ctrl+Shift+F`) opens a box at the pane's top right. Matches highlight as you type, with
their positions marked beside the scrollbar; `Enter` moves to the next older match and
`Shift+Enter` the next newer one (both wrap), and `Esc` closes the box. `Aa` makes it
case-sensitive and `.*` treats the text as a .NET regular expression (`^`/`$` are the start
and end of a line). Lines soft-wrapped across rows are searched as one line, so matches can
continue onto the next row; real line breaks separate lines. Results refresh as new output
arrives.

### Workspaces, panes and tabs

The title-bar tabs are **workspaces**: each has its own layout of split **panes**, and a pane
holds one or more **tabs**, each a terminal. A pane with two or more tabs shows its own strip
of them (click to switch, middle-click or × to close, right-click to close others or move a
tab to a new workspace or window, + for another tab). New tabs open in the focused pane with
its current tab's profile; the title bar's + and ▾ open workspaces.

- `tab-groups = false` (in `[window]`) keeps one terminal per pane, like Windows Terminal: new tabs
  open as workspaces.
- In a split workspace the focused pane is marked by an accent line along its top (on the shown
  tab when the pane has a strip), and the others are dimmed. `unfocused-split-opacity` (in `[window]`,
  0.15–1, default 0.7 as in Ghostty; 1 = no dimming) sets how much, and `unfocused-split-fill`
  what they fade toward (default: each pane's background).
- `group-tab-strip = "always"` (in `[window]`) shows the strip on every pane, even with one tab
  (default `"auto"`: only with two or more).
- `workspace-tabs = "left"` (in `[window]`) lists workspaces in a column on the left, under a plain
  title bar, instead of as title-bar tabs (default `"top"`). The list works like the tabs: click,
  close, right-click, drag to reorder or anywhere a title-bar tab can go. The title bar then shows
  what the focused tab is running and the folder its shell last reported (see Reopening where you
  left off); clicking it opens the command palette.
- `sidebar` (in `[window]`, with `workspace-tabs = "left"`): `"full"` (default), `"rail"` (a narrow
  column of workspace icons; names in tooltips) or `"auto-hide"` (hidden until the pointer
  touches the window's left edge, or a dragged tab does; it slides back once the pointer leaves).
  `title-bar`: `"show"` (default) or `"auto-hide"` (revealed at the top edge, window buttons
  included). `Ctrl+Shift+B` and the palette's Toggle sidebar / Toggle title bar / Focus mode
  switch them per window, and a reopened window keeps its own (until you change these settings
  in the config, which then win).

Right-clicking a workspace offers rename, workspace color, duplicate, split, moving it to a new
window, export text (whole scrollback to a file), and closing this / other / right-hand
workspaces. Its Split pane menu can also move the focused pane out to a new workspace or window.

Workspaces can be dragged, with their terminals still running:

- out of the tab strip, to open in a new window where you drop them;
- onto another Phosphor window's tabs (or its +/▾ buttons), to move there;
- onto the edge of a pane (the outer third on that side), to join that workspace as a split on
  that side, keeping their own split layout;
- onto the middle of a pane, to join that pane as tabs (with tab groups on).

A pane's own tabs can be dragged too:

- along their strip, to reorder them;
- onto another pane's strip or middle, to join it; onto a pane's edge, to become a new pane
  there (also beside its own pane, when that pane has other tabs);
- onto the title bar's tabs, to become a workspace of their own; resting on a workspace's tab
  for a moment switches to it, so a tab can be carried into another workspace's panes;
- outside every Phosphor window, to open in a new window;
- into panes and title bars of other Phosphor windows.

The drop area is highlighted while you drag.

If the shell exits with a non-zero code, the window stays open showing the code
until a key is pressed; a clean exit closes it.

## Layout

```
scripts/build-libghostty.sh       fetch + build libghostty-vt at a pinned commit
scripts/gen-keys.py               regenerate GhosttyKey.g.cs from the C header
scripts/gen-icon.py               assets/*.svg -> src/Phosphor/Assets/Phosphor.ico (pixel-snapped sizes)
assets/                           source artwork (app icon SVG)
src/Phosphor.Native.targets        ghostty-vt.dll + bundled ConPTY for runnable projects
src/Phosphor.Core/                 everything testable without a window:
  Ghostty/                          P/Invoke bindings
  Pty/ConPty.cs                     pseudoconsole + child process + reader thread
  Terminal/TerminalSession.cs       terminal + pty glue, libghostty callbacks
  Input/                            scan code -> GhosttyKey map, keyboard layout translation
  Config.cs, ShellResolver.cs       config file, command resolution
src/Phosphor/                      WinUI app: window, TerminalControl (Win2D render + input)
tests/Phosphor.Tests/              xUnit v3: ABI check, headless sessions, unit tests
```

## Updating libghostty-vt

The libghostty-vt C API is explicitly unstable. To move to a newer Ghostty:

1. Change `COMMIT` in `scripts/build-libghostty.sh` and run it.
2. Run `python scripts/gen-keys.py`.
3. Run `dotnet test`. `AbiTests` compares every binding struct, enum and imported
   function against the library's own ABI manifest (`ghostty_type_json()`).

## Known gaps

- IME (CJK input methods) is untested; there is no composition UI.
- Keybindings aren't configurable yet.
- Ligatures are intentionally disabled (glyphs are placed per cell).
- Desktop notifications are parsed by libghostty but not surfaced yet. Sixel isn't supported
  (libghostty has no sixel API); kitty graphics animation shows the current frame only.
- Links and URLs continue across rows the terminal wrapped, but a URL an app broke with its own
  line break (e.g. to fit a fixed width) is cut there.
