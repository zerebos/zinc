# Shell integration: Phosphor vs Ghostty

Where Phosphor's automatic shell integration (`assets/shell-integration/`,
`src/Phosphor.Core/Terminal/ShellIntegration.cs`) and Ghostty's (`external/ghostty/src/shell-integration/`,
`src/termio/shell_integration.zig`) differ, as of 2026-10-01. Written to find ideas worth taking,
not as a to-do list.

## Shells covered

| Shell | Phosphor | Ghostty |
|---|---|---|
| bash | script, loaded through `PROMPT_COMMAND` | script, loaded by starting `bash --posix` with `ENV` pointing at it (then leaving POSIX mode; HISTFILE fixed up) |
| zsh | `ZDOTDIR` → our `.zshenv`, which restores the user's | same mechanism |
| PowerShell (pwsh, Windows PowerShell) | `-NoExit -Command` running our script as text | none: Ghostty doesn't run on Windows |
| fish | **nothing to load**: fish 4.x sends its own marks (see below) | `vendor_conf.d` script through an `XDG_DATA_DIRS` prepend |
| nushell | **nothing to load**: nushell sends its own marks | `vendor/autoload` module through `XDG_DATA_DIRS` + `--execute 'use ghostty *'`, for ssh/sudo only (no marks) |
| elvish | none | `lib/ghostty-integration.elv` through `XDG_DATA_DIRS` |

What fish and nushell send by themselves, traced in Phosphor (`PHOSPHOR_TRACE`) on 2026-10-01:

- **fish 4.3** (Homebrew, Fedora on WSL): `133;A;click_events=1`, `133;B`,
  `133;C;cmdline_url=<encoded>`, `133;D;<status>`, `7;file://host/path`. Everything our command
  list needs, including the command's text.
- **nushell 0.116** (`$env.config.shell_integration`): `133;A;k=i;click_events=1`,
  `133;P;k=r` (right prompt), `133;B`, `133;C` (no command text), `133;D;<status>`, a title (OSC 2).
  The folder report is OSC 7 on Linux and OSC 9;9 on Windows (its `osc7`/`osc9_9` defaults). It
  redraws the prompt (A/P/B again) on every key. Phosphor reads the command text back from the
  input cells, so the command list works without C's `cmdline_url`. nushell also sends OSC 633
  (VS Code's protocol, which carries the command line in `633;E`) when `osc633` is on, and it's
  on by default. We don't parse 633; it's the way to get exact text if reading back the cells
  ever falls short.

Gaps:
- An older fish without its own marks (Ghostty enables `click_events` only from 4.1; we checked 4.3) gets none, and we load nothing into it. If that
  matters, the fix is Ghostty's: a `vendor_conf.d` script found through `XDG_DATA_DIRS`.
  In WSL, `XDG_DATA_DIRS` would have to be passed through `WSLENV`. Our bash and zsh scripts
  would also have to export it to their children, since fish is usually started from them.
  The script must leave the defaults (`/usr/local/share:/usr/share`) in place when the variable
  was unset.
- Our variables (`PROMPT_COMMAND`, `ZDOTDIR`, `PHOSPHOR_SHELL_INTEGRATION_DIR`) are removed by
  our bash/zsh scripts once they run. A profile whose shell is fish or nu never runs them, so
  that shell and its children inherit the variables. A zsh started from there still works
  (our `.zshenv` restores the user's), but a cleaner fix is to skip the variables when
  `Prepare` can see the shell is fish/nu. It can't see that through `wsl.exe` or a launcher.

## The marks themselves

| | Phosphor | Ghostty |
|---|---|---|
| Prompt start | `133;A` | `133;A;cl=line` (zsh), `133;A;redraw=last;cl=line;aid=<pid>` (bash); fish/elvish add `click_events=1`/`aid` |
| Continuation prompts (PS2) | not marked | `133;P;k=s` (and bash marks each line of a multi-line PS1 with `P;k=s`) |
| Input start | `133;B` | `133;B` (bash, zsh); not in fish/elvish |
| Command start | `133;C;cmdline_url=<percent-encoded>` (zsh, pwsh); plain `133;C` (bash: sent from PS0, which can't see the line) | `133;C`, no command text |
| Command end | `133;D;<status>`, only after a prompt was shown | `133;D;<status>` (+ `aid` in bash) |
| Folder | `7;file://host/<percent-encoded path>`; pwsh: `9;9;"<path>"` | `7;kitty-shell-cwd://host<path>` (raw path, no encoding) |
| String terminator | BEL for marks, ESC\ for zsh's OSC 7 | BEL everywhere |
| Title | none (programs and prompts set their own; `program-titles` decides what we show) | `title` feature: folder at the prompt, command while it runs |

Why ours differ:
- **`cmdline_url`**: our command center, palette and attention dots show what ran. Ghostty keeps
  no command history, so it doesn't need the text.
- **Exit codes**: libghostty ignores D's status. We parse it ourselves (`ShellMarks.cs`) for
  failure bars and notifications.
- **Percent-encoded `file://`**: paths with spaces, `#` or non-ASCII survive the URI. Ghostty's
  `kitty-shell-cwd://` exists so the path can be sent raw.

Ideas from Ghostty's side:
- **`cl=line` / `click_events`**: click-to-move-cursor at the prompt. libghostty parses these; the
  app would have to turn a click on the input line into arrow keys. fish and nushell already
  announce `click_events=1`.
- **`redraw=last`** (bash): tells the terminal the shell redraws the prompt's last line on
  resize, so the terminal can clear it rather than reflow it. Ghostty's docs credit
  integration with "resizing the window with a complex prompt usually paints much better".
- **`aid=<pid>`**: separates nested shells' marks (a shell inside a shell). Our `Osc133Scanner`
  doesn't track it. Today a nested shell's marks simply interleave (the outer `fish` command
  above stays open, "exit ?").
- **Secondary prompts (`P;k=s`)**: lets "select output" and jump-to-prompt treat a multi-line
  command as input, not output.
- **Writing marks to a dedicated TTY descriptor** (zsh: `sysopen -o cloexec /dev/tty`), so they
  still reach the terminal when the user redirects stdout in a hook. We print to stdout.
- **Not sending D from zle-invoked precmd** (zsh plugins that re-run precmd to refresh the
  prompt). Worth checking against our zsh script if a prompt plugin ever doubles D marks.

## Features beyond marks

Ghostty's `shell-integration-features` (defaults in brackets) and where Phosphor stands:

| Feature | What it does | Phosphor |
|---|---|---|
| `cursor` [on] | bar cursor at the prompt (DECSCUSR 5/6), reset before a command runs | not done. Our `cursor-style` setting is the default shape everywhere |
| `title` [on] | title = folder at the prompt, command while running | not done; titles come from the shell/prompt itself |
| `sudo` [off] | `sudo` wrapper keeping `TERMINFO`, so Ghostty's terminfo works as root | not needed: we use `TERM=xterm-256color` |
| `ssh-env` [off] | `ssh` wrapper: TERM fallback, forwards COLORTERM/TERM_PROGRAM | partly moot (no custom TERM); forwarding `TERM_PROGRAM` could matter once we set it |
| `ssh-terminfo` [off] | installs Ghostty's terminfo on the remote host with `tic` | not needed (no custom terminfo) |
| `path` [on] | puts the `ghostty` binary on PATH | we have no CLI to put there |

Both use the marks for close confirmation: closing at a prompt doesn't ask. Ours asks only when a
pane has a running command (C without D yet, `TerminalControl.RunningCommand`).

Phosphor-only:
- **Stand-down**: each script checks whether the prompt already sends marks or folder reports
  (powerlevel10k's own integration, `133;` or `]7;` in PS1/hooks/functions) and leaves those to it.
  Ghostty's scripts don't check; p10k under Ghostty is configured through Ghostty's variables instead.
- **PSReadLine wrapper**: pwsh's B/C come from a `PSConsoleHostReadLine` wrapper. A B inside the prompt
  string broke PSReadLine's redraw after a failed command.
- **Windows paths**: OSC 9;9 for PowerShell; MSYS `/c/...` and WSL distro paths resolved by
  `ShellDirectory`; `WSLENV` carries the variables into WSL (`/p` translates the folder).
