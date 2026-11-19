<div align="center">
  <a href="https://zincterminal.dev"><img src="https://zincterminal.dev/brand/app-icon-128.png" height="128" alt="Zinc"></a>
  <h1 align="center">Zinc</h1>
  <p align="center"><em>Every cell, exactly right.</em></p>
  <img src="https://github.com/user-attachments/assets/b063dd2c-835d-4424-a4d8-afebb85f4bec" alt="Zinc with panes in the card style. It shows a focused card with a Powerline prompt, a file tree, and a box-drawn test summary, beside two smaller cards with a git log and a dev server. Three workspaces run along the top, and one has a green mark because its command finished.">
</div>

Zinc is a native terminal for Windows, organized around how you actually work. It's built on libghostty, the same core that powers [Ghostty](https://ghostty.org).

Group a project's tabs and splits into a workspace, and they reopen where you left them. Shell integration sets itself up for PowerShell, bash, zsh, fish, and more, and marks each command, so a failure stands out before you read a line. When a command finishes, fails, or needs you while it's out of view, Zinc says so in plain words, like "Tests failed (exit 1) in workspace 2."

Underneath, it's precise in what it draws and in how you tune it. Box lines, blocks, and Powerline shapes meet without seams in any font, and every setting says what it does and applies the moment you change it.

Zinc is in alpha and free. Download the installer from the [latest release](https://github.com/zerebos/zinc/releases/latest) or from [zincterminal.dev](https://zincterminal.dev).

## What's in it

**Workspaces, panes, and tabs.** A workspace holds one project's terminals, split into panes, and each pane can hold tabs. Workspaces sit along the top or in a sidebar, and Zinc reopens all of them where you left them, with fresh shells in the same folders.

**Shell integration.** Zinc loads its integration into PowerShell, bash, zsh, fish, nushell, elvish, and cmd (fullest with Clink) without you editing a profile. Each command gets a mark in the gutter, and you can jump between prompts, copy a command's output, or click to move the cursor along the line you're typing.

**Notifications.** A dot on the tab or workspace, and a Windows notification if you're elsewhere, says what happened: a command finished, failed, or is waiting for you.

**Box drawing and Powerline.** Zinc draws box lines, blocks, Powerline separators, and Braille itself, cell by cell, so they line up in any font and at any line height. Powerline prompts work without a patched font. Emoji and CJK fall back to the right fonts, and Kitty graphics show images inline.

**Settings.** Zinc is configured with a `config.toml` file, but you never have to open it. The Settings window explains every setting in plain words, shows a preview as you change it, writes only the line that changed, and puts everything back with one click. About 650 color schemes come with it, along with custom shaders written for Ghostty or Shadertoy.

**Windows integration.** Zinc uses Mica, and it has a tray icon, a quick terminal on a global hotkey, the Windows default terminal, a jump list, and an "Open in Zinc" entry in Explorer. Zinc finds your shells on its own, including WSL distros, Git Bash, MSYS2, the Visual Studio developer shells, and SSH hosts.

**Scripting.** `zn`, the command-line tool, opens and arranges windows, workspaces, and panes. Once you turn on remote control, it can also type into terminals and read them.

## Installing

Zinc runs on Windows 10 version 2004 (build 19041) or later, and on Windows 11, on x64 and ARM64 PCs. On an ARM64 PC, use `Zinc-win-arm64-Setup.exe` instead of `Zinc-win-Setup.exe`. The ARM64 build is new and has had much less testing than x64.

The installer sets Zinc up for your user only, with no admin prompt. Zinc isn't signed yet, so Windows may show "Windows protected your PC" the first time. Select **More info**, then **Run anyway**. Installed copies update themselves, and you choose when to restart. The portable zip on the same release runs from any folder, without updates.

## Configuration

The first time Zinc starts, a welcome page asks how you like to work and writes your answers to `%APPDATA%\Zinc\config.toml`. After that, **Settings** (`Ctrl+,`) edits the same file, and Zinc picks up any change to it the moment it's saved, from the Settings window or from your editor.

The [guide](https://zincterminal.dev/docs) covers getting started, configuration, shell integration, and `zn`, and the [config reference](https://zincterminal.dev/docs/config) lists every setting and every action.

## Keys

The default keys follow Windows Terminal's, so `Ctrl+Shift+T` opens a tab and `Alt+Shift+Plus` splits a pane. The command palette (`Ctrl+Shift+P`) lists every action with its key, and `Settings > Keyboard` changes them. A `[keys]` table in `config.toml` does the same, and the [config reference](https://zincterminal.dev/docs/config) lists the actions it can bind.

## Known gaps

- Sixel isn't supported, because libghostty has no sixel API. Kitty graphics animations show only their current frame.
- Links and URLs continue across rows the terminal wrapped, but a URL an app broke with its own line break (to fit a fixed width, for example) is cut there.
- Default terminal mode needs Windows Terminal installed (its console host does the other half).
- Shell integration doesn't reach a bash in WSL whose startup files replace `PROMPT_COMMAND` (a bash started directly, like Git Bash, is fine), elvish inside WSL, YSH, or xonsh. Without Clink, cmd reports no exit codes.
- Git Bash and MSYS2 programs write to a console a few bytes at a time, so very large colorful output from them is slow in any Windows terminal (native programs and WSL are fine).

## Building from source

```powershell
pwsh scripts/build/libghostty.ps1   # the native DLLs, which need Zig 0.17
dotnet build
dotnet test
```

[docs/development.md](docs/development.md) covers what you need to install, running a dev build next to an installed Zinc, testing, releasing, and forking. [docs/architecture.md](docs/architecture.md) explains how Zinc works inside and where things live.

## Open-source software

Zinc ships Ghostty's terminal core (libghostty-vt, with simdutf, Highway, uucode, and the Zig standard library compiled in), z2d (in the built-in glyphs), glslang and SPIRV-Cross (custom shaders), Cascadia Mono NF, iTerm2-Color-Schemes, the Windows App SDK, Win2D, Vortice.Windows, ConPTY and OpenConsole, .NET, Tomlyn, Velopack, and a Heroicons shape. Settings > About lists them with their licenses, and the license texts are in `Licenses\` next to `zinc.exe`.

## License

Copyright 2026 Zerebos. Zinc is licensed under [Apache-2.0](LICENSE). The license doesn't cover the Zinc name or icon, so if you ship a fork, give it its own name and icon. Saying it's based on Zinc is fine.
