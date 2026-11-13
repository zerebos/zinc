# Shell integration: Zinc and Ghostty

This compares Zinc's automatic shell integration (`assets/shell-integration/`, `src/Zinc.Core/Terminal/ShellIntegration.cs`) with Ghostty's (`external/ghostty/src/shell-integration/`, `src/termio/shell_integration.zig`). It's for finding ideas worth taking, not a to-do list.

## Shells covered

| Shell | Zinc | Ghostty |
|---|---|---|
| bash | Git Bash and MSYS2 start as `bash --posix` with `ENV` set to Zinc's startup script. Any other bash gets a `PROMPT_COMMAND` bootstrap. | `bash --posix` with `ENV` pointing at its script, which leaves POSIX mode and fixes up `HISTFILE` |
| zsh | `ZDOTDIR` points at Zinc's `.zshenv`, which restores the user's | The same |
| OSH (Oils) | The bash script, from `PROMPT_COMMAND` (in WSL). YSH gets nothing. | Nothing |
| cmd | A `PROMPT` variable around the user's, or a Lua script under Clink | Nothing, because Ghostty doesn't run on Windows |
| PowerShell (pwsh and Windows PowerShell) | `-NoExit -Command` with Zinc's script as text | Nothing, because Ghostty doesn't run on Windows |
| fish | Nothing to load, because fish 4 sends its own marks | A `vendor_conf.d` script, found through an `XDG_DATA_DIRS` prepend |
| nushell | Nothing to load, because nushell sends its own marks | A `vendor/autoload` module through `XDG_DATA_DIRS` and `--execute 'use ghostty *'`, for ssh and sudo only, with no marks |
| elvish | Windows' elvish.exe starts with `-rc` and Zinc's script | `lib/ghostty-integration.elv`, found through `XDG_DATA_DIRS`, which the user turns on by adding `use ghostty-integration` to rc.elv. It sends A, C, D, and OSC 7, and no B, because elvish draws prompts on a background thread. |

### bash and OSH

When Zinc starts bash.exe directly (Git Bash, MSYS2), it uses the same idea as Ghostty, with its own code. It starts `bash --posix` with `ENV` set to `bash/startup.bash`, which leaves POSIX mode and reads the startup files as bash would. A login shell reads /etc/profile and the first of ~/.bash_profile, ~/.bash_login, and ~/.profile. Any other shell reads /etc/bash.bashrc and ~/.bashrc, or the `--rcfile`. The script then fixes up `HISTFILE` and loads `zinc.bash` last, so a startup file that assigns `PROMPT_COMMAND` can't drop it.

Any other bash (in WSL, or started by a launcher script) loads `zinc.bash` through a `PROMPT_COMMAND` bootstrap. A startup file that assigns `PROMPT_COMMAND` still drops that one.

OSH runs the bash script too. OSH never shows PS0, so its A carries `zinc_c=enter`, and Zinc marks C itself, as it does for cmd. In OSH, `declare -f` needs names, so the script silences it when it looks for marks the prompt already sends.

### cmd

Zinc wraps the user's `PROMPT` in its own (`ShellIntegration.CmdPrompt`), which sends D with no status, A with `zinc_c=enter`, the folder as OSC 9;9, and B. cmd has no hook that runs before a command, so after a prompt whose A carries `zinc_c=enter`, the session marks C itself at the first line break of output that follows an Enter (`TerminalSession.COnEnter`). It reads the command's text back from the input cells. Plain cmd reports no exit codes.

Under Clink, `clink/zinc.lua` is on `CLINK_PATH`, and it does anything only when Zinc set `ZINC_CMD_PROMPT`. It sends these:

- D with `os.geterrorlevel()`. The errorlevel is sticky, because built-ins like `echo` and `title` keep the last one, while `cd` and `set` reset it. So a line that's only `echo`, `title`, `rem`, or `cls`, or an empty one, with no `&` or `|`, sends D without a code (`keepsErrorlevel`).
- A and OSC 9;9 from `clink.onbeginedit`.
- B through `CLINK_PROMPT_SUFFIX`, which Clink adds after every prompt filter, so a theme that replaces the prompt and stops filtering keeps it.
- C with `cmdline_url` from `clink.onendedit`, written after the Enter's line break.

A prompt filter that runs before all others strips the marks from Zinc's `PROMPT`, so they aren't sent twice.

### elvish

Windows' elvish.exe starts with `-rc` and Zinc's `elvish/zinc.elv`. That script evaluates the user's rc (`$runtime:rc-path`, or their own `-rc`, which arrives through `ZINC_ELVISH_RC`) and adds its names with `edit:add-vars`. It then marks A before readline, C with `cmdline_url` after readline, and D with the exit status after each command, which is an external program's status, or else 1. It reports the folder with OSC 9;9 on Windows and OSC 7 elsewhere, and it sends no B. Zinc can't reach elvish in WSL, because wsl.exe's command can't take Zinc's `-rc`.

### What fish and nushell send

Traced in Zinc with `ZINC_TRACE`:

- **fish 4.3** (Homebrew, on Fedora in WSL) sends `133;A;click_events=1`, `133;B`, `133;C;cmdline_url=<encoded>`, `133;D;<status>`, and `7;file://host/path`. That's everything Zinc's command list needs, including the command's text.
- **nushell 0.116** (`$env.config.shell_integration`) sends `133;A;k=i;click_events=1`, `133;P;k=r` (the right prompt), `133;B`, `133;C` with no command text, `133;D;<status>`, and a title (OSC 2). It reports the folder with OSC 7 on Linux and OSC 9;9 on Windows (its `osc7` and `osc9_9` defaults), and it draws the prompt again (A, P, and B) on every key. Zinc reads the command's text back from the input cells, so the command list works without `cmdline_url`, and the text is exact, `;` and double spaces included. nushell's OSC 633 (VS Code's protocol, whose `633;E` carries the command line) goes out only inside VS Code (`TERM_PROGRAM=vscode`), so Zinc never sees it.

### Gaps

- An older fish without its own marks gets none, because Zinc loads nothing into it. Ghostty turns on `click_events` from fish 4.1, and Zinc was traced with 4.3. The fix would be Ghostty's, a `vendor_conf.d` script found through `XDG_DATA_DIRS`. In WSL, `XDG_DATA_DIRS` would have to go through `WSLENV`, and Zinc's bash and zsh scripts would have to export it to their children, because fish usually starts from them. The script would also have to keep the defaults (`/usr/local/share:/usr/share`) when the variable was unset.
- Zinc's bash and zsh scripts remove Zinc's variables (`PROMPT_COMMAND`, `ZDOTDIR`, `ZINC_SHELL_INTEGRATION_DIR`) once they run, which fish and nu never do. So `Prepare` leaves them out when the command line shows the shell is fish or nu (`fish.exe`, `nu.exe`, `wsl.exe -- fish`, `-e`, or `--exec`). It can't see a distro whose login shell is fish (plain `wsl.exe`), which still inherits them. A zsh started from there still works, because Zinc's `.zshenv` restores the user's.

## The marks themselves

| | Zinc | Ghostty |
|---|---|---|
| Prompt start | `133;A;aid=<pid>` (zsh, elvish), `133;A;aid=<pid>;redraw=last` (bash), and `redraw=0` (pwsh, cmd, Clink) | `133;A;cl=line` (zsh) and `133;A;redraw=last;cl=line;aid=<pid>` (bash). fish and elvish add `click_events=1` and `aid`. |
| Continuation prompts (PS2) | `133;P;k=s` and B around bash's PS2. zsh doesn't need them, because its C carries the text. | `133;P;k=s`, and bash marks each line of a multi-line PS1 with `P;k=s` |
| Input start | `133;B` | `133;B` in bash and zsh, but not in fish or elvish |
| Command start | `133;C;cmdline_url=<percent-encoded>` (zsh, pwsh), and a plain `133;C` in bash, which sends it from PS0, where the line can't be seen | `133;C`, with no command text |
| Command end | `133;D;<status>`, only after a prompt was shown | `133;D;<status>`, plus `aid` in bash |
| Folder | `7;file://host/<percent-encoded path>`, and `9;9;"<path>"` in pwsh | `7;kitty-shell-cwd://host<path>`, with the path raw |
| String terminator | BEL for marks, and ESC\ for zsh's OSC 7 | BEL everywhere |
| Title | Zinc sets it from the marks (`shell-titles`), with the folder at the prompt and the command while it runs, so every shell that marks gets it with no script. A title the running program sets wins. | The `title` feature in each script, with the folder at the prompt and the command while it runs |

Why Zinc's marks differ:

- **`cmdline_url`**: Zinc's command center, palette, and attention dots show what ran. Ghostty keeps no command history, so it doesn't need the text.
- **Exit codes**: libghostty ignores D's status, so Zinc parses it itself (`ShellMarks.cs`) for failure bars and notifications.
- **Percent-encoded `file://`**: paths with spaces, `#`, or non-ASCII characters survive the URI. Ghostty's `kitty-shell-cwd://` exists so the path can be sent raw.

### Ideas from Ghostty

- **Clicking at the prompt** (`cl=line` and `click_events`) moves the cursor. Zinc ports it from Ghostty's app code, because libghostty-vt has no API for it, with three differences:
  - A prompt that marks its input (B) but announces nothing gets arrow keys anyway, because Zinc's scripts and powerlevel10k's own integration announce nothing. Ghostty's scripts send `cl=line`.
  - A click past the text stops at its last character, so blank input cells and decoration across a wide gap don't count. zsh clears autosuggestions with input-marked spaces, and powerlevel10k's corner is input. Ghostty counts every input cell.
  - Click events go out as a press and a release, aimed at the text when the click missed it. Ghostty sends only the press, and nushell then ignores every later click.
- **`redraw`** tells the terminal whether the shell draws its prompt again after a resize, so the terminal can clear the rewrapped old prompt first. libghostty's C API assumes the shell doesn't (the Ghostty app assumes it does), and only this option on a prompt mark changes that. So Zinc adds `redraw=1` to marks that don't say, which are powerlevel10k's own, fish's, nushell's, and Zinc's zsh and elvish scripts. Zinc's scripts for shells that don't draw it again say so, with `last` for bash (readline draws only the last line again, as Ghostty's bash script says) and `0` for pwsh, cmd, and Clink. Without it, powerlevel10k's full-width frame leaves a stale copy above the prompt on every resize that narrows the pane, because zsh goes up only the rows it drew, and the terminal has already wrapped the frame onto more. The clear also needs a libghostty fix in `patches/ghostty`, because rewrapping marks every wrapped part of a prompt line as a prompt start, so the clear misses the upper parts. Zinc also tells the shell the new size only once a resize settles (see "ConPTY" in [architecture.md](architecture.md)), so a fast drag draws the prompt once.
- **`aid=<pid>`** separates nested shells' marks. Zinc does this differently, by telling shells apart by their prompt mark's options as a whole, because the shells that nest most (fish, nushell, powerlevel10k) send no `aid` but differ anyway. Zinc's scripts send `aid=<pid>` on A, so they always differ. Ghostty doesn't track nesting.
- **Secondary prompts** (`P;k=s`) are marked in bash. A command of several lines is read back from the screen row by row, so the blank rest of each row doesn't look like a decoration gap.
- **A dedicated TTY descriptor** in zsh (`sysopen -o cloexec /dev/tty`) carries the marks, so they still reach the terminal when the user redirects stdout in a hook. Zinc falls back to stdout when /dev/tty can't be opened.
- **No D from a precmd that zle runs**, for zsh plugins that run precmd again to refresh the prompt. Zinc's precmd sends nothing while zle is active (`zle` succeeds only then), but it puts B back on a prompt the theme rebuilt.
- **Marking each line of a multi-line bash PS1** (`P;k=s`) is left out, because ConPTY repaints multi-line prompts on a resize, and everything Zinc builds on marks starts from the prompt's first row.

### No fresh line for a prompt mark

The spec (FinalTerm's, which kitty and Ghostty follow) says a prompt-start mark (`133;A`, and `133;N`) first does a *fresh line*, which moves to a new line if the cursor isn't at the start of one. libghostty does exactly that, but Zinc doesn't. When an A or N arrives with the cursor mid-line, Zinc gives libghostty `133;P` instead, which marks a prompt start without moving the cursor.

Zinc always runs behind ConPTY, which keeps its own copy of the screen and repaints from it. A cursor move that ConPTY didn't make, and never hears of, leaves the two screens a line apart from then on. A PowerShell prompt that draws its text with `Write-Host` shows the problem, because Zinc's marks go in the prompt's return value, so they come after the text. A fresh line there would put the cursor, and everything PSReadLine draws after it, a line below the prompt. To see it, give PowerShell a `prompt` function that draws its text with `Write-Host` and returns an empty string. Windows Terminal does no fresh line either, so a prompt that works there works here.

The tradeoff is that a prompt whose text comes before its mark (a `Write-Host` prompt, or any shell whose previous output didn't end its line) is recorded as starting where the mark landed, at the end of the prompt line rather than its start. Nothing visible changes, because Zinc keeps a prompt's *row* (jumping to a prompt, failure bars, scrollbar ticks, and clearing all go by rows), and it reads the command's text from where typing starts (B). Putting such a prompt on a line of its own, as a terminal that follows the spec would, can't be done safely under ConPTY.

## Features beyond marks

Ghostty's `shell-integration-features`, with their defaults in brackets, and what Zinc does instead:

| Feature | What it does | Zinc |
|---|---|---|
| `cursor` [on] | A bar cursor at the prompt (DECSCUSR 5 and 6), reset before a command runs | Zinc does it from the marks with `cursor-style-at-prompt` (default "same"), so it works in every shell that marks, with no script. A program's DECSCUSR still wins. |
| `title` [on] | The folder at the prompt, and the command while it runs | Zinc does it from the marks and folder reports with `shell-titles` (on by default, `ShellTitle.cs`). A title the running program sets still wins. |
| `sudo` [off] | A `sudo` wrapper that keeps `TERMINFO`, so Ghostty's terminfo works as root | Not needed, because Zinc has no terminfo of its own |
| `ssh-env` [off] | An `ssh` wrapper with a `TERM` fallback, which forwards `COLORTERM` and `TERM_PROGRAM` | Zinc has no wrapper. Its `TERM` needs no fallback, and its `COLORTERM` and `TERM_PROGRAM` don't reach a remote host. |
| `ssh-terminfo` [off] | Installs Ghostty's terminfo on the remote host with `tic` | Not needed, because Zinc has no terminfo of its own |
| `path` [on] | Puts the `ghostty` binary on `PATH` | `zn`'s folder goes at the end of every terminal's `PATH`, and `ZINC_ZN` names it for WSL, where the bash and zsh scripts define `zn` |

Both terminals use the marks to confirm closing, so closing at a prompt doesn't ask. Zinc asks only when a pane has a running command, meaning a C with no D yet (`TerminalControl.RunningCommand`).

What only Zinc does:

- **Standing down**: each script checks whether the prompt already sends marks or folder reports (powerlevel10k's own integration, or `133;` or `]7;` in PS1, hooks, or functions) and leaves those to it. Ghostty's scripts don't check, and powerlevel10k under Ghostty is configured through Ghostty's variables instead.
- **A PSReadLine wrapper**: pwsh's B and C come from a `PSConsoleHostReadLine` wrapper, because a B inside the prompt string breaks PSReadLine's redraw after a failed command.
- **Windows paths**: PowerShell reports its folder with OSC 9;9, `ShellDirectory` resolves MSYS `/c/...` paths and WSL distro paths, and `WSLENV` carries Zinc's variables into WSL, where `/p` translates the folder.
