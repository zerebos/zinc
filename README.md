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

`%APPDATA%\Phosphor\config` (override the path with `PHOSPHOR_CONFIG`), in Ghostty's
syntax: one `key = value` per line, `#` comments, later lines win.

```
font-family = JetBrainsMono NF
font-size = 11
command = wsl.exe --cd ~
background = #181818
foreground = #d8d8d8
cursor-color = #d8d8d8
palette = 1=#AC4242
window-padding-x = 15
window-padding-y = 10
```

- `font-family`: any installed monospace family; Nerd Fonts work. If it isn't
  installed, Phosphor falls back to Cascadia Mono, then Consolas.
- `font-size`: in points, like Ghostty and Windows Terminal; `Ctrl+0` resets to this.
- `command`: the default command line (see below for precedence).
- `background`, `foreground`, `cursor-color`: `#rrggbb`.
- `palette = N=#rrggbb`: override palette entry N (0-255), one line per entry.
- `window-padding-x`, `window-padding-y`: points; `N` or `left,right` / `top,bottom`.
- `mouse-pointer`: `arrow` (default, like Windows Terminal) or `text` (an I-beam over text,
  like Ghostty). It's always an arrow over the scrollbar and while an app uses the mouse.
- `clipboard-write`: `allow` (default) or `deny`, for programs copying with OSC 52
  (tmux, neovim, remote shells over ssh).
- `clipboard-read`: `deny` (default) or `allow`, for programs reading the clipboard with
  OSC 52. Off by default because anything running in the terminal, including a remote
  host, could read it. Ghostty's `ask` isn't supported yet.

These are Ghostty's key names and units, so color and font lines can be copied
straight from a Ghostty config.

### Profiles

Lines before the first section are global; each `[profile "Name"]` section inherits
them and overrides what it sets. Profiles appear in file order.

```
font-family = JetBrainsMono NF
default-profile = Fedora

[profile "Fedora"]
command = wsl.exe -d Fedora --cd ~
icon = C:\Users\me\Pictures\fedora.png
background = #181818

[profile "Git Bash"]
command = "C:\Program Files\Git\bin\bash.exe" --login -i
working-directory = ~/dev
```

- `default-profile` (global only): the profile Phosphor starts with; defaults to the first.
- `icon`: image shown for the profile in tabs. Without one, Phosphor picks one like Windows
  Terminal: the WSL distro's registered icon, Git for Windows' icon for Git Bash, a network
  glyph for `ssh`, else the command's executable icon.
- `working-directory`: `~`/`home` (the default) or a path; `~/sub` and `%VARS%` expand.
- Without any sections, the global settings act as a single "Default" profile.
- Problems (unknown keys, bad values, duplicate profiles) are collected as warnings
  instead of stopping Phosphor.

This section syntax is an interim step; a Phosphor-specific config format is planned.

### Quick terminal

A global hotkey (default ``Win+` ``) summons a terminal window above other windows and
dismisses it again, like Ghostty's quick terminal. It stays out of the taskbar
and Alt+Tab, and it keeps its tabs while hidden. Global settings:

- `quick-terminal-hotkey`: e.g. `win+grave`, `ctrl+alt+t` (needs a modifier).
- `quick-terminal-position`: `top` (default), `bottom`, `left`, `right`, `center`.
- `quick-terminal-screen`: `mouse` (default; the monitor under the pointer) or `main`.
- `quick-terminal-size`: `primary[,secondary]`, each `N%` or `Npx`. Primary is the height for
  top/bottom and the width for left/right/center; secondary is the other side. Defaults:
  50% for the edges, 70%,60% for center.
- `quick-terminal-autohide`: `true` (default) hides it when it loses focus.

If another app holds the hotkey (Windows Terminal's quake mode uses ``Win+` ``), windows
show a notice, and Phosphor retries whenever one of its windows is activated.

### Tray icon and background

Phosphor shows a notification-area icon while it runs. Clicking it brings back the most
recently used window (or opens one); right-clicking offers New window, Show/Hide quick
terminal, and Quit Phosphor, which closes every window, the hidden quick terminal included.
Quit Phosphor is also in the command palette.

When the last window closes, Phosphor keeps running in the tray while its quick terminal hotkey
works (or the quick terminal is showing), so the hotkey still summons it; otherwise it exits.
`quit-after-last-window-closed = true` always exits, `false` always stays in the tray.

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
| `Ctrl+Shift+P` | Command palette: every action, profile and tab, fuzzy-searchable |
| `Ctrl+Shift+F` | Find in the pane's scrollback |
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

- `tab-groups = false` (global) keeps one terminal per pane, like Windows Terminal: new tabs
  open as workspaces.
- In a split workspace the focused pane is marked by an accent line along its top (on the shown
  tab when the pane has a strip), and the others are dimmed. `unfocused-split-opacity` (global,
  0.15–1, default 0.7 as in Ghostty; 1 = no dimming) sets how much, and `unfocused-split-fill`
  what they fade toward (default: each pane's background).
- `group-tab-strip = always` (global) shows the strip on every pane, even with one tab
  (default `auto`: only with two or more).
- `workspace-tabs = left` (global) lists workspaces in a column on the left, under a plain
  title bar, instead of as title-bar tabs (default `top`). The list works like the tabs: click,
  close, right-click, drag to reorder or anywhere a title-bar tab can go.

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
