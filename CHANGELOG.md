# Changelog

What changed in each version testers got, newest first. Versions are `0.1.0-dev.N`, where N counts the commits
it was built from. Each release's notes are this file's entry for it (`scripts/release.ps1` checks the newest entry
is the version it builds, and hands it to the installer as the release notes).

## Unreleased

### New
- Settings backup (Settings > About): Export settings saves your settings, color schemes, shaders and open windows
  in one zip file; Import settings brings them back, on this PC or another. Your current settings are saved first,
  so an import can be undone.

## 0.1.0-dev.330 (2026-10-07)

### New
- Ligatures, for fonts that have them (Cascadia Code, JetBrains Mono, Fira Code): `=>` `!=` `->` `===` and the like
  drawn as one shape, each character still in its own cell. Turn them on in Appearance > Font > Ligatures.
- Duplicate workspace (the workspace's menu) copies the whole workspace now: its splits and tabs, each where its shell
  is, with its folder, icon, color and name.

### Changed
- New tabs and splits start in the folder you're in (the focused terminal's, with shell integration), instead of the
  profile's. In a workspace with its own folder, new tabs still start there, and splits start where you are. To get
  the old behavior back, set Layout > Panes > New tabs and splits start to "In the profile's folder".
- Duplicate tab and Duplicate pane start where the original is now, not where it started.
- With Tabs inside panes off, "New tab" is called "New workspace" in menus, the command palette and Settings, since
  that's what it opens.
- Some settings have clearer names in config.toml: `[window] pane-tabs`, `pane-tab-strip`, `unfocused-pane-opacity`
  and `unfocused-pane-fill` (were `tab-groups`, `group-tab-strip`, `unfocused-split-opacity`, `unfocused-split-fill`).
  The old names still work.

### Fixed
- A shell on another machine (over ssh) that reports its folder is no longer taken for a folder of the same name on
  this PC. New tabs, splits and reopened tabs start in the folder you ran ssh from, and the tab shows the title the
  remote shell sets.

## 0.1.0-dev.318 (2026-10-05)

### New
- A menu on right-click in a terminal: copy, paste and find; open or copy the link you clicked; copy the command you
  clicked, or select or copy its output (with shell integration); split, new tab, close. Each item shows its key.
  Shift+F10 or the Menu key opens it at the cursor.
- Shell integration for cmd: jump between its prompts, select or copy a command's output, see its commands in the
  command center and tabs follow its folder. (cmd can't report exit codes, so failed commands get no red bar.)
- Shell integration for elvish (your `rc.elv` still runs first) and OSH (Oils) in WSL: prompts, commands with their
  exit codes, folders.
- Font weight and bold weight, from thin to black (Appearance > Font). A font without that weight shows its nearest.
- Fallback fonts: pick the fonts for characters yours lacks (Japanese, symbols, icons), tried in order before
  Windows chooses (Appearance > Font).

### Changed
- Right-click opens that menu now, instead of copying a selection or pasting. To get the old behavior back, set
  Terminal > Mouse > Right-click to "Copy or paste" (or "Paste").

### Fixed
- Developer PowerShell for Visual Studio (and any PowerShell profile that runs a command and stays open) gets shell
  integration now: command marks, failed-command bars, the command center's folder.
- Git Bash and MSYS2's bash keep their shell integration even when your `.bashrc` sets `PROMPT_COMMAND` outright
  (`PROMPT_COMMAND='history -a'` used to switch the marks off).

## 0.1.0-dev.306 (2026-10-05)

### New
- New tabs pick up environment changes: a PATH an installer changed works without restarting Zinc, as in
  Windows Terminal (Terminal > Shell > Pick up environment changes).
- Environment variables of your own, for every program (Terminal > Shell) or per profile (on its page), in
  config.toml's `[environment]`. `%PATH%` and the like work in values; an empty value removes a variable.
- Open links with Alt+click, or a plain click, instead of Ctrl+click (Terminal > Mouse > Open links with).
- Middle-click pastes, if you turn it on (Terminal > Mouse).
- A sound when a long command finishes out of view: Windows' notification sound or its beep (Notifications).
- Turn off the question before closing or quitting while a command runs (Layout > Closing).
- How far a resize key moves a pane's divider (Layout > Panes > Resize step).
- Line height: more room between lines, as a percentage or in pixels (Appearance > Font). Box drawing still joins up.
- Match 256 colors to the scheme: programs using the extended palette get shades made from your scheme's colors
  (Appearance > Colors).
- Open in Zinc from File Explorer: right-click a folder or a drive (Layout > When you come back; off by default).

### Changed
- Settings: pages inside a page (a profile, color schemes, shaders, the scheme editor) show where you are in their
  heading (Profiles > PowerShell); click any part to go back up, or use the back arrow in the title bar.
- Settings: in a narrower window the sidebar folds to icons, and to a menu button when narrower still.
- Settings: headings and text use Windows' own text styles.
- The welcome window's page dots slide from page to page.
- Workspace colors are a grid like the icons: arrow keys move between them, and screen readers name the chosen one.
- Wording: buttons say what they do ("Delete profile", "Close workspace", "Quit Zinc", "Don't allow"), key
  sequences show as Ctrl+A > C everywhere, and settings refer to each other by their exact names in quotes.
- An update that's ready offers "Restart Zinc" or "Install when I quit" (it always installs when you quit).
- A dot's meaning is in words: tab tooltips, screen readers and the tab switcher ("@") say "finished",
  "failed (exit 1)" or "needs you". The command-finished notification reads "npm test failed (exit 1)", then where
  and how long.

### Fixed
- Screen readers can now find and pick workspaces in the sidebar and tabs in a pane's tab strip. Before, they
  skipped them entirely.
- Arrow keys in the workspace icon and color choosers picked the next one and closed the menu. They move between
  choices now; Enter, Space or a click picks.
- Settings: the preview at the top of Appearance and profile pages lost its first lines for good when its rows got
  taller (line height) or the window narrower. It grows to fit now, and lays itself out again at each size.

## 0.1.0-dev.282 (2026-10-05)

### New
- A jump list: right-click Zinc's taskbar button for your recent profiles and folders, a new window, the quick
  terminal, Settings, and reopening what you closed. Each item opens in the window you used last. The same things
  work from the command line: `zinc --profile <id>`, `--folder <path>`, `--new-window`, `--quick-terminal`,
  `--settings`, `--reopen-workspace`, `--reopen-window`.
- Shaders have their own settings page (Appearance > Shaders > Edit shaders): your list, and a library of
  Zinc's shaders, yours and the folders you add (a collection you cloned, say), each with a line about it.
  Click one to see it in the preview (and whether it compiles), again to take it back; Add puts it in your list.
- Color schemes have their own page too (Appearance > Color scheme): the scheme in use (or your light and dark
  pair) with Edit and New scheme, and all ~650 schemes as cards you can filter by Dark, Light or Yours and search.
  Click one to see it in the preview, again to take it back; Use puts it in place.

### Changed
- Settings: the search box is in the title bar now (Ctrl+F or F3 goes there, Esc clears it), and pages use more of
  a wide window (up to 1000 px, as Windows Settings).
- Input methods (IME: Japanese, Chinese, Korean…) work by default. If keys ever behave oddly, Terminal > Troubleshooting
  > Input methods turns it off.
- Clearer wording across the app (settings, menus, notices, the palette). Some names changed:
  - "Window glass" is now Window background (Mica, Acrylic, Clear), with Window tint and Window tint color.
  - Palette: "Color scheme for this tab…", "Default color scheme…", "Workspace folder…", "Save tab text…",
    "Go to workspace: …".
  - Tooltips show your own shortcuts.

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
