# Phantom

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
- **ConPTY** runs the child process. Phantom ships Windows Terminal's
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
dotnet run --project src/Phantom

# 3. Run the tests (ABI check against libghostty's manifest, headless ConPTY sessions)
dotnet test
```

The app is unpackaged and Windows App SDK self-contained, so
`src/Phantom/bin/x64/<Config>/net10.0-windows10.0.26100.0/win-x64/Phantom.exe`
runs directly.

## Installing locally

```bash
pwsh scripts/install.ps1                  # Release build -> Start Menu "Phantom" (Raycast finds it)
pwsh scripts/install.ps1 -InstallGitHook  # also reinstall in the background after each commit
pwsh scripts/install.ps1 -Uninstall       # remove install, shortcut and hook (keeps config)
```

Each install publishes to a new folder under `%LOCALAPPDATA%\Programs\Phantom\versions\`
and repoints the Start Menu shortcut, so open windows are never disturbed; the three
newest versions are kept (`-Keep N`). Requires .NET 10 (the runtime isn't bundled).

## Configuration

`%APPDATA%\Phantom\config` (override the path with `PHANTOM_CONFIG`), in Ghostty's
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
  installed, Phantom falls back to Cascadia Mono, then Consolas.
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

- `default-profile` (global only): the profile Phantom starts with; defaults to the first.
- `icon`: image shown for the profile in tabs. Without one, Phantom picks one like Windows
  Terminal: the WSL distro's registered icon, Git for Windows' icon for Git Bash, a network
  glyph for `ssh`, else the command's executable icon.
- `working-directory`: `~`/`home` (the default) or a path; `~/sub` and `%VARS%` expand.
- Without any sections, the global settings act as a single "Default" profile.
- Problems (unknown keys, bad values, duplicate profiles) are collected as warnings
  instead of stopping Phantom.

This section syntax is an interim step; a Phantom-specific config format is planned.

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
show a notice, and Phantom retries whenever one of its windows is activated. A hidden quick
terminal keeps Phantom running after its other windows close.

## Choosing a shell

1. Arguments after the executable: `Phantom.exe "C:\Program Files\Git\bin\bash.exe" --login -i`
2. The `PHANTOM_SHELL` environment variable (a full command line)
3. The default profile's `command` (tabs opened later use their profile's)
4. Otherwise `pwsh.exe`, then `powershell.exe`, then `cmd.exe`

Children get `COLORTERM=truecolor` and `TERM_PROGRAM=phantom`.

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
| `Ctrl+Shift+T` / `Ctrl+Shift+1..9` | New tab (default profile / profile N) |
| `Ctrl+Shift+Space`, ▾ button | Profile menu (Alt+click: open as a split; Shift+click: new window) |
| `Ctrl+Shift+D` | Duplicate tab |
| `Ctrl+Shift+W` | Close pane (the tab, if it's the last one) |
| `Alt+Shift+Plus` / `Alt+Shift+Minus` | Split right / down (default profile) |
| `Alt+Shift+D` | Duplicate pane (splits along the longer side) |
| `Alt+Arrow` | Focus the pane in that direction |
| `Alt+Shift+Arrow` | Move the nearest divider (or drag it) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Next / previous tab |
| `Ctrl+Alt+1..8`, `Ctrl+Alt+9` | Go to tab N / last tab |
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

Each pane has a scrollbar (thin until the pointer nears the right edge) while there is scrollback; it hides for
full-screen apps, whose alternate screen has none. The selected tab takes the focused
pane's background color unless you give the tab a color.

When an app turns on mouse reporting (vim, htop, tmux, flow), clicks, drags and
the wheel go to the app, and the pointer becomes an arrow instead of an I-beam. On the alternate screen with alternate-scroll mode, the
wheel sends arrow keys (`less`, `man`).

Right-clicking a tab offers rename, tab color, duplicate, split, export text (whole
scrollback to a file), and closing this / other / right-hand tabs.

If the shell exits with a non-zero code, the window stays open showing the code
until a key is pressed; a clean exit closes it.

## Layout

```
scripts/build-libghostty.sh       fetch + build libghostty-vt at a pinned commit
scripts/gen-keys.py               regenerate GhosttyKey.g.cs from the C header
scripts/gen-icon.py               assets/*.svg -> src/Phantom/Assets/Phantom.ico (pixel-snapped sizes)
assets/                           source artwork (app icon SVG)
src/Phantom.Native.targets        ghostty-vt.dll + bundled ConPTY for runnable projects
src/Phantom.Core/                 everything testable without a window:
  Ghostty/                          P/Invoke bindings
  Pty/ConPty.cs                     pseudoconsole + child process + reader thread
  Terminal/TerminalSession.cs       terminal + pty glue, libghostty callbacks
  Input/                            scan code -> GhosttyKey map, keyboard layout translation
  Config.cs, ShellResolver.cs       config file, command resolution
src/Phantom/                      WinUI app: window, TerminalControl (Win2D render + input)
tests/Phantom.Tests/              xUnit v3: ABI check, headless sessions, unit tests
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
- Kitty graphics and desktop notifications are parsed by libghostty but not surfaced yet.
- Links and URLs are detected within one row; a URL wrapped onto the next line is cut there.
