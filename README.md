# Zinc

*Every cell, exactly right.* A precise, native terminal for Windows, built on Ghostty's core.
Brand guidelines: [docs/brand.md](docs/brand.md).

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
- **ConPTY** runs the child process. Zinc ships Windows Terminal's
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
  (used by Zig to link the DLL; its redistributable Visual C++ runtime, `vcruntime140.dll`, ships
  beside the DLLs, since a fresh Windows doesn't have it)
- [Zig](https://ziglang.org) **0.16.x** on `PATH` (Ghostty's minimum)
- Git Bash (or any POSIX shell) for the build script

## Build and run

```bash
# 1. Build libghostty-vt at the pinned Ghostty commit -> native/x64/ghostty-vt.dll
scripts/build-libghostty.sh x64

# 2. Build and run the app
dotnet run --project src/Zinc

# 3. Run the tests (ABI check against libghostty's manifest, headless ConPTY sessions)
dotnet test
```

The app is unpackaged and Windows App SDK self-contained, so
`src/Zinc/bin/x64/<Config>/net10.0-windows10.0.26100.0/win-x64/zinc.exe`
runs directly.

## Installing locally

```bash
pwsh scripts/install.ps1                  # Release build -> Start Menu "Zinc" (Raycast finds it)
pwsh scripts/install.ps1 -InstallGitHook  # also reinstall in the background after each commit
pwsh scripts/install.ps1 -Uninstall       # remove install, shortcut and hook (keeps config)
```

Each install publishes to a new folder under `%LOCALAPPDATA%\Programs\Zinc\versions\`
and repoints the Start Menu shortcut, so open windows are never disturbed; the three
newest versions are kept (`-Keep N`). Each carries its own .NET runtime, as the tester zip does.

**Sharing a build.** `pwsh scripts/release.ps1 -UpdateUrl <address>` makes a release in
`dist\release` with [Velopack](https://velopack.io): `Zinc-win-Setup.exe` (a per-user install, no admin
prompt), the update packages, `releases.win.json` (what installed copies check daily) and
`Zinc-win-Portable.zip`. The .NET runtime and the Windows App SDK are inside (about 77 MB). Put the
folder's contents at the address; installed copies then update themselves (Settings > About > Updates).
`-From <address>` fetches the previous release first so the new one also gets a small delta package.
`README.txt` (from `scripts/package-readme.txt`) tells testers how to install past SmartScreen (the
build isn't signed), how updates work, where settings and logs go, how to send a debug log and
benchmark results with feedback, and how to remove it.

**Versions.** `ZincVersion` in `Directory.Build.props` is the release being worked toward. A build
of the commit tagged `v<that version>` is that release (`0.1.0+a1b2c3d`); every other build is a dev
build numbered by the repository's commit count (`0.1.0-dev.812+a1b2c3d`). It's shown in Settings >
About, answered to programs asking the terminal's version (XTVERSION: `zinc 0.1.0-dev.812+a1b2c3d`),
in the exe's file properties, and in install folder names (after a timestamp, which keeps them in order).

## Configuration

The first time Zinc starts, a welcome page asks how you like to work (**Simple**: tabs along
the top, one terminal each; **Workspaces**: in a sidebar or along the top, each with its own tabs
and splits), a color scheme and font (with a preview; or two schemes that follow Windows' light
and dark mode), your default shell, and whether a long command finishing in the background shows
a Windows notification, and writes your answers to `config.toml`. Reopen it from the command palette (**Welcome…**): it changes only what
you change there and leaves the rest of the file as it is.

**Settings** (`Ctrl+,`, the command palette, the ▾ menu or the tray menu) edits the settings below in a window
of its own, next to your terminals: each change is written to `config.toml` right away and applied
as you make it (only that line changes; choosing a default removes the line), and **Undo changes**
(at the bottom while there are changes) puts the file back as it was when you opened the window.
Each setting shows its key in the file; the arrow beside a changed setting's name resets it.
Color schemes are shown in their own colors (type to filter the list), and a color setting offers
the scheme's 16 colors, "use the scheme's", or any color. **Find a setting** searches settings,
key bindings and a profile's own settings ("font size ubuntu"), each under where it lives; "Show
in …" (or Enter, for the first result) goes there and highlights it. **Appearance** and each
profile's page keep a small preview terminal at the top, showing that page's font, colors and
padding as you change them (hidden when the window is too short for it; **Hide preview** beside
the heading puts it away, remembered). Moving through a color scheme list (pointer or arrow keys)
shows each scheme in it before you pick one. Settings that depend on
another fold under it, and a profile's groups fold into one line each that says what the profile
changes.
While `config.toml` has an error, Settings says so and changes nothing. The window reopens where
you left it. Under **Profiles**: the default profile, every profile in your order (drag them, or
**⋯ → Move up/down**; the new-tab menu follows it), found on this PC or your own (add, duplicate,
delete, or undo your changes to a found one), each one's name, command, folder, icon (with a
folder button to browse for them) and visibility, and its
own font, colors and padding: each shows what it inherits until you change it there, and the
reset arrow makes it inherit again ("Inherited from Appearance" links to where it's set). Also which
shells Zinc looks for, and what each search found. A found profile you changed that can't open
any more (its shell is gone, or its search is turned off) stays in the list, saying why, with
**Delete** or **Turn on** the search; it's left out of the menus meanwhile. Under **Keyboard**: every action with its keys, grouped (Clipboard, Tabs,
Panes…) and filterable by name or key; an action that takes an argument (split: a direction; new
tab: a profile; go to workspace: a number) lists its variants that have keys, and **Add a key for
another …** binds any other; click a key to take it away, **+** to add one
by pressing it (it says if another action has that key, and takes it over), the reset arrow for
the defaults. Changes go in `[keys]`: taking away a default key writes `"chord" = "none"`. Key
sequences are listed but added in the file.

`%APPDATA%\Zinc\config.toml` (override the path with `ZINC_CONFIG`), in
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
  ships with Zinc and needs no installing. A family that isn't installed falls back to it,
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
- `theme`: a color scheme by name. About 650 come with Zinc (the
  [iTerm2-Color-Schemes](https://github.com/mbadolato/iTerm2-Color-Schemes) collection);
  your own go in `%APPDATA%\Zinc\themes\`, one file per scheme in Ghostty's theme format
  (`background = #181818`, `palette = 1=#AC4242`, ...), named as `theme` refers to them. Yours
  win over bundled ones with the same name.
  **Customize** (Settings > Appearance, "Edit colors") edits the scheme in force: its background, text,
  cursor, selection and 16 palette colors, each with a color picker. A bundled scheme is copied into
  your `themes\` folder first and the copy is used instead of it everywhere ("Back to the original"
  deletes the copy); your own are edited in place. Every pane using the scheme changes as you edit,
  and "Undo changes" puts the file back as it was when you opened it. **New scheme…** (there, and
  "Save as a new scheme…" in the editor) starts a scheme of your own as a copy, under a name you give;
  "Delete scheme" removes one of yours (where it was chosen, Zinc's own colors take its place).
  Without a theme, Zinc's own colors: "Lit Cell", phosphor green on near-black (it and its
  light partner come with Zinc by name: `theme = "light:Lit Cell Light,dark:Lit Cell"` follows
  Windows' mode). A scheme
  replaces them entirely: what its file leaves out (some palette entries, selection colors) is
  libghostty's default, not Lit Cell's.
  **Light and dark**: `theme = "light:Catppuccin Latte,dark:Catppuccin Mocha"` (Ghostty's syntax)
  follows Windows' app mode (Settings > Personalization > Colors), switching as it changes.
  Programs that ask whether the terminal is light or dark (`CSI ? 996 n`) are told the side in
  force (with a single scheme: whether its background is dark), and those that subscribe (mode
  2031, e.g. neovim) hear of each change, so they can switch their own colors too.
- `minimum-contrast`: `1` (default: off) to `21`: text that reads worse than this against its
  background (the WCAG contrast ratio; `4.5` is the usual accessibility level) turns black or white.
- `bold`: `"#rrggbb"` for bold text in the default text color; `bold-is-bright`: `true` shows bold
  text in the first eight colors in their bright versions (as older terminals did).
- `faint-opacity`: how much of faint (dim) text's color shows, `0.1` to `1` (default `0.5`).
- `cursor-opacity`: `0.1` to `1` (default): below 1 a block cursor lets its character show.
- `background-opacity`: `0` to `1` (default): below 1 the terminal's background is see-through and
  the window glass shows through it (cells with a background color of their own stay solid). The glass
  is `[window] backdrop`: `"mica"` (default: solid, your wallpaper's tint), `"acrylic"` (frosted: the
  windows behind, blurred) or `"none"` (clear: the windows behind, as they are), tinted with
  `[window] backdrop-color` (`"#rrggbb"`; default: the terminal's background) by `[window] backdrop-tint`
  (`0` to `1`, default `0.3`; lower is more see-through; Mica isn't tinted). Settings > Appearance >
  Background calls them Window glass, Glass color, Glass tint and Terminal background opacity.
- `find-highlight`, `find-highlight-current`: `"#rrggbb"` behind Find's matches and the current one
  (default: see-through yellow and orange).
- `background`, `foreground`, `cursor`: `"#rrggbb"`.
- `cursor-text`: `"#rrggbb"`, the character under a block cursor, instead of the scheme's (without
  either, the background color). It stays when a program recolors the cursor.
- `selection-background`, `selection-foreground`: `"#rrggbb"`, instead of the scheme's (most
  schemes set them). With neither, selection inverts the colors; with only a background, selected
  text keeps its color.
- `palette`: an array of up to 256 colors from index 0, or single entries: `palette.1 = "#AC4242"`.

**`[terminal]`**
- `padding-x`, `padding-y`: points; one number, or `[left, right]` / `[top, bottom]`.
- `padding-balance`: `false` (default) or `true`. Text fills whole cells, so up to a cell of room is
  left over; it stays on the right and bottom, or with `true` is split between both sides (Ghostty's
  `window-padding-balance`).
- `mouse-pointer`: `"arrow"` (default, like Windows Terminal) or `"text"` (an I-beam over text,
  like Ghostty). It's always an arrow over the scrollbar and while an app uses the mouse.
- `copy-on-select`: `false` (default) or `true`: selecting text with the mouse copies it (the
  selection stays).
- `click-to-move`: `true` (default) or `false`: at a shell prompt, clicking in the command you're
  typing moves the cursor there (Ghostty's `cursor-click-to-move`). It needs the prompt marks of
  [shell integration](#shell-integration): Zinc presses the arrow keys for you, or, for shells
  that ask for it (fish 4.1+, nushell), sends the click to the shell. A click on the prompt goes to
  the start of the command, one past its end to the end. Only within the command's own line.
- `paste-warning`: when to ask before a paste of more than one line. `"auto"` (default) asks when
  each line would run as a command: the program hasn't turned on bracketed paste, so line breaks
  arrive as Enter (cmd, old shells, a plain `cat`). `"multiline"` asks for every multi-line paste,
  `"never"` never asks. The question shows the paste's first lines.
- `large-paste-warning`: `true` (default) asks before pasting more than 5 KB.
- `word-boundaries`: the characters that end a word when double-clicking, besides spaces and tabs.
  Unset, Ghostty's: ``'"│`|:;,()[]{}<>$``; e.g. add `/` to select one part of a path.
- `clipboard-write`: `true` (default) or `false`, for programs copying with OSC 52 (tmux,
  neovim, remote shells over ssh).
- `clipboard-read`: for programs reading the clipboard with OSC 52 (tmux, neovim, remote shells).
  `"ask"` (default) asks each time, showing what's on the clipboard: allow once, allow in this tab,
  or deny (the program waits for the answer); `"allow"` (or `true`), `"deny"` (or `false`).
  Anything running in the terminal, including a remote host, could read it.
- `command-marks`: `"failures"` (default), `"all"` or `"none"`: which commands get a bar beside
  them ([shell integration](#shell-integration)).
- `shell-integration`: `"auto"` (default) loads Zinc's [shell integration](#shell-integration)
  into PowerShell, bash and zsh; `"off"` starts shells as they are.
- `cursor-style-at-prompt`: `"same"` (default) or a shape as for `cursor-style`: the cursor while
  a shell waits at its prompt ([shell integration](#shell-integration) says when), e.g. `"bar"`, as
  in a text box (Ghostty's default). A shell or program that sets its own shape still does.
- `cursor-style`: `"block"` (default), `"bar"`, `"underline"` or `"block-hollow"`, and
  `cursor-blink`: `false` (default) or `true`: the cursor until a program picks its own (DECSCUSR)
  and after it resets it.
- `scrollback-lines`: how many lines you can scroll back through, `10000` by default (0 to
  100,000; libghostty prunes a page at a time, so it keeps about that many).
- `close-on-exit`: `"success"` (default) closes a tab whose program ended cleanly and keeps a
  failure on screen; `"always"` or `"never"` (any key then closes it).
- `bell`: `"attention"` (default) puts a dot on the tab and flashes the taskbar button when you
  aren't looking at it; `"flash"` also flashes the pane; `"none"`. `bell-sound = true` plays
  Windows' default sound too. A burst of bells (a held key the prompt refuses) rings once; the
  bell rings again after 0.3 s without one. `screen-flash-bell`: `true` (default) turns a program's
  own screen flash (reverse video on and straight off, readline's `bell-style visible`, which Git
  Bash sets) into this bell; reverse video a program keeps on shows a quarter second late.
- `input-method`: `false` (default, while it's new) or `true`: Windows' input methods (IME: Japanese, Chinese, Korean…) type
  into the terminal. What you're composing shows at the cursor, in the terminal's font, with the
  candidate window beside it; the committed text goes to the program. `false` sends keys straight
  to the terminal.
- `program-titles`: `true` (default) lets shells and programs set the tab's title; `false` keeps the
  profile's name (a name you give a tab always wins).
- `shell-titles`: `true` (default) names the tab from the shell's marks (shell integration, ours or the
  shell's own): the folder at the prompt (`~` for your home), the command while one runs. A title the
  running program sets itself (vim, htop) still wins until it ends. Shells without marks keep their own titles.

**`[shaders]`**: custom shaders over the terminal's picture, Ghostty's way (its `custom-shader`
and `custom-shader-animation`): GLSL fragment shaders in [Shadertoy](https://www.shadertoy.com)'s form, so
shaders written for Ghostty work unchanged
(CRT screens, bloom, cursor trails...).
- `files`: one path or a list, run in order, each over the previous one's picture; relative paths are from
  the config file's folder, `~` is your home. A shader that doesn't compile is left out (the others still
  run) and a notice says why, with its line number. Saving a shader file applies it at once (the notice
  goes when it compiles).
- `animation`: `true` (default) lets shaders that use time (`iTime`, `iFrame`...) redraw on their own
  while the terminal has focus. Unfocused, their clock pauses (they pick up where they stopped; unlike
  Ghostty, where it keeps running): the terminal's text still updates as output arrives, under a still effect;
  `"always"`, focused or not; `false`, never: they hold still (cursor effects included) and are drawn only
  when the terminal changes (unlike Ghostty's `false`, where each such frame shows them further along).
  Shaders that don't use time draw only when the terminal changes anyway.
- Settings > Appearance > Shaders has both: add files, reorder or remove them, open one in your editor,
  and see whether each compiles (and why not); the preview at the top runs them. A profile's page has
  the same, inherited until you change it there.
- Profiles can have their own (`[profile.x.shaders]`, `files = []` for none). Shaders need
  `low-latency-drawing` (the default). What a shader gets: all of Ghostty's uniforms: the picture so far
  (`iChannel0`), size, time, frame, date, focus, the cursor (where and how it's drawn, the previous one and
  when it moved: cursor trails), the live palette and colors, and `iMouse` (Shadertoy's: while the left button
  is down). Positions are in pixels with y going down, as in Ghostty on macOS.

**`[window]`**: `workspace-tabs`, `sidebar`, `title-bar`, `tab-groups`, `group-tab-strip`, `unfocused-split-opacity`,
`unfocused-split-fill`, `save-state` and `quit-after-last-window-closed`, described with the
features they control below; `focus-follows-mouse = true`, which focuses a pane when the pointer
enters it (only while focus is in another pane, not in the palette or a find box); and `software-rendering = true`, which draws with the processor
instead of the graphics card (for broken graphics drivers, some virtual machines and remote desktop
glitches; slower; applies to terminals opened after it's set). `low-latency-drawing = true`
(the default) has each terminal present its frames from a swap chain of its own as soon as they're
drawn, instead of at the window's next render tick, so typing shows up about a refresh sooner; `false`
goes back to the previous drawing, as a fallback (terminals opened after it's set). `free-hidden-memory = true`
(the default) has a tab left in the background give back the graphics memory it draws with (two full-size
buffers, more with shaders: tens of MB for a maximized tab on a high-resolution screen) once it has been hidden for
`free-hidden-after` (seconds, or `"2m"`; default 60), and make it anew when you come back; `false` keeps it.

**`[notify]`**: `command-finished`, `command-finished-after` (see
[Shell integration](#shell-integration)), and `programs`: notifications that programs send
(OSC 9 and OSC 777, e.g. a build script's `printf '\e]9;Build done\a'`). Out of view they put a
blue dot on the tab, like a finished command; `"toast"` also shows a Windows notification while
Zinc is in the background (`"toast-always"`: whenever that pane is out of view), `"never"`
ignores them. Programs that report progress (OSC 9;4)
get a small ring on their tab and a progress bar on Zinc's taskbar button.
Windows notifications come from Zinc's tray icon and respect Do Not Disturb.

**Dot colors** (`[notify] finished-color`, `bell-color`, `failed-color`, `"#rrggbb"`): the tab dots for a
finished command or a program's notification, the bell, and a failed command (default: Zinc's green, amber
and red, darker in light mode).

**Light or dark** (`[window] appearance`): `"system"` (default: as Windows' app mode is when Zinc starts),
`"light"` or `"dark"`, for windows, menus and dialogs. The terminal keeps its color scheme (`theme = "light:X,dark:Y"`
follows Windows live). Applies when Zinc starts again: WinUI sets an app's theme once.

**Accent** (`[window] accent`): `"zinc"` (default: Zinc's green on selections, focus lines,
switches and buttons) or `"windows"` (your Windows accent color). Applies when Zinc starts.

**`[updates]`**: `check = true` (default) lets a Zinc installed with its installer look for a new
version once a day and download it in the background; a notice then offers to restart into it, or it
installs the next time you quit. `false`: only Settings > About > Check for updates looks. Copies not
installed by the installer (a dev build, `scripts/install.ps1`'s) never update.

**`[cli]`**: `remote-control = true` lets `zn` (below) control this Zinc; off by default, when only
`zn version` answers. `path = true` puts `zn` on your user PATH for other terminals. Settings > Terminal >
Command line has both.

**`[debug]`**: `logging = true` keeps a debug log in `%APPDATA%\Zinc\logs\zinc.log`
(moved aside to `zinc.old.log` at 4 MB). Settings > About has the switch and Open folder, Copy
(the log with Zinc's version and Windows' on top, for a bug report) and Clear. `ZINC_LOG=<file>`
overrides it: always on, into that file. About also has a drawing benchmark: four 5-second tests
in a window of their own (optionally again with software rendering), with results you can copy.

**`include`** (at the top): other files to read first, e.g. `include = ["profiles/*.toml"]`,
relative to this one. Settings in the including file win.

### Profiles

Each `[profile.<id>]` section is a profile, starting from the settings above and overriding
`[font]`, `[colors]`, `[terminal]` and `[shaders]` for itself (as subsections, or dotted keys like
`font.size = 12`). Profiles appear in the new-tab menu with yours first (in file order), then the
ones found on this PC in the order they're found; `profile-order` (or dragging them in Settings)
sets any order you like. The id is what
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

- `default-profile` (at the top): the profile Zinc starts with; defaults to the first.
- `profile-order` (at the top): profile ids in the order menus list them (Settings writes it when
  you reorder); profiles not in it follow in their usual order.
- `name`: shown in menus and tabs; defaults to the id.
- `command`: the command line; without one, PowerShell 7, else Windows PowerShell, else cmd.
- `icon`: image shown for the profile in tabs. Without one, Zinc picks one like Windows
  Terminal: the WSL distro's registered icon, Git for Windows' icon for Git Bash, a network
  glyph for `ssh`, else the command's executable icon.
- `working-directory`: `"~"` (the default) or a path; `~/sub` and `%VARS%` expand.
- `hidden = true`: keeps a profile out of menus.
- Without any profiles, of your own or found, the settings above act as a single "Default" profile.

Zinc also finds the shells installed on this machine every time it reads the config, as
Windows Terminal does, and lists them after your own profiles:

| Found | Id | Name |
| --- | --- | --- |
| WSL distros (the default one first; Docker Desktop's are skipped) | `wsl:<distro>` | the distro's name |
| PowerShell 7, Windows PowerShell, cmd | `pwsh`, `powershell`, `cmd` | PowerShell, Windows PowerShell, Command Prompt |
| Git for Windows | `git-bash` | Git Bash |
| MSYS2 (its UCRT64 environment) | `msys2:ucrt64` | MSYS2 UCRT64 |
| Visual Studio installs | `vs:<instance>:pwsh`, `vs:<instance>:cmd` | Developer PowerShell / Command Prompt for … |
| Hosts in `~/.ssh/config` (not patterns like `*`; `Include`s are followed) | `ssh:<host>` | SSH - &lt;host&gt; |

A `[profile]` section with a found profile's id changes it, starting from what was found, without
moving it (use `profile-order` for that); `hidden = true` hides it. A section for a found profile that
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
show a notice, and Zinc retries whenever one of its windows is activated.

### Tray icon and background

Only one Zinc runs at a time: launching it again (Start menu, a launcher, the command line)
opens a new window in the running one — with the command given, if any — or, when it's in the
tray with no window open, reopens the saved windows.

Zinc shows a notification-area icon while it runs. Clicking it brings back the most
recently used window (or the saved windows); right-clicking offers New window, Show/Hide quick
terminal, Settings, and Quit Zinc, which closes every window, the hidden quick terminal
included. Quit Zinc is also in the command palette, next to Quit and start fresh next time,
which quits without keeping the windows (the next start opens a new one).

`launch-at-login` in `[window]` starts Zinc when you sign in to Windows: `"tray"` without a
window (the quick terminal's hotkey works; the tray icon opens your windows), `"windows"` with
your windows as you left them, `"off"` (default). Zinc keeps a "Zinc" entry in your
startup apps (the Run key) in step with it, pointing at the installed version; switching it off
in Windows' Startup apps page works too (Zinc leaves that switch alone).

When the last window closes, Zinc keeps running in the tray while its quick terminal hotkey
works (or the quick terminal is showing), so the hotkey still summons it; otherwise it exits.
In `[window]`, `quit-after-last-window-closed = true` always exits, `false` always stays in the tray.

### Default terminal

Zinc can open the console programs Windows starts outside a terminal: a script you
double-click, a command from Run, an installer, a tool that opens a console. Turn on **Default
terminal app** in Settings > Layout; each such program then opens in a Zinc tab (in the window
you used last, or a window of its own if none is open), running as it would anywhere. Turning it off
gives the setting back to what it was.

For now this needs **Windows Terminal** (any edition) installed and chosen as the default terminal
app in Windows Settings first: Windows hands each new console to a console host and then to a
terminal, and Zinc takes the terminal's part while Windows Terminal's console host keeps its own.
No package or installer change is involved (Zinc registers itself for your user only; the
installer keeps that pointing at the newest version and gives the setting back on uninstall). Its
tabs aren't saved with the session: such a program can't be started again as it was.

### Reopening where you left off

Zinc remembers its windows: their size and place, their workspaces (titles, colors), split
layouts and tabs, and which were shown. Next time it starts (without a command on its command
line), they reopen with fresh shells in the same folders. What the terminals showed isn't kept.
The state is saved as you go (`%APPDATA%\Zinc\session.json`), so a crash keeps it too;
closing the last window keeps that window for next time. `save-state = "never"` in `[window]` turns this
off (`"always"`, the default, keeps it on).

A window you close can come back too: *Reopen closed window* (in the command palette and the
tray menu, `reopen-window` in `[keys]`) reopens the one closed last, where it was, with its
workspaces, splits and tabs. The last five are kept, with the session.

A tab reopens in the folder its shell last *reported* (OSC 7, or OSC 9;9 / OSC 1337).
PowerShell, bash and zsh report it through Zinc's [shell integration](#shell-integration),
with nothing to set up; other shells need to send it themselves, e.g. from their prompt:
`printf '\e]7;file://%s%s\e\\' "$HOSTNAME" "$PWD"`. Without it, tabs reopen where they first
started. WSL tabs reopen through `wsl.exe --cd`, Git Bash paths (`/c/...`) map to their drive.

### Shell integration

A shell can mark its prompts and commands (OSC 133): where each prompt starts, where a command's
output starts, and how it ended. With those marks, Zinc can:

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
  workspace with several tabs to look at shows how many. The taskbar button flashes if Zinc
  is in the background. In
  `[notify]`: `command-finished = "toast"` shows a Windows notification instead of the flash
  (click it to go to the pane), `"toast-always"` shows one even while you're in Zinc (just
  not looking at that pane), `"never"` turns this off, and `command-finished-after` sets what
  counts as long (`30`, `"45s"`, `"2m"`);
- **ask before closing a running command**: closing a tab, pane, workspace or window, or
  quitting, asks first while a command is running there (never at an idle prompt).

The marks are invisible, and **PowerShell (7 and Windows PowerShell), bash and zsh get them
automatically**, in WSL, Git Bash and MSYS2 too, along with folder reports for
[reopening tabs](#reopening-where-you-left-off). Your startup files run as usual; Zinc's
script (in `ShellIntegration\` next to zinc.exe) runs after them and changes nothing you see:

- **PowerShell** is started with `-NoExit -Command` running it after your profile. A profile
  started with `-Command` or `-File` of its own is left alone.
- **bash** finds it in `PROMPT_COMMAND`, which runs it once at the first prompt (then puts
  `PROMPT_COMMAND` back as your startup files left it).
- **zsh** finds it through `ZDOTDIR`: Zinc's `.zshenv` puts your `ZDOTDIR` back, loads your
  `.zshenv` as zsh would have, and sets the rest up at the first prompt.
- **WSL** gets the same variables through `WSLENV`, so it works with whichever of these is your
  login shell.

Marks your prompt already sends are left to it: **powerlevel10k's** own integration
(`typeset -g POWERLEVEL9K_TERM_SHELL_INTEGRATION=true` in `p10k.zsh`; it knows its own prompt best) or a
hand-written one, and a folder report you send yourself. Zinc then adds only what's missing
(powerlevel10k sends no folder report).

`shell-integration = "off"` in `[terminal]` (or in one profile) starts shells exactly as anywhere
else. If your startup files hand over to something else (tmux, or `exec` another shell), the
marks don't follow; the scripts in `ShellIntegration\` show what to send from there.

**A shell started from another** (`fish` from zsh, `wsl` from PowerShell) is followed too: the
command that started it runs until you leave it, and the commands you run inside are its own. Two
shells are told apart by how their prompts mark themselves (Zinc's scripts add their process
id); two that mark alike, like zsh inside zsh with powerlevel10k, look like one shell, and the
command that started the inner one ends at its first prompt.

**cmd** can mark prompts (for jumping), but not commands or exit codes:
`setx PROMPT "$e]133;D$e\$e]133;A$e\$P$G$e]133;B$e\"`.

**fish** (recent versions: 4.3 checked) and **nushell** send the marks and folder reports on their own, so
Zinc loads nothing into them: jumping, selecting output and the command list work as they
are. For other shells, the scripts in `ShellIntegration\` show what to send.
[docs/shell-integration.md](docs/shell-integration.md) compares all this with Ghostty's.

## Choosing a shell

1. Arguments after the executable: `zinc.exe "C:\Program Files\Git\bin\bash.exe" --login -i`
2. The `ZINC_SHELL` environment variable (a full command line)
3. The default profile's `command` (tabs opened later use their profile's)
4. Otherwise `pwsh.exe`, then `powershell.exe`, then `cmd.exe`

Children get `COLORTERM=truecolor` and `TERM_PROGRAM=zinc`.

## Keys and mouse

| Input | Action |
| --- | --- |
| `Ctrl+Shift+C`, or `Ctrl+C` with a selection | Copy selection |
| `Ctrl+V`, `Ctrl+Shift+V`, `Shift+Insert` | Paste (bracketed when the app enables it) |
| Right click | Copy the selection if there is one, otherwise paste |
| Drag / double-click / triple-click | Select cells / word / line |
| Drop files on a pane | Types their paths at the prompt as its shell writes them (WSL: `/mnt/c/…`, or where its `/etc/wsl.conf` mounts drives, and its own files as `/home/…`; Git Bash and MSYS2: `/c/…`), quoted where needed |
| Mouse wheel, `Shift+PgUp/PgDn` | Scroll back (`Ctrl+Shift+Home/End` jump to top/bottom) |
| `Ctrl+=`, `Ctrl+-`, `Ctrl+0`, `Ctrl`+wheel | Zoom in / out / reset (this pane, in steps of 10% of the configured font size; the wheel zooms even when a program uses the mouse). A badge shows the zoom and the size in points |
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
| `zoom-in`, `zoom-out`, `reset-zoom` | Zoom this pane in or out by 10% of the configured font size, or back to it. |
| `scroll-page-up`, `scroll-page-down`, `scroll-to-top`, `scroll-to-bottom` | Scroll the scrollback. |
| `new-tab` | A new tab (in the focused pane, with tab groups). `new-tab <profile>`: that profile, by id or menu position. |
| `new-workspace` | A new workspace with the default profile. |
| `new-window` | A new window with the default profile. |
| `reopen-workspace` | Bring back the workspace closed last: its tabs, splits and folders. |
| `reopen-window` | Bring back the window closed last, where it was: its workspaces, tabs, splits and folders. |
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
| `quit-fresh` | Close every window and exit without saving them: next time opens a new window. |

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
support with the protocol's query; Zinc answers it (and identifies as libghostty).

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

A **new workspace** (+, `Ctrl+Alt+T`, the sidebar's New workspace, the palette), and a new window
(New window, `Ctrl+Shift+N`, the tray's New window), opens on a page for
choosing how it starts: a profile, a folder (ones your shells were in lately, a typed path, or
Browse…, which for a WSL profile opens in that distro), a layout (one pane, side by side, stacked,
one big and two small, or a grid of four: every pane runs the profile in the folder), a name, and
an icon and a color for the workspace (buttons by the name).
Under **Reopen**, it lists the workspaces you closed lately (their tabs, splits, folders, name and
color, as a restart would bring them back): a click reopens one at once. "Reopen closed workspace"
in the palette (`reopen-workspace`, no key by default) brings back the last one. Typing filters both lists, and a typed path is used as is. Enter starts it; untouched,
Enter starts the default profile in its usual folder, as before. Esc leaves the page (in a window's
only tab, it starts the default). While it's shown, the window's own shortcuts work (switching tabs,
closing it, new windows, settings…), and so does the command palette, with the window's commands
and *New workspace: …* for each profile, which starts it in the page's place; with the sidebar, it has an entry at the end of the list. A workspace started in a folder is named after it (or what you
type) and keeps it: its new tabs and splits start there, in each shell's own terms (a WSL folder
opens in PowerShell as `\\wsl.localhost\…`, a Windows one in WSL under `/mnt/c/…`); shells can still
go anywhere, and Duplicate and "New tab in folder…" keep their own folder. "Show this page for new
workspaces" at the bottom (`new-workspace-page` in `[window]`) turns the page off: + then starts the
default at once. The ▾ menu and Zinc's own start (with nothing to reopen) start at once, as before.

A workspace's **icon** (a set of Windows' own icons: code, folder, globe, bug, cloud…) shows on its tab
and in the sidebar instead of its active tab's profile icon; set it on the page, from the workspace's
menu (**Workspace icon…**) or the palette (which previews each one). **Workspace folder** in the same
menu (and the palette's **Set workspace folder…** / **Clear workspace folder**) sets or clears its
folder after the fact: typed like "New tab in folder…", and the workspace takes the folder's name
unless you named it yourself. `workspace-colors` in `[window]` colors new workspaces you don't color
yourself: `"off"` (default), `"folder"` (a workspace started in a folder gets that folder's color, the
same one every time, and setting or clearing a folder later changes it too, unless you chose the
color yourself) or `"cycle"` (each new workspace takes the next color).

- `tab-groups = false` (in `[window]`) keeps one terminal per pane, like Windows Terminal: new tabs
  open as workspaces.
- In a split workspace the focused pane is marked by an accent line along its top (on the shown
  tab when the pane has a strip), and the others are dimmed. `unfocused-split-opacity` (in `[window]`,
  0.15–1, default 0.7 as in Ghostty; 1 = no dimming) sets how much, and `unfocused-split-fill`
  what they fade toward (default: each pane's background).
  `divider-color` (in `[window]`, `"#rrggbb"`) colors the line between panes.
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
- onto another Zinc window's tabs (or its +/▾ buttons), to move there;
- onto the edge of a pane (the outer third on that side), to join that workspace as a split on
  that side, keeping their own split layout;
- onto the middle of a pane, to join that pane as tabs (with tab groups on).

A pane's own tabs can be dragged too:

- along their strip, to reorder them;
- onto another pane's strip or middle, to join it; onto a pane's edge, to become a new pane
  there (also beside its own pane, when that pane has other tabs);
- onto the title bar's tabs, to become a workspace of their own; resting on a workspace's tab
  for a moment switches to it, so a tab can be carried into another workspace's panes;
- outside every Zinc window, to open in a new window;
- into panes and title bars of other Zinc windows.

The drop area is highlighted while you drag.

If the shell exits with a non-zero code, the window stays open showing the code
until a key is pressed; a clean exit closes it.

### zn: controlling Zinc from scripts

`zn` (in `bin\` next to `zinc.exe`) opens, arranges and closes windows, workspaces, panes and terminals in the running
Zinc, types into terminals and reads them, once `[cli] remote-control` is on (Settings > Terminal > Command line):

```text
zn list                                       everything open, with ids ((*) = focused)
zn pane split right -- htop                   a pane beside this one, running htop
zn terminal new --pane 4 --profile wsl:Fedora a Fedora tab in pane 4
zn workspace new --name api --cwd .           a workspace in this folder
zn terminal send --terminal 5 ":w\r"          types into terminal 5 (\r = Enter)
zn terminal read --terminal 7 --lines 20      the last 20 lines of terminal 7
zn workspace color teal                       this workspace's color (rename and icon work alike)
zn action "zoom-in"                           any action, by its name
```

Commands are `zn <thing> <verb>`:

| Thing | Verbs |
| --- | --- |
| `window` | `list`, `new`, `focus`, `close` |
| `workspace` | `list`, `new`, `focus`, `close`, `rename`, `color`, `icon`, `move --to-window N\|new` |
| `pane` | `list`, `split right\|left\|down\|up`, `focus`, `close` |
| `terminal` | `list`, `new`, `send`, `read`, `title`, `focus`, `move --to-pane N`, `close` |

Run in a Zinc terminal, a command acts on that terminal (its pane, workspace, window); `--terminal`, `--pane`,
`--workspace`, `--window` (ids from `zn list`; they last while Zinc runs) aim elsewhere; outside Zinc it acts on the
window used last and says which on stderr. `--profile`, `--cwd`, and `-- program args` (run instead of the shell; for
a WSL profile, inside that distro) shape what opens; commands that open a terminal print its id. `close` asks first
when a command is running (`--force` doesn't). `terminal send` types its text as if typed (escapes: `\r` Enter,
`\e` Esc, `\t`, `\n`, `\xHH`, `\\`; `-` reads stdin as is; `--` before text starting with a dash, e.g. `-- -`); `--paste` sends it as a paste, which shells insert
without running. `terminal read` prints the screen, `--lines N` the last N lines with the scrollback, `--ansi` with
colors. `terminal title ""` and `workspace rename ""` give the name back. `--json` prints lists for scripts;
`zn <thing> --help` lists the rest. Exit codes: 0 done, 1 refused or failed, 2 wrong usage, 3 Zinc isn't running,
4 remote control is off.

Every terminal gets `ZINC_TERMINAL_ID` and `ZINC_PIPE` (how `zn` knows where it runs), and `zn`'s folder at the end
of its PATH. In WSL, where Windows' PATH may not be shared (`appendWindowsPath`), Zinc's shell integration for bash
and zsh defines `zn` from `ZINC_ZN` (zn.exe's path, passed in); in other WSL shells, `alias zn="$ZINC_ZN"`. For other
terminals, **Use zn in other terminals** (`[cli] path = true`) puts it on your user PATH (taken off again when turned
off, or on uninstall). With the debug log on, each command is logged, never the text sent.

## Layout

```
scripts/build-libghostty.sh       fetch + build libghostty-vt at a pinned commit
scripts/copy-vcruntime.sh         the Visual C++ runtime the native DLLs need, next to them
scripts/gen-keys.py               regenerate GhosttyKey.g.cs from the C header
assets/brand/                     icon frames, mark and lockups (docs/brand.md)
src/Zinc.Native.targets        ghostty-vt.dll + bundled ConPTY for runnable projects
src/Zinc.Core/                 everything testable without a window:
  Ghostty/                          P/Invoke bindings
  Pty/ConPty.cs                     pseudoconsole + child process + reader thread
  Terminal/TerminalSession.cs       terminal + pty glue, libghostty callbacks
  Input/                            scan code -> GhosttyKey map, keyboard layout translation
  Config.cs, ShellResolver.cs       config file, command resolution
src/Zinc/                      WinUI app: window, TerminalControl (Win2D render + input)
tests/Zinc.Tests/              xUnit v3: ABI check, headless sessions, unit tests
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

## Open-source software

Zinc ships Ghostty's terminal core (libghostty-vt, with simdutf, Highway, uucode and the Zig
standard library compiled in), z2d (in the built-in glyphs), Cascadia Mono NF, iTerm2-Color-Schemes,
the Windows App SDK, Win2D, ConPTY/OpenConsole, .NET, Tomlyn and a Heroicons shape. Settings > About
lists them with their licenses; the texts are in `Licenses\` next to `zinc.exe`.
