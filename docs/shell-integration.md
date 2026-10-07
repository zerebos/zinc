# Shell integration: Zinc vs Ghostty

Where Zinc's automatic shell integration (`assets/shell-integration/`,
`src/Zinc.Core/Terminal/ShellIntegration.cs`) and Ghostty's (`external/ghostty/src/shell-integration/`,
`src/termio/shell_integration.zig`) differ, as of 2026-10-01. Written to find ideas worth taking,
not as a to-do list.

## Shells covered

| Shell | Zinc | Ghostty |
|---|---|---|
| bash | bash.exe started directly (Git Bash, MSYS2): **since 2026-10-05** the same idea as Ghostty's, our own code: `bash --posix` with `ENV` = `bash/startup.bash`, which leaves POSIX mode, reads the startup files as bash would (login: /etc/profile + the first of ~/.bash_profile, ~/.bash_login, ~/.profile; else /etc/bash.bashrc + ~/.bashrc, or the --rcfile), fixes HISTFILE, then loads `zinc.bash` last, so a startup file that assigns `PROMPT_COMMAND` can't drop it. Anything else (WSL, launcher scripts): `zinc.bash` through a `PROMPT_COMMAND` bootstrap, which such a startup file still drops | script, loaded by starting `bash --posix` with `ENV` pointing at it (then leaving POSIX mode; HISTFILE fixed up) |
| zsh | `ZDOTDIR` → our `.zshenv`, which restores the user's | same mechanism |
| OSH (Oils) | the bash script, from `PROMPT_COMMAND` (WSL); OSH never shows PS0, so its A carries `zinc_c=enter` (C on Enter, as cmd) and `declare -f` without names is silenced. YSH: none | none |
| cmd | **since 2026-10-05** a `PROMPT` variable around the user's (`ShellIntegration.CmdPrompt`): D (no status), A with `zinc_c=enter`, OSC 9;9 folder, B. cmd has no pre-exec hook, so for a prompt whose A carries `zinc_c=enter` the session marks C itself after the first line break of output following an Enter (`TerminalSession.COnEnter`); the text is read back from the input cells. No exit codes. **Under Clink (2026-10-07)**: `clink/zinc.lua` on `CLINK_PATH` (only when Zinc set `ZINC_CMD_PROMPT`) sends D with `os.geterrorlevel()`, A and OSC 9;9 from `clink.onbeginedit`, B through `CLINK_PROMPT_SUFFIX` (added after every prompt filter, so themes that replace the prompt and stop filtering keep it), C with `cmdline_url` from `clink.onendedit` (written after the Enter's line break); a first-of-all prompt filter strips PROMPT's own marks. Restoring the original PROMPT with `os.setenv` at load stopped Clink after one command, hence the filter | none: Ghostty doesn't run on Windows |
| PowerShell (pwsh, Windows PowerShell) | `-NoExit -Command` running our script as text | none: Ghostty doesn't run on Windows |
| fish | **nothing to load**: fish 4.x sends its own marks (see below) | `vendor_conf.d` script through an `XDG_DATA_DIRS` prepend |
| nushell | **nothing to load**: nushell sends its own marks | `vendor/autoload` module through `XDG_DATA_DIRS` + `--execute 'use ghostty *'`, for ssh/sudo only (no marks) |
| elvish | **since 2026-10-05**: Windows' elvish.exe starts with `-rc <ours>` (`elvish/zinc.elv`): it evals the user's rc (`$runtime:rc-path`, or their -rc via `ZINC_ELVISH_RC`) and adds its names with `edit:add-vars`, then marks A (before-readline), C with `cmdline_url` (after-readline), D with the exit status (after-command: an external program's, else 1); OSC 9;9 on Windows, OSC 7 elsewhere. No B. elvish in WSL isn't reached (wsl.exe's command can't take our -rc) | `lib/ghostty-integration.elv`, found through `XDG_DATA_DIRS` but **opt-in**: the user adds `use ghostty-integration` to rc.elv. A, C, D, OSC 7; no B (elvish draws prompts on a background thread) |

What fish and nushell send by themselves, traced in Zinc (`ZINC_TRACE`) on 2026-10-01:

- **fish 4.3** (Homebrew, Fedora on WSL): `133;A;click_events=1`, `133;B`,
  `133;C;cmdline_url=<encoded>`, `133;D;<status>`, `7;file://host/path`. Everything our command
  list needs, including the command's text.
- **nushell 0.116** (`$env.config.shell_integration`): `133;A;k=i;click_events=1`,
  `133;P;k=r` (right prompt), `133;B`, `133;C` (no command text), `133;D;<status>`, a title (OSC 2).
  The folder report is OSC 7 on Linux and OSC 9;9 on Windows (its `osc7`/`osc9_9` defaults). It
  redraws the prompt (A/P/B again) on every key. Zinc reads the command text back from the
  input cells, so the command list works without C's `cmdline_url`. nushell's OSC 633
  (VS Code's protocol, whose `633;E` carries the command line) goes out only inside VS Code
  (`TERM_PROGRAM=vscode`), so Zinc never sees it (traced 2026-10-03). Reading the cells back
  gives the exact text, `;` and double spaces included.

Gaps:
- An older fish without its own marks (Ghostty enables `click_events` only from 4.1; we checked 4.3) gets none, and we load nothing into it. If that
  matters, the fix is Ghostty's: a `vendor_conf.d` script found through `XDG_DATA_DIRS`.
  In WSL, `XDG_DATA_DIRS` would have to be passed through `WSLENV`. Our bash and zsh scripts
  would also have to export it to their children, since fish is usually started from them.
  The script must leave the defaults (`/usr/local/share:/usr/share`) in place when the variable
  was unset.
- Our variables (`PROMPT_COMMAND`, `ZDOTDIR`, `ZINC_SHELL_INTEGRATION_DIR`) are removed by
  our bash/zsh scripts once they run, which fish and nu never do. **Since 2026-10-03** `Prepare`
  leaves them out when the command line shows the shell is fish or nu (`fish.exe`, `nu.exe`,
  `wsl.exe -- fish`, `-e`/`--exec`). It can't see a distro whose login shell is fish (plain
  `wsl.exe`): that one still inherits them (a zsh started from there still works: our `.zshenv`
  restores the user's).

## The marks themselves

| | Zinc | Ghostty |
|---|---|---|
| Prompt start | `133;A;aid=<pid>` | `133;A;cl=line` (zsh), `133;A;redraw=last;cl=line;aid=<pid>` (bash); fish/elvish add `click_events=1`/`aid` |
| Continuation prompts (PS2) | `133;P;k=s` … `B` around bash's PS2 (zsh's C carries the text, so not needed there) | `133;P;k=s` (and bash marks each line of a multi-line PS1 with `P;k=s`) |
| Input start | `133;B` | `133;B` (bash, zsh); not in fish/elvish |
| Command start | `133;C;cmdline_url=<percent-encoded>` (zsh, pwsh); plain `133;C` (bash: sent from PS0, which can't see the line) | `133;C`, no command text |
| Command end | `133;D;<status>`, only after a prompt was shown | `133;D;<status>` (+ `aid` in bash) |
| Folder | `7;file://host/<percent-encoded path>`; pwsh: `9;9;"<path>"` | `7;kitty-shell-cwd://host<path>` (raw path, no encoding) |
| String terminator | BEL for marks, ESC\ for zsh's OSC 7 | BEL everywhere |
| Title | `shell-titles` (2026-10-03): folder at the prompt, command while it runs, done in Zinc from the marks (no script), so every marking shell gets it; a title the running program sets wins | `title` feature in each script: folder at the prompt, command while it runs |

Why ours differ:
- **`cmdline_url`**: our command center, palette and attention dots show what ran. Ghostty keeps
  no command history, so it doesn't need the text.
- **Exit codes**: libghostty ignores D's status. We parse it ourselves (`ShellMarks.cs`) for
  failure bars and notifications.
- **Percent-encoded `file://`**: paths with spaces, `#` or non-ASCII survive the URI. Ghostty's
  `kitty-shell-cwd://` exists so the path can be sent raw.

Ideas from Ghostty's side:
- **`cl=line` / `click_events`**: click to move the cursor at the prompt. **Done (2026-10-01)**,
  ported from Ghostty's app code (libghostty-vt has no API for it), with three differences:
  - A prompt that marks its input (B) but announces nothing gets arrow keys anyway, since our
    scripts and powerlevel10k's own integration announce nothing. Ghostty's scripts send
    `cl=line`.
  - Clicks past the text stop at its last character: blank input cells and decoration across a
    wide gap don't count (zsh clears autosuggestions with input-marked spaces; p10k's corner is
    input). Ghostty counts every input cell.
  - Click events go out as press and release, aimed at the text when the click missed it.
    Ghostty sends only the press, and nushell then ignored every later click.
- **`redraw=last`** (bash): tells the terminal the shell redraws only the prompt's last line on
  resize. libghostty already clears prompt lines on resize (its default assumes a full redraw).
  **Checked 2026-10-01, not needed under ConPTY:** pwsh, Git Bash (two-line prompt) and a long
  wrapped command all came through narrow/wide resizes intact: ConPTY repaints the screen itself
  after a resize. powerlevel10k's full-width frame was the exception: a fast drag (dozens of sizes)
  piled up stale copies of it, in Windows Terminal too (the user compared). Fixed by telling the
  shell the size only once a resize settles (100 ms; the view still reflows live): one redraw per
  drag, at most one stale copy, as a slow resize gives.
- **`aid=<pid>`**: separates nested shells' marks. **Done differently (2026-10-01)**: Zinc tracks
  shells by their prompt mark's options as a whole, since the shells that nest most (fish,
  nushell, powerlevel10k) send no aid but differ anyway. Our scripts send `aid=<pid>` on A so
  they always differ. Ghostty doesn't track nesting ("We don't currently do explicit command
  tracking").
- **Secondary prompts (`P;k=s`)**: **done for bash (2026-10-01)**. Also fixed reading a command of
  several lines back from the screen: it kept only the first line (the blank rest of each row
  looked like a decoration gap); rows are now read one by one.
- **Writing marks to a dedicated TTY descriptor** (zsh: `sysopen -o cloexec /dev/tty`), so they
  still reach the terminal when the user redirects stdout in a hook. **Done 2026-10-03** (stdout
  when /dev/tty can't be opened).
- **Not sending D from zle-invoked precmd** (zsh plugins that re-run precmd to refresh the
  prompt). **Done 2026-10-03**: our precmd sends nothing while zle is active (`zle` succeeds only
  then), but puts B back on a prompt the theme rebuilt.
- **Marking each line of a multi-line bash PS1** (`P;k=s`): skipped. ConPTY repaints multi-line
  prompts on resize (above), and everything we build on marks starts from the prompt's first row.

### No fresh line for a prompt mark (2026-10-03)

The spec (FinalTerm; kitty and Ghostty follow it) says a prompt-start mark (`133;A`, and `133;N`)
first does a *fresh line*: if the cursor isn't at the start of a line, move to a new one.
libghostty does exactly that. **Zinc doesn't**: when an `A` or `N` arrives with the cursor mid-line,
libghostty is given `133;P` (prompt start, no movement) instead.

Why: Zinc always runs behind ConPTY, which keeps its own copy of the screen and repaints from it. A
cursor move that ConPTY didn't make, and never hears of, leaves the two screens a line apart from
then on. A tester's PowerShell prompt drew itself with `Write-Host`, so our marks (in the prompt's
return value) came after its text; the fresh line put the cursor, and everything PSReadLine drew
after it, a line below the prompt. Windows Terminal does no fresh line either, so a prompt that
works there works here. Repro: `.scratch/tools/pwsh-profile-cursor.ps1` (the prompt in the
2026-10-03 note).

The tradeoff: a prompt whose text comes before its mark (a `Write-Host` prompt in PowerShell, or
any shell whose previous output didn't end its line) is recorded as starting where the mark landed,
at the end of the prompt line rather than its start. Nothing visible changes: Zinc keeps a prompt's
*row* (jump to prompt, failure bars, scrollbar ticks, clear handling all go by rows), and the
command's text is read from where typing starts (`B`). What a spec-following terminal would do, put
such a prompt on a line of its own, can't be done safely under ConPTY.

## Features beyond marks

Ghostty's `shell-integration-features` (defaults in brackets) and where Zinc stands:

| Feature | What it does | Zinc |
|---|---|---|
| `cursor` [on] | bar cursor at the prompt (DECSCUSR 5/6), reset before a command runs | **done terminal-side** (2026-10-01): `cursor-style-at-prompt` (default "same"), from the marks, so it works for every marking shell without scripts; a program's DECSCUSR still wins |
| `title` [on] | title = folder at the prompt, command while running | **done terminal-side** (2026-10-03): `shell-titles` (default on), from the marks and folder reports (`ShellTitle.cs`); a title the running program sets still wins |
| `sudo` [off] | `sudo` wrapper keeping `TERMINFO`, so Ghostty's terminfo works as root | not needed: we use `TERM=xterm-256color` |
| `ssh-env` [off] | `ssh` wrapper: TERM fallback, forwards COLORTERM/TERM_PROGRAM | partly moot (no custom TERM); forwarding `TERM_PROGRAM` could matter once we set it |
| `ssh-terminfo` [off] | installs Ghostty's terminfo on the remote host with `tic` | not needed (no custom terminfo) |
| `path` [on] | puts the `ghostty` binary on PATH | **done** (2026-10-03): `zn`'s folder at the end of every terminal's PATH, `ZINC_ZN` for WSL (bash/zsh scripts define `zn`) |

Both use the marks for close confirmation: closing at a prompt doesn't ask. Ours asks only when a
pane has a running command (C without D yet, `TerminalControl.RunningCommand`).

Zinc-only:
- **Stand-down**: each script checks whether the prompt already sends marks or folder reports
  (powerlevel10k's own integration, `133;` or `]7;` in PS1/hooks/functions) and leaves those to it.
  Ghostty's scripts don't check; p10k under Ghostty is configured through Ghostty's variables instead.
- **PSReadLine wrapper**: pwsh's B/C come from a `PSConsoleHostReadLine` wrapper. A B inside the prompt
  string broke PSReadLine's redraw after a failed command.
- **Windows paths**: OSC 9;9 for PowerShell; MSYS `/c/...` and WSL distro paths resolved by
  `ShellDirectory`; `WSLENV` carries the variables into WSL (`/p` translates the folder).
