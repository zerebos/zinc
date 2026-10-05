# Changelog

What changed in each version testers got, newest first. Versions are `0.1.0-dev.N`, where N counts the commits
it was built from. Each release's notes are this file's entry for it (`scripts/release.ps1` checks the newest entry
is the version it builds, and hands it to the installer as the release notes).

## Unreleased

### New
- A jump list: right-click Zinc's taskbar button for your recent profiles and folders, a new window, the quick
  terminal, Settings, and reopening what you closed. Each item opens in the window you used last. The same things
  work from the command line: `zinc --profile <id>`, `--folder <path>`, `--new-window`, `--quick-terminal`,
  `--settings`, `--reopen-workspace`, `--reopen-window`.
- Shaders have their own settings page (Appearance > Shaders > Edit shaders): your list, and a library of
  Zinc's shaders, yours and the folders you add (a collection you cloned, say), each with a line about it.
  Click one to see it in the preview (and whether it compiles), again to take it back; Add puts it in your list.

### Fixed
- Settings: while searching (or editing a profile or a scheme), clicking the section that was open before did
  nothing; it goes back to that section now. Search results no longer show a section as selected.

## 0.1.0-dev.254 (2026-10-04)

### New
- Zinc comes with six shaders of its own: `scanlines`, `crt`, `glow`, `phosphor` (in Lit Cell's green),
  `cursor-glow` and `cursor-trail`. Use them by name (`[shaders] files = ["crt", "glow"]`), or pick them from
  Settings > Appearance > Shaders > Add. Only `crt` and `cursor-trail` move on their own; the others cost nothing
  while the terminal is idle.
- A downloaded update says what's new in it: Settings > About > "What's new in …" (this file's entry for it).

### Changed
- The quick terminal (Win+`) is just the terminal now: no title bar, frame or tab strip, centered on the screen,
  fading and growing into place (Settings > Quick terminal > Animation). It's one terminal: splits yes, tabs and
  workspaces no. Dismissing it gives the foreground back to the window you came from, and closing it only hides
  it, so its shells and scrollback are still there next time.
- Resize the quick terminal by dragging its edges. Each monitor remembers its size; double-click an edge (or run
  "Reset quick terminal size") to go back to the configured one.

### Fixed
- Alt+F4 typed an escape sequence into the terminal (and rang its bell) instead of closing the window.
- The quick terminal came out too big, or too small, the first time it opened on a monitor with another scale.
- Box-drawing lines (prompt frames, `─`) had a gap in every cell after a window moved to a monitor with another scale.
- Settings: a short page (a search with few results) slid off the right side of a window wider than about 1230 px.
- Zinc could close without a word while a window was being resized, most likely when dragging fast. Resizing is also
  lighter now: most sizes made Zinc draw every character's shape again.

## 0.1.0-dev.228 (2026-10-04)

### Faster
- Japanese, Chinese, Korean and emoji draw as fast as other text: a screen full of them took ~200 ms a frame
  (5 frames a second), now ~1 ms.

### Less graphics memory
- Terminals with the same font share their drawn letters instead of each keeping a copy.
- A tab left in the background for a minute gives its graphics memory back (tens of MB for a maximized tab on a
  high-resolution screen) and makes it anew when you come back. Settings > Terminal > Troubleshooting has the
  switch and the delay.
- A terminal showing thousands of different characters no longer keeps growing its memory.

### Fixed
- With three or more shaders, effects piled up on themselves while they animated.
- Tabs in the background drew frames no one could see.
- Coming back to a tab showed its old picture for a moment, then jumped to the new one.
- Closed terminals could still react to their window moving to another monitor.
- Recovery after the graphics driver resets is more reliable.

## 0.1.0-dev.221 (2026-10-03)

### New
- Custom shaders, as in Ghostty: effects drawn over the terminal (CRT screens, glow, cursor trails), from GLSL
  files written for Ghostty or Shadertoy. Settings > Appearance > Shaders, or `[shaders]` in config.toml; a
  profile can have its own. A shader that doesn't compile is left out and a notice says why; saving a shader file
  applies it at once.

### Changed
- The installer is 49 MB instead of 85, and Zinc takes 95 MB on disk instead of 200.
- `zn list` cuts long names, titles and folders to fit a terminal.
- About's list of open-source software is in alphabetical order.

## 0.1.0-dev.213 (2026-10-03)

### Fixed
- Notices were hard to read over the new-workspace page.
- An update's download showed no progress.

## 0.1.0-dev.212 (2026-10-03)

### Fixed
- Failure marks stayed beside commands that `clear` had erased.
- After a prompt drawn with Write-Host, the cursor could land on the line below it.

## 0.1.0-dev.209 (2026-10-03)

### New
- `zn`, a command-line tool for controlling Zinc from its terminals and scripts (off until you turn on
  Settings > Terminal > Command line).
- Tabs show the folder at the prompt, and the command while one runs (`shell-titles`).

### Changed
- Low-latency drawing is on for everyone: what you type shows up about a screen refresh sooner.

### Fixed
- A terminal could stop drawing while still on screen.
- zsh's prompt marks could go astray; fish and nushell were handed bash and zsh settings.
- The debug log no longer fills with missing-profile warnings.

## 0.1.0-dev.201 (2026-10-03)

### New
- Experimental low-latency drawing (`[window] low-latency-drawing`): each terminal presents its frames as soon as
  they're drawn.

## 0.1.0-dev.200 (2026-10-03)

### New
- The debug log can trace typing (key to screen, stage by stage), to track down sluggish input. Logging no longer
  slows down the terminal.

## 0.1.0-dev.199 (2026-10-03)

### Fixed
- Zinc wouldn't start on a fresh Windows install ("Unable to load DLL 'ghostty-vt'"): it now brings the Visual C++
  runtime it needs.

## 0.1.0-dev.198 (2026-10-02)

The first release with an installer: Zinc installs per user, keeps itself up to date (it checks once a day,
downloads in the background, and installs on restart or when you quit), and appears in Settings > Apps.

Before this, testers got zip builds.
