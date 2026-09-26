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
```

The app is unpackaged and Windows App SDK self-contained, so
`src/Phantom/bin/x64/<Config>/net10.0-windows10.0.26100.0/win-x64/Phantom.exe`
runs directly.

## Choosing a shell

1. Arguments after the executable: `Phantom.exe "C:\Program Files\Git\bin\bash.exe" --login -i`
2. The `PHANTOM_SHELL` environment variable (a full command line)
3. Otherwise `pwsh.exe`, then `powershell.exe`, then `cmd.exe`

Children get `COLORTERM=truecolor` and `TERM_PROGRAM=phantom`.

## Keys and mouse

| Input | Action |
| --- | --- |
| `Ctrl+Shift+C`, or `Ctrl+C` with a selection | Copy selection |
| `Ctrl+Shift+V`, `Shift+Insert` | Paste (bracketed when the app enables it) |
| Right click | Copy the selection if there is one, otherwise paste |
| Drag / double-click / triple-click | Select cells / word / line |
| Mouse wheel, `Shift+PgUp/PgDn` | Scroll back (`Ctrl+Shift+Home/End` jump to top/bottom) |
| `Ctrl+=`, `Ctrl+-`, `Ctrl+0` | Font size bigger / smaller / reset |
| `Shift` + mouse | Bypass app mouse reporting to select text |

When an app turns on mouse reporting (vim, htop, tmux), clicks, drags and the
wheel go to the app. On the alternate screen with alternate-scroll mode, the
wheel sends arrow keys (`less`, `man`).

If the shell exits with a non-zero code, the window stays open showing the code
until a key is pressed; a clean exit closes it.

## Layout

```
scripts/build-libghostty.sh    fetch + build libghostty-vt at a pinned commit
scripts/gen-keys.py            regenerate GhosttyKey.g.cs from the C header
src/Phantom/Ghostty/           P/Invoke bindings (layouts checked against ghostty_type_json)
src/Phantom/Pty/ConPty.cs      pseudoconsole + child process + reader thread
src/Phantom/Terminal/          TerminalSession (terminal+pty glue), TerminalControl (render+input)
src/Phantom/Input/             scan code -> GhosttyKey map, Win32 keyboard layout translation
```

## Updating libghostty-vt

The libghostty-vt C API is explicitly unstable. To move to a newer Ghostty:

1. Change `COMMIT` in `scripts/build-libghostty.sh` and run it.
2. Run `python scripts/gen-keys.py`.
3. Check the struct layouts in `src/Phantom/Ghostty/Native.cs` against the
   library's ABI manifest (`ghostty_type_json()`), and the enum values used.

## Known gaps

- IME (CJK input methods) is untested; there is no composition UI.
- No tabs, splits, config file, or scrollbar yet.
- Ligatures are intentionally disabled (glyphs are placed per cell).
- Kitty graphics, hyperlinks (OSC 8) and desktop notifications are parsed by
  libghostty but not surfaced yet.
