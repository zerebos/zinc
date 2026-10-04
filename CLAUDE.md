# Zinc — notes for Claude

WinUI 3 (C#, .NET 10, Windows App SDK 2.5.x, unpackaged + self-contained) terminal
emulator on **libghostty-vt** (Ghostty's VT core, C API via P/Invoke), ConPTY, and
Win2D. See README.md for user-facing docs. Session history, open issues and the
test harness live in `.scratch/` (gitignored; read `.scratch/NOTES.md` first if it exists).

## Build / run

- `scripts/build-libghostty.sh x64` → `native/x64/ghostty-vt.dll` (Zig 0.16, pinned commit).
  `external/` (ghostty clone) and `native/` are gitignored build products. Both native DLLs (ghostty-vt,
  zinc-sprite) are built for `windows-msvc` and import **`vcruntime140.dll`, which a fresh Windows lacks**
  (Windows Sandbox: "Unable to load DLL 'ghostty-vt'"; every dev PC has it from something else, so it
  never shows here): `scripts/copy-vcruntime.sh` (run by build-libghostty.sh) copies VS's redistributable
  one into native/<arch>/, Zinc.Native.targets ships it app-local (build error if missing), release.ps1
  checks it. Check new native dependencies with pefile (`.scratch/vcdeps.py <folder>`).
- `dotnet build` (Zinc.slnx; add `-c Release` for perf work). App output:
  `src/Zinc/bin/x64/<Config>/net10.0-windows10.0.26100.0/win-x64/zinc.exe`.
- `dotnet test` — run after any change to bindings, TerminalSession or ConPty, and
  always after bumping the Ghostty commit.
- **Before every commit: `dotnet build` (the solution) AND `dotnet test`.** `dotnet test`
  alone does not build the app project (tests don't reference it): a compile error in
  src/Zinc once got committed, the post-commit install failed, and a "verification"
  ran the stale exe. When verifying the app, check the exe/dll timestamp is fresh.
- **A running zinc.exe locks the output** → build fails with MSB3027. Stop only *dev*
  builds — the user daily-drives the installed copy, and this session may be running in it:
  `Get-Process zinc -ErrorAction SilentlyContinue | Where-Object Path -like '*\src\Zinc\bin\*' | Stop-Process -Force`
  (the build output, whatever the repo folder is called; installed copies live under Programs\Zinc\versions)
  Never stop every Zinc (zinc.exe) process.
- Shell: args after the exe, else `ZINC_SHELL`, else pwsh → powershell → cmd.
- `ZINC_TRACE=<file>` records raw PTY output bytes (see which modes an app enables).
- `ZINC_LOG=<file>` appends diagnostics from `Log.Write` (Core). Cheap to add a line
  when something "doesn't happen" (e.g. pane lifecycle: Loaded / CreateResources / sizes).
  Users get the same log from `[debug] logging` (Settings > About > Troubleshooting: switch, Open
  folder / Copy / Clear) → `Log.SetEnabled` → `%APPDATA%\Zinc\logs\zinc.log` (dated
  lines, moved to zinc.old.log at 4 MB). ZINC_LOG wins (always on, short times: tools read
  them). Test it with `.scratch/tools/log-test.ps1` (no ZINC_LOG; removes the logs folder after).
  `Log.Write` only queues: a background thread appends batches (it used to open/append/close per line on
  the caller's thread: every keystroke paid a file write with logging on). `Log.Flush` before reading,
  clearing, after a crash and at exit; a killed process loses at most what's still queued (milliseconds).
  Typing trace (a tester's "sluggish typing, a space lands before the last letter", 2026-10-03): with the log
  on, each pane's Core `Input/InputLatency` (tested) gets key presses (with `GetMessageTime` queue delay and
  the character expected), characters, sends, output arrival and drawn frames (`TerminalControl.InputTrace.cs`)
  and logs `input: …` per stretch of typing: queue, key→char, handling, send→output, output→frame, out of
  order (with examples), swallowed, plus send→last output and output reads per key (a shell answers a key in
  several reads: pwsh 7-9 within ~1 ms). Posted WM_CHAR (keys.ps1) counts as "unexpected" (no key press).
  `.scratch/tools/typing-trace.ps1 [-LowLatency]` types into pwsh and prints them.
- `[window] low-latency-drawing` (**default on since 2026-10-03**; off = the CanvasControl fallback): `Terminal/SwapChainSurface.cs` replaces the pane's
  CanvasControl with a CanvasSwapChainPanel + CanvasSwapChain on Win2D's shared device; a frame is presented at
  once (High-priority enqueue) once output has paused 0.5 ms (max 3 ms wait, re-enqueued so the UI thread runs),
  at most one per refresh (after a present, the next waits for `CompositionTarget.Rendering`, hooked only while
  frames come). The pane goes through `Surface`/`Creator`/`Redraw()` in both modes; `_canvas` is null in swap
  mode (CanvasLifetime is canvas-only). A SwapChainPanel can't take a Background (WinUI throws): the pane's root
  grid gets the transparent one for hit-testing. Lost device: `DeviceLost` → new shared device, resources, swap
  chain. Verified: visual suite identical in both modes (`visual.ps1 -WindowSettings 'low-latency-drawing =
  true'`), benchmark 144 fps capped in both, moving a workspace to a new window, echo→frame 1.3-1.6 ms vs a
  CanvasControl frame that still waits for WinUI's commit. `bench-test.ps1 -Config` (.scratch/settings-lowlat).
- Hidden tabs free their swap chain (`[window] free-hidden-memory`, default on; `free-hidden-after`, default 60 s):
  `SwapChainSurface.FreeHiddenAfter` (static, set by App with Enabled), a per-surface DispatcherQueueTimer started
  on Panel.Unloaded, `FreeHidden` drops the chain + `ShaderPass.FreeTargets`; Loaded's Resize/Invalidate rebuild it.
  Logs `pane: swap chain: freed while hidden (WxH)`. Test: `.scratch/tmp/free-test.ps1` pattern (delay 2, Ctrl+Tab back).
- Drawing benchmark (Settings > About, under the logs): Core `Terminal/Benchmark.cs` (`BenchmarkWorkloads`:
  Colors / Synchronized / Text / Scrolling, seeded, a port of `.scratch/perf/load.py`; `TimeStats`,
  `BenchmarkResult`, `BenchmarkReport.Text()` = what Copy gives; tested) → App `BenchmarkWindow` (its own
  1200x760 window, `TerminalControl.Benchmark(config, software)` = a preview pane with a `FramePerf`;
  each workload written from a background thread for 5 s via `WriteForBenchmark`, no ConPTY) →
  `SettingsWindow.Benchmark.cs` (Run, "Also with software rendering", last results + Copy; logs
  `benchmark: …`). `FramePerf` is per pane now (`TerminalRenderer.Perf`); ZINC_PERF=1 + ZINC_LOG
  still logs every pane once a second. Test: `.scratch/tools/bench-test.ps1 [-Software]`.
- Config: `%APPDATA%\Zinc\config.toml` (TOML, grouped sections, `[profile.<id>]`; font
  size and padding are *points*; README has the reference). User schemes in `themes\` next to
  it, bundled ones (assets/themes, iTerm2-Color-Schemes' Ghostty-format files) in `Themes\`
  next to the exe. `ZINC_CONFIG=<file>` overrides the path — use it for tests instead of
  editing the user's real config (which sets `family = "JetBrainsMono NF"`). Schemes are
  looked up next to *that* file, so a test config copied elsewhere needs its `themes\` too;
  test scripts append their own `[window]` section (a TOML table can't be opened twice).
  The old key = value `config` is no longer read (the user's was converted once, by hand).
  Live reload: `App.WatchConfig` (FileSystemWatcher on the config folder: *.toml and themes\,
  not session.json; 300 ms settle) → `ReloadConfig` → `MainWindow.ApplyConfig`
  (MainWindow.Config.cs: re-resolve each tab's profile by id → `TerminalControl.ApplyConfig`,
  which calls `TerminalSession.SetTheme`; strips, dims, menus) and the ConfigNotice InfoBar.
  `ConfigFile.HasErrors` (invalid TOML) = report only, keep the settings in force.
  libghostty's "default palette" getter returns the last palette *set*, so the session keeps
  its built-in palette from creation to restore entries a new theme doesn't define.
- Session (reopening windows): `%APPDATA%\Zinc\session.json`, `ZINC_SESSION=<file>`
  overrides. **A run with `ZINC_CONFIG` set neither restores nor saves** unless
  `ZINC_SESSION` is also set — so test instances never touch the user's session. Launching
  with a command (args or ZINC_SHELL) opens it instead of restoring.
- **Single instance per session file** (`Program.cs`, `DISABLE_XAML_GENERATED_MAIN`): a later
  launch redirects its activation (args, exe path included) to the running process
  (`App.OnRedirectedLaunch`) and exits. So **a dev build started without `ZINC_CONFIG`
  would open a window inside the user's installed Zinc** instead of running — always set
  ZINC_CONFIG (or ZINC_SESSION to a scratch file, which also exercises redirection:
  `.scratch/tools/single-instance.ps1`). App's session mutex is the backstop if redirection fails.

## Architecture (where to look)

`src/Zinc.Core` (no WinUI; what the tests load):
- `Ghostty/Native.cs` — bindings. `Ghostty/GhosttyKey.g.cs` — generated by `scripts/gen-keys.py`.
- `Pty/ConPty.cs` — pseudoconsole, child process, reader thread, exit waiter.
- `Terminal/TerminalSession.cs` — owns the GhosttyTerminal + encoders + ConPty; libghostty
  callbacks; `ReadScreenText()` (formatter) for tests; internal `PtyReplyObserver` test hook.
  Light/dark: `theme = "light:X,dark:Y"` (`ThemeLibrary.TryParsePair`) resolves against
  `ConfigFile.SystemScheme` (App keeps it from `UISettings`; a flip → `SetSystemScheme` → config
  reload if `FollowsAppearance`; `test:appearance-light/-dark` simulate it). `Config.IsDark` (pair
  side, else background luminance) → `TerminalSession.SetColorScheme`: answers CSI ? 996 n via
  the COLOR_SCHEME callback, sends CSI ? 997 on change when mode 2031 is set. Chrome surfaces
  colored like the pane (selected tab, sidebar item, pane strip) get dark text when light (`Contrast`).
  Options worth knowing: `RESIZE_PULL_SCROLLBACK` off (ConPTY has no scrollback; libghostty's
  docs name it), `XTVERSION` → "zinc <InformationalVersion>" (`Directory.Build.props`: `ZincVersion` +
  `-dev.<commit count>` unless the commit is tagged v<version>, + `+<7-char hash>`; About and the
  crash log show it too), `RENDER_HOLD`
  (synchronized output, mode 2026): the callback captures the renderer's snapshot at hold start
  (`RenderHoldBegan` → `TerminalRenderer.CaptureHeldFrame`, under the renderer's `_stateLock`,
  which guards the snapshot read outside the terminal lock); Draw skips updates while held and
  calls `EndRenderHold` after 1 s; a one-shot `_holdTimer` redraws at the timeout. Unset so far:
  ENQUIRY, TITLE_REPORT (off on purpose), GLYPH_PROTOCOL.
  Output path allocations (2026-10-02, measured with GC.GetAllocatedBytesForCurrentThread): parsing allocates
  nothing; keep the per-read work allocation-free. Titles: the reader only notes a change (one `TitleChanged`
  notice until `TakeTitle`, which the pane calls on the UI thread); cmd under ConPTY retitles ~14,000 times a
  second in a loop. Screen-flash filter: `ScreenFlashFilter.Concerns` lets reads without its sequences skip
  the copying filter. Together: ~20 MB/s of garbage on heavy output -> ~1.4 MB/s.
- `Input/` — scan code → GhosttyKey map; Win32 `ToUnicodeEx` layout translation.
- `Config.cs` (one resolved settings set; `Apply(key, value)` parses one value by its
  Ghostty-style key — the TOML reader maps each `[section] key` onto it, and scheme files are
  read through it), `ConfigFile.cs` (Tomlyn syntax tree → flat entries with file:line →
  global settings, `Profile(Id, Name, Config, Hidden)`s, warnings; includes; themes;
  `[keys]` → Keybindings.Configure; `[discover]` → `ProfileDiscovery` (WSL registry, pwsh/
  powershell/cmd, Git Bash, MSYS2 UCRT64, vswhere (cached per run), ~/.ssh/config hosts; pure
  helpers `SshHosts`/`WslProfiles`/`VisualStudioProfiles` are tested). Found profiles are the
  base for a `[profile]` with the same id, else listed after the file's. Only `Load` discovers;
  `Parse` takes the discoverer as a parameter, so tests never read the machine),
  `ConfigEditor.cs` (the app's writes to config.toml: `Set`/`Remove` a key path; locates via
  the Tomlyn syntax tree, *splices the text* so everything else stays byte-identical. Careful:
  a KeyValueSyntax span includes its line break — anchor on the value span. New profile
  sections go after the last `[profile.*]`; other new tables at the end),
  `ShellResolver.cs` (`Resolve` for the first tab: args > ZINC_SHELL > profile;
  `ForProfile` for tabs opened later).
- `Input/KeyDecision.cs` — "character path or key encoder?" for a key press (pure, tested).
  Bindings run in the terminal's OnKeyDown; with focus elsewhere (tab strip, sidebar, a button),
  `MainWindow.OnWindowKeyDown` (Root.KeyDown, unhandled only) runs Ctrl/Alt chords through the
  shown workspace's focused terminal (`Control.Invoke`).
- `Input/Keybindings.cs` — `KeyChord.Parse("ctrl+shift+c")` (WT key names) → `TerminalAction`;
  defaults follow Windows Terminal; `Configure` applies config's `[keys]` on top; `CopyFrom`
  updates the one instance App shares with every window and pane (live reload). Key sequences
  ("ctrl+a > c"): `KeySequence`, `Keybindings.Sequences`/`Continuations`/`ShortcutFor`, and the
  pure `KeySequencer` state machine (tested); `TerminalControl.Sequences.cs` feeds it from
  OnKeyDown (before plain chords) and draws the which-key hint. ZINC_TEST_KEYS entries go
  through it too (`PressForTest`); `.scratch/tools/leader-shot.ps1` captures hints/runs.
  `Input/Actions.cs` — the action registry: names/args as text ("split right", "new-tab
  wsl:Fedora"), palette titles. **A new action needs an entry there (and the README table:
  a test checks it), plus a handler** in TerminalControl.RunAction (pane) or
  MainWindow.HandleAction (window).
- `Terminal/SelectionGesture.cs` — drag/double/triple-click selection (tested in C#);
  `word-boundaries` → `SetWordBoundaries` (gesture option 5, a `GhosttyCodepoints` list; setting
  it replaces Ghostty's whole default set, so `Boundaries` spells that set out).
- `Terminal/PasteCheck.cs` — whether a paste asks first (`paste-warning`, `large-paste-warning`;
  Ghostty's rule: unsafe = a line break while bracketed paste is off; tested). App side:
  `ClipboardText.Paste` (ContentDialog with a preview). `test:paste:<text>` (`|` = line break)
  pastes without touching the real clipboard. Ctrl+wheel zoom, copy-on-select and
  focus-follows-mouse (`OnPointerEntered`) live in `TerminalControl.Mouse.cs`.
- Terminal settings (2026-09-30): `cursor-style`/`cursor-blink` → `TerminalSession.SetCursorDefaults`
  (libghostty's DEFAULT_CURSOR_STYLE/BLINK: apply at once while the cursor is default, a program's
  DECSCUSR wins; tested via DECRQSS), `scrollback-lines` → `SetScrollback` (live), selection colors
  on `TerminalTheme` (schemes' `selection-*` now read; renderer uses them, else inverse),
  `close-on-exit` (`OnProcessExited`), `bell` + `bell-sound` (`TerminalControl.Bell.cs`: flash overlay,
  MessageBeep, `BellRang` → `MainWindow.OnBell` = Notified dot + taskbar flash; a burst rings once:
  0.3 s of quiet re-arms it), `screen-flash-bell` (Core `ScreenFlashFilter`, tested: `Feed` filters
  output under `_flashLock`, holds `CSI ? 5 h` 250 ms; `l` in time = bell, else applied late by
  `_flashTimer` → `ExpireFlash`; ConPTY passes both through, often in one read), `program-titles`
  (`TerminalControl.Title` is "" while off).
- File drops: Core `Terminal/DroppedPaths.cs` (Windows path → the pane's shell path via its
  `LinkContext`, the reverse of `LinkTarget.Classify`; `PathQuoting` Posix/PowerShell/Windows;
  tested) ← `TerminalControl.Drop.cs` (DragOver/Drop for StorageItems only, marked Handled, so tab
  drags still reach the pane host; `ShellEnvironment.QuotingFor`; pasted via `session.Paste`).
  `test:drop:<path>|<path>` runs it without a drag (real drags can't be automated).
  WSL drive paths use `LinkContext.WslMountRoot` (Core `WslConf`: the distro's /etc/wsl.conf
  `[automount] root`, read once per pane through `\\wsl.localhost`; null = not mounted); links too.
- `clipboard-read = ask` (default): OSC 52 reads are found in the output *before* libghostty
  (`ClipboardReadRequest`, tested): `TerminalSession.Feed` feeds up to the request, calls
  `ClipboardConsent` (blocking, on the reader thread, terminal unlocked), then feeds the request
  with `_readConsented`; the callback denies anything not consented (split requests, OSC 5522).
  App: `TerminalControl.ClipboardRead.cs` (waits on `ClipboardText.AskToRead`'s dialog; `_closing`
  cancels it; "Allow in this tab" remembered per pane). `ZINC_TEST_CLIPBOARD` stands in for the
  clipboard's text in tests. `launch-at-login`: `LoginStartup` keeps HKCU Run "Zinc" =
  `"<exe>" --at-login` in step (never with ZINC_CONFIG); `ShellResolver.LoginFlag` isn't a
  command; "tray" opens no window; install.ps1 repoints/removes it. `quit-fresh` (palette/keys only, not the tray: the user's call) = `App.AskQuit(fresh)`
  → `ForgetWindows` (session.json keeps recents and settings bounds, no windows).
- New-workspace page (design: claude.ai/artifact/Vw21Hh3F1zEYbUTshQ4E9d, option E; all 4 steps built):
  `StartPage.cs` (code-only: filter, Profile/Folder/Layout lists, name, "Show this page" = `new-workspace-page`;
  layouts = Core `Panes/StartLayout.cs`, `StartLayouts.Build` splits a Workspace, tested)
  lives in a TabViewItem whose Tag is the page, *not* a WorkspaceTab: `AllTabs()` is `OfType`,
  `SelectedTab` is null while it's shown, drags of it cancel, × / Esc go through `RemoveStartPage` /
  `LeaveStartPage` (`MainWindow.StartPage.cs`). `OpenNewWorkspace` (+, sidebar, NewWorkspace/NewTab
  actions; not the quick terminal) → `StartWorkspace` adds the workspace at the page's index, then
  removes the page. `WorkspaceTab.Folder` (Core `WorkspaceFolder`: path + WSL distro; `LaunchFor` gives
  any shell its start there, tested) → `StartIn` for new tabs in a group, splits and Alt+profile;
  saved in `WorkspaceState.Folder`. Recent folders: Core `RecentFolders` on `App` (session.json
  `recentFolders`), noted from each pane's DirectoryChanged (`NoteFolder`). TabView's + is named
  "New workspace" for UIA (named in `DisableTabAnimations`). Keys on the page: `OnWindowKeyDown` →
  `HandlePageAction` (window actions only); the palette there = `BuildPageCommands` (window commands run
  through HandlePageAction, profiles via `StartWorkspace` in the page's place); test keys, `palette:` and
  `test:palette-open:` replay there too. Sidebar: the list's
  `PageItem` (+ "New workspace", pill, ×; `Update(..., page, pageSelected)`). New windows (`App.OpenNewWindow`: New window action, tray) open on it too.
  Browse = `FolderDialog` (IFileOpenDialog via ComImport: can start in `\\wsl.localhost\<distro>\…`;
  the WinRT FolderPicker can't), result through `WorkspaceFolder.FromWindows` (tested). Steps 3-4
  (reopen: `CloseTabNow` → `RememberClosed` (a WorkspaceState, before the panes close) → Core
  `ClosedWorkspaces` on App (session.json `closedWorkspaces`, 10, tested) → the page's Reopen chips /
  `reopen-workspace` → `ReopenWorkspace` via `SessionLayout.Restore` + `OpenSaved`). Closed windows: the
  window's `Closed` handler (first thing, not the quick terminal) → `App.NoteClosedWindow` (`CaptureState`,
  skipped while quitting or for an emptied window; `_maximized` is tracked because AppWindow is gone by
  then) → Core `ClosedWindows` (5, session.json `closedWindows`, tested). The last window is marked
  `WasLast` (the session keeps it anyway); `RestoreSession` drops those entries. `reopen-window`, palette,
  tray menu → `App.ReopenWindow`. Test: `.scratch/tools/close-window.ps1` (posts WM_CLOSE to our others). Later ideas are
  in .scratch/NOTES.md.
- IME (`input-method`, **off by default** until the user confirms it): `TerminalControl.InputMethod.cs`. A
  focused pane's keyboard focus sits in a hidden TextBox at the cursor (`OnGotFocus` redirects;
  `FocusIsWithin` = pane or box, so moving between them isn't a focus change for programs; the find
  box counts as elsewhere). Its PreviewKeyDown runs `HandleKeyDown`, which marks encoded keys and
  shortcuts handled (no Tab navigation); keys that type text are left to the box: **real keystrokes
  reach a focused TextBox as TSF text, not as CharacterReceived** (the first version dropped every
  letter for the user; posted WM_CHAR hid it), so text is taken in `BeforeTextChanging` (sent, then
  cancelled: the box stays empty); CharacterReceived passes only control characters. VK_PROCESSKEY
  (229) and anything while composing go to the input method; TextComposition* show the box (styled as
  the terminal) and send the committed text. The box must be `IsTabStop = true` or `Focus()` refuses
  it. Logs `ime: …`. Real keystrokes can't be automated here (TSF; no SendInput): the user tests.
- **All the workspace rules in one place (name, icon, color, folder, page, reopen): `.scratch/workspaces.md`.**
- Workspace look: Core `Panes/WorkspaceLook.cs` (`WorkspaceIcons`: curated Segoe Fluent glyphs, saved by
  name; `WorkspaceColors`: the 8 colors + `ForFolder` hash; `WorkspaceColorMode` = `workspace-colors`;
  tested). `WorkspaceTab.Icon` → `TabHeader.SetIcon` (hides the profile icon; the sidebar shares the
  header). `AddWorkspace(..., folder, icon)` is the one way in (restore, reopen, transfer, page).
  Pickers: `WorkspaceLookPickers` (page + menu). `AutoColor` in MainWindow.StartPage.cs (page and
  `OpenTab`; cycle counter on App). Set/Clear folder: `MainWindow.WorkspaceLook.cs`.
- Zoom (per pane): Core `Terminal/ZoomSteps.cs` (levels = 10% of the configured size, clamped to
  sizes that fit; tested) → `TerminalControl.Zoom.cs` (`_zoomLevel`, badge; a config font-size
  change keeps the level). Actions `zoom-in`/`zoom-out`/`reset-zoom`. The palette matches titles,
  plus `PaletteCommand.SearchWords` (from `Actions.SearchWords`; ranked below title matches).
- Kitty graphics: `Terminal/KittyImages.cs` (the process-wide PNG decoder libghostty calls —
  WIC via WinRT, blocking is fine on the MTA reader thread; premultiplied RGBA conversion),
  `TerminalSession.CollectImages` (per-frame placement snapshot, pixels copied only for images
  the renderer hasn't cached by id+generation; call under the render-state lock),
  `Terminal/KittyPlaceholders.cs` (Unicode placeholders = virtual placements, ported from
  Ghostty's graphics_unicode.zig; the `.Diacritics.g.cs` table is copied from there). Renderer:
  z<0 images under all rows, placeholders in the text pass, z>=0 above. Images need a cell
  size in pixels (`Resize` passes it) and storage enabled (`KittyImageStorageLimit`).
- Find: `TerminalSession.Search(regex)` formats the screen+scrollback (plain, unwrap, trim: one
  line per logical line) to find candidate lines, maps line k to screen rows via each row's
  WRAP flag, then matches the stitched `RowText`s of those rows (cell-by-cell via grid refs;
  wide chars whole; spacer heads add no text) — matches can span soft wraps (EndRow > Row).
  ~25 ms over a full 10k-row scrollback.
- Scrollback: 10,000 lines with libghostty's byte limit removed (its default kept ~700 rows).
  libghostty's own search API is literal + ASCII-case-insensitive only, hence our own. App side:
  `TerminalControl.Search.cs` (find box in the pane's `_root` grid; key/char/pointer handlers
  ignore events from it via `IsFromSearch`), renderer draws highlights, OnDraw the ticks.
- `Terminal/Links.cs` — `UrlDetector` (plain URLs in a row), `LinkTarget.Classify` (what a
  Ctrl+click may do: open web/mail, *reveal* local files, reject the rest; tested). File
  links map per pane via `LinkContext` (app `ShellEnvironment`: WSL distro, MSYS root).
  `TerminalSession.LinkAt(col, row)` finds an OSC 8 hyperlink (`grid_ref_hyperlink_uri`) or
  a URL in its logical line (soft-wrapped rows joined, up into the scrollback; `LinkMatch` rows
  are viewport rows, negative above it; shares `LogicalLine` with find). OSC 52 goes through `ClipboardWriter`/`ClipboardReader` on the session
  (null = denied); both are called mid-parse under the lock and must reply synchronously.
- `Session/SessionState.cs` — session.json records, `SessionLayout.Capture/Restore` (workspace ↔
  saved layout; tabs that can't reopen drop out, emptied panes close up), `SessionStore`.
  `Terminal/ShellDirectory.cs` decodes reported folders (OSC 7 URI / OSC 9;9 path) and
  `LaunchIn` turns one into a start (WSL `--cd`, Windows cwd, MSYS `/c/`). App side:
  `MainWindow.Session.cs`, `App` (restore at launch or tray click, save every 2 s when changed,
  and first thing in `Quit`). libghostty's pwd callback (`OPT_PWD_CHANGED`) → `ReportedDirectory`.
- Shell integration (OSC 133): `Terminal/ShellMarks.cs` (`Osc133Scanner` finds marks as output
  streams by, split reads included; `ShellMark.Parse`: exit codes, C's `cmdline_url`) and
  `TerminalSession.Commands.cs`: `Feed` writes up to each mark, then pins the cursor with a
  *tracked* grid ref (follows scrolling/pruning/reflow; freed with the command). libghostty marks
  prompt rows/cells itself but ignores D's exit code and keeps no history. `Commands()` snapshots
  as screen rows (= scrollbar units; cached until the next write/resize); `JumpToPrompt`,
  `CommandInView`, `SelectOutput`. A command `Ran` only if it has text: shells send D (and p10k C)
  after an empty Enter too. Text comes from C's cmdline_url, else input cells read back (wide gaps
  = prompt decoration: p10k draws its frame corner after B). App: `TerminalControl.Commands.cs`
  (actions, scrollbar ticks, failure bars; logs `command: ...` with ZINC_LOG), command center.
  Nested shells (`_nested` in TerminalSession.Commands.cs): a level per shell, told apart by its A
  mark's options (`ShellMark.Options`: p10k "", fish "click_events=1", nu "k=i;click_events=1", our
  scripts "aid=<pid>"). An A with new options while a command runs = a shell started by it (the
  command stays open as the level's host); an A matching an outer level = back there (`LeaveNested`:
  hosts close; a D just before is the host's exit, taken off the inner command it closed; hosts get
  no End, so their rows stop at the nested shell's first prompt). Lookalikes (zsh in zsh) stay one
  level. `TerminalControl.OnCommandsChanged` announces each finished command once (by Started) only
  once a later prompt exists, since a D's owner is known at the next A. `.scratch/tools/nest-trace.ps1
  -Shell ... -Lines ...` prints the raw marks and the `command:` log.
  `MainWindow.Attention.cs`: `TerminalControl.CommandFinished` → `TerminalTab.Attention` (dot on
  `TabHeader`/`GroupView` via `AttentionDot`, after the title or on the icon's corner on the rail;
  pulses on appearing and on window activation; "looking" = the focused terminal of the shown
  workspace in the active window, cleared in `UpdateFocusBorders`), `FlashWindowEx`, and close confirmation: user closes go
  through `AskClosePane`/`CloseTab`/`CloseTabs`/`AppWindow.Closing`/`App.AskQuit`; `ClosePane`,
  `CloseTabNow`, `Quit` never ask (a shell exiting must not). `[notify]` is a config group.
  Phase 3: OSC 9/777 notifications and OSC 9;4 progress come from libghostty callbacks
  (`TerminalSession.Notifications.cs`; OSC 9;9 stays a folder report) → blue `Attention.Notified`
  and a `ProgressRing` in `AttentionDot`; `TaskbarProgress` (ITaskbarList3 via ComImport).
  "toast" = `TrayIcon.ShowNotification` (NIF_INFO balloon, shown as a Windows notification;
  click = NIN_BALLOONUSERCLICK → `App.Notify`'s callback → `BringToPane`). Not AppNotificationManager:
  an unpackaged app's identity follows its exe path, and installs are versioned folders.
  **No fresh line on 133;A/N mid-line** (`WritePromptStart` hands libghostty 133;P: a move ConPTY never hears of
  desyncs the screens; docs/shell-integration.md has the why and the tradeoff).
  PSReadLine: B/C must be written from a `PSConsoleHostReadLine` wrapper; a B inside the prompt
  string broke PSReadLine's prompt redraw after a failed command (text overwrote `> `).
- Automatic shell integration (`shell-integration = auto|off`, per profile): `Terminal/ShellIntegration.cs`
  (Core, tested: `Prepare` → command line + per-launch env; ConPty builds an environment block)
  and `assets/shell-integration/` (LF-only via .gitattributes; copied to `ShellIntegration\`).
  pwsh/powershell: `-NoExit -Command ". ([scriptblock]::Create(ReadAllText(...)))"` (text, not a
  file: WinPS's Restricted policy); skipped when the args already run something. Everything else
  gets env: `PROMPT_COMMAND` bootstrap (bash; the script removes it), `ZDOTDIR` (zsh; `.zshenv`
  restores + sources the user's), `ZINC_SHELL_INTEGRATION_DIR` (forward slashes: MSYS reads
  C:/x, WSLENV `/p` translates it), `WSLENV`. Each script stands down where marks/OSC 7 already
  exist (p10k's `POWERLEVEL9K_TERM_SHELL_INTEGRATION`, `133;`/`]7;` in prompt/hook text).
  `ShellInjectionTests` run real pwsh, WinPS, Git Bash, MSYS2 zsh (skipped if absent);
  `.scratch/tools/si-live.ps1 -Command ...` checks a shell in the app (log + trace marks).
  fish 4.x and nushell send their own marks (traced 2026-10-01; nu's C has no cmdline, the text is
  read back): nothing loaded; elvish sends nothing (not covered yet). `docs/shell-integration.md` =
  how all this differs from Ghostty's.
- Click to move (`[terminal] click-to-move`, default on; Ghostty's `cursor-click-to-move`): Core
  `Terminal/PromptClick.cs` (port of Ghostty's `Screen.promptClickLine`: arrow counts over input
  cells, wide chars once, soft wraps; plus: the text ends at its last non-blank cell before an
  8-column gap (zsh writes spaces as input when clearing an autosuggestion; p10k's "─╯" corner is
  input) so a past-the-end click never sends an extra arrow that could accept a suggestion;
  `EventColumn` aims click events at the text) and `TerminalSession.PromptClick.cs` (`ClickToMove`:
  mode from the A mark's `cl=`/`click_events=` via `ShellMark.Click`, else arrows when the prompt
  marked input (B), our fallback for p10k/our scripts; prompt state from marks; not on the alt screen
  or above the prompt). Click events are press **and release**: nushell ignored every click after the
  first with a press alone (Ghostty/kitty send only the press). App: plain left click (no modifiers,
  no drag, no selection, pane already focused) in `TerminalControl.Mouse.cs`; `test:click:col:row`;
  `.scratch/tools/click-test.ps1 -Shell ... -Clicks "c:r",...` types a marker after each click.
- Custom shaders (`[shaders] files`/`animation` = Ghostty's custom-shader/-animation; per profile; low-latency drawing
  only): Core `Shaders/` (`ShaderSource`: Ghostty's Shadertoy prefix, `UsesTime`, glslang log -> "file, line N: ..."
  in the file's own lines; `ShaderTranslator`: zinc-shader.dll = `native-src/shader` (glslang from Ghostty's pkg +
  SPIRV-Cross with its HLSL backend, static MSVC runtime: imports only KERNEL32; `scripts/build-shader.sh`, run by
  build-libghostty.sh; one call at a time); `ShaderUniforms`: the std140 block, offsets tested against SPIRV-Cross's
  packoffsets; all tested in `ShaderTests`). App: `ShaderLoader` (read + translate + D3DCompile on the thread pool,
  cached per file by size+time and shared by panes; failures = messages), `ShaderPass` (Vortice D3D11 on Win2D's
  device: the frame drawn into a CanvasRenderTarget, then each shader as a full-screen triangle, ping-pong, the last
  into the swap chain's back buffer; y down as Ghostty's Metal), `SwapChainSurface.SetShaders/Animate/SetFocus` (an
  animation frame with nothing new reruns the shaders over the last picture: redrawing the terminal each refresh
  cost ~30% of a core; no animation on unloaded = hidden tabs; the shader clock pauses with a focus-paused
  animation, so hover redraws in an unfocused pane don't jump it; SPIRV-Cross zero-initializes variables: fxc's X4000), `TerminalControl.Shaders.cs` (load on start, after
  the window wired the pane, on every config reload and when `ShaderLoader.FileChanged` (watchers on the shaders'
  folders, 300 ms settle) names one of its files; after each load `ShaderProblems` = the pane's full current list ->
  `MainWindow.Shaders.cs` (per pane, union shown, closes when empty; closed-on lines stay quiet until a reload)).
  Uniforms from the frame: `TerminalRenderer.ForShaders` (`ShaderFrame`: palette/colors copied in DrawSnapshot,
  the cursor rect as `DrawCursor` draws it, only while the pane has shaders) -> `FillShaderUniforms` via
  `SwapChainSurface.FillUniforms` before each pass (cursor: left, bottom edge, size in pixels, as Ghostty's Metal;
  previous/change time in Core `ShaderUniforms.SetCursor`; iMouse = Shadertoy's, from the pointer handlers).
  Short cursor effects: `animation = false` freezes them as finished now; capture with a slow copy (DURATION 5).
  Zinc's own shaders: `assets/shaders` -> `Shaders\` (scanlines, crt, glow, phosphor, cursor-glow, cursor-trail; only
  crt and cursor-trail use time, tested), by name in `files` (`ShaderPaths.IsName/Resolve`: yours in `shaders\` next
  to the config first; `BundledFolder` settable for tests); Settings' Add is a menu of them + "From a file…".
  Settings (session 3): catalog `shaders.files` = `SettingKind.Files` (cards: `SettingsWindow.Shaders.cs`
  `ShaderFilesCard`, also in profile pages and search; rows = state via `ShaderLoader.Load`, Up/Down/Open/Remove;
  rebuilt rows give focus back to the same button, else the page scrolled to its first field), `shaders.animation`
  a Choice; paths written by Core `ShaderPaths.ForConfig` (relative to the config, else ~/, forward slashes). The
  preview pane animates as if focused. Test: `.scratch/tools/shader-shot.ps1 -Shaders a.glsl,b.glsl
  [-Extra 'animation = false'] [-Window ...] [-KeepOpen]`; samples in .scratch/shaders.
- Grid placement: Core `Terminal/GridLayout.cs` (whole cells inside the padding; `[terminal] padding-balance` splits the
  leftover between both sides, in whole device pixels so the atlas's subpixel offset never changes; tested) →
  `TerminalControl._gridLeft/_gridTop` (every pointer → cell conversion) and `TerminalRenderer.SetGridOrigin`.
- Resizes: `TerminalSession.Resize` resizes libghostty at once (live reflow) but ConPTY (the shell's
  SIGWINCH) only after 100 ms without another (`PtyResizeSettle`, a threadpool Timer): a drag's many
  sizes made p10k redraw its full-width frame each time and stale copies piled up (Windows Terminal does
  the same). Tests that check the child's size must wait for the settle.
- Scheme editor (`SettingsWindow.SchemeEditor.cs`): "Customize X" buttons on the scheme cards → `CustomizeScheme`
  (`ThemeLibrary.Customize`: the user's file, else a copy of the bundled one into themes\ that stands in for it;
  `BackToOriginal` deletes it; tested) → `_editingScheme` sub-page (any section; heading = the name; preview =
  that scheme via `PreviewConfig`). Writes go through Core `Settings/SchemeFile` (Get/Set one key, other lines
  untouched, `palette.N`; tested); the config watcher reloads, and the page updates in place via `_refreshers`
  (never re-render: an open picker would close). Base colors reuse `ColorControl` (`SettingDef.NullLabel`);
  palette chips have their own picker flyout (150 ms settle). Zinc's own → "Lit Cell" written to the config.
  New schemes: `NewSchemeFlyout` (card and editor) → `ThemeLibrary.Create(name, from)` (`NameProblem`; tested) →
  `UseScheme` (replaces one side of a light/dark pair) → editor; `DeleteScheme` (ContentDialog; user-only schemes).
  Glass: `[window] backdrop-tint` → `GlassBackdrop` (DesktopAcrylicController, tint = terminal bg, kept active) /
  `ClearBackdrop(tint)` (a transparent brush alone showed BLACK: the window also needs DWM blur-behind with an empty region + zero frame margins + WM_ERASEBKGND filled black, via a window subclass; redone on WM_DWMCOMPOSITIONCHANGED, undone on disconnect; as WinUIEx's TransparentTintBackdrop). Opacity reaches 0.
- Brand look (docs/brand.md, section 14 = what the app does differently): `Brand.cs` (UI color tokens per theme,
- Light/dark chrome (2026-10-02; before, every window forced Dark): `[window] appearance` system|light|dark →
  `Application.RequestedTheme`, set in **App's constructor** (the config is loaded there now; WinUI throws if it's
  set later), so code-fetched theme brushes agree with the windows. No `RequestedTheme = Dark` anywhere; caption
  buttons via `Brand.StyleCaptionButtons`; `Contrast.TextOn` flips text both ways (white on a dark scheme in light
  chrome); command marks follow the *terminal's* lightness. Live Windows-mode flips change the terminal pair only.
  Captures: `ZINC_TEST_APPEARANCE=light|dark` + `.scratch/tools/theme-shots.ps1 -Appearance light`.
  `ApplyAccent` overrides WinUI's SystemAccentColor* before any window when `[window] accent = "zinc"` (default;
  restart to apply)). `AttentionDot`: lit-cell Rectangle, `Attention.Bell` (amber, from `OnBell`), colors from
  `[notify] *-color` via static `SetColors` (version counter recolors on the next Show), pulse = endless 1.6 s
  fade of the inner shapes (**never animate the dot's own Opacity: hosts hide it with that on hover**), off
  with Windows animations and under `TestHooks.Still` (ZINC_TEST_STILL=1, set by attn-shot.ps1). Top
  workspace tabs: `TabItemDot` puts the dot in TabView's template CloseButton cell (button Opacity 0). About:
  the lockup SVG is the heading. Welcome: the app tile SVG. Artwork ships from assets/brand to `Brand\`.
  Review page: claude.ai/artifact/WnrBCkuKo2oECTe2eqXc7L; tools `brand-shots.ps1`, `brand-try.ps1`; unused
  variants in .scratch/brand-extras.
  Brand exports arrive with C2PA metadata (SVG <metadata>, PNG caBX); the repo's copies are stripped:
  `python scripts/strip-c2pa.py` after copying new ones into assets/brand (keep .scratch/brand's originals).
- Color settings (settings, not scheme keys: `ApplyFile` ignores them; Settings > Appearance > Readability):
  `[colors] minimum-contrast` (Core `MinimumContrast`: WCAG ratio, below it black/white; tested), `bold`,
  `bold-is-bright` (palette 0-7 → 8-15 from the frame's live palette), `faint-opacity`, `cursor-opacity`
  (a see-through block skips the glyph redraw), `find-highlight(-current)` (`MatchColorFor`), all applied in
  `TerminalRenderer.ReadRow`/`DrawCursor`; `[window] divider-color` (`ApplyDividerColor`, shared brush).
- Transparency: `[colors] background-opacity` → the renderer's `ds.Clear` alpha (and the canvas `ClearColor`);
  `[window] backdrop` → `MainWindow.ApplyBackdrop` (MicaBackdrop / DesktopAcrylicBackdrop / `ClearBackdrop`: a
  transparent Windows.UI.Composition brush; needs a Windows.System DispatcherQueue, made once). Nothing opaque
  sits behind the canvas (checked with backdrop none). Don't screenshot acrylic/none windows for others' content:
  PrintWindow shows only our pixels, but keep captures to mica/none.
- `cursor-style-at-prompt`: `SetCursorDefaults(style, blink, atPrompt)` stores them; `ApplyCursorDefaults`
  switches libghostty's DEFAULT_CURSOR_STYLE when the marks' prompt state enters/leaves Prompt|Input
  (`NotePromptState`), so a program's DECSCUSR still wins. Command text read-back
  (`ReadCommandLine`) goes row by row (decoration gap cut per row; soft wraps joined, line breaks a space).
- Default terminal (Settings > Layout, a switch on Windows' state, not config.toml): Windows hands a new
  console to `HKCU\Console\%%Startup` DelegationConsole (an installed Windows Terminal's OpenConsole; Zinc
  doesn't ship that half yet) then DelegationTerminal = Zinc's CLSID (Core `Pty/DefaultTerminal.cs`:
  Enable/Disable remember the replaced value in `HKCU\Software\Zinc`, `Register` = HKCU LocalServer32
  `"exe" -Embedding`, no package; tested on a scratch key). App `TerminalHandoff.cs`: CoRegisterClassObject on
  its own MTA thread at every start (not test runs unless ZINC_DEFTERM_TEST=1); **the COM types must be
  public and top-level** (nested in an internal class, QueryInterface failed and OpenConsole fell back to a
  console window). `ITerminalHandoff3.EstablishPtyHandoff` → Core `ConPty.FromHandoff`
  (ConptyPackPseudoConsole + two anonymous pipes; reading waits for `Begin()`, called by
  `TerminalSession.Attach`: output waits in the pipe while the pane is made) → `MainWindow.OpenHandedOver`
  (`ProfileForHandedOver`: the one profile running the same exe, else a made-up one named after the program with its icon; `TerminalTab.HandedOver`: not saved). Test runs listen as `DefaultTerminal.TestClsid` so the installed Zinc (which always listens as `Clsid`) never takes a test console. `-Embedding` launch: no window until a console
  arrives (20 s, else quit); redirected `-Embedding` launches are ignored. install.ps1 repoints/gives back.
  Test: `.scratch/tools/defterm-test.ps1` (backs up + restores the user's value; never run a COM *launch*
  of the dev exe: without ZINC_CONFIG it would redirect into the installed Zinc). Findings and the
  feasibility spike: .scratch/NOTES.md, .scratch/defterm-spike.
- About's open-source list: Core `OpenSourceNotices` (name, version, use, license, URL, license file)
  + `assets/licenses/*.txt` → `Licenses\` (font/scheme licenses stay in Fonts\/Themes\); App
  `SettingsWindow.About.cs`. `OpenSourceNoticesTests` require every shipped NuGet package, every
  license file and the pinned Ghostty commit to match — a new dependency needs an entry + text, in alphabetical order (tested).
- Palette recents: `RecentCommands` (Core/Commands, by title; one instance on `App`, saved in
  session.json's `recentCommands`) — `CommandFilter.Filter(..., recent)` puts the 5 newest first
  on an empty query and boosts them a little otherwise. Entries that come and go (workspaces,
  tabs) are `Remember: false`. "@" = tab switcher (`MainWindow.TabSwitcher.cs`, `IsTab` entries);
  `test:palette-open:<query>` opens it for captures; `palette:<title>` runs through
  `RunPaletteCommand`, so it counts as recent.
- Palette questions (`PaletteArgument` in MainWindow.CommandPalette.cs; commands in
  `MainWindow.PaletteArguments.cs`): an `AsksArgument` command keeps the palette open and asks,
  with choices (`Highlight` = live preview, `Cancel` on Esc/close) or typed text (`Submit`). An
  answer runs *before* the palette closes, so it can ask again. Color schemes: `ThemeLibrary`
  (Core; the config reader uses it too); a tab's own scheme = `TerminalTab.ColorScheme` + a
  themed copy of its Profile, re-applied on reload and restored from session.json.
  `ShellDirectory.ResolveTyped` turns a typed folder into what `LaunchIn` takes. "New tab
  running…" starts the focused tab's *command* (not its profile's) and types the line via
  `TerminalControl.StartupInput` once the shell is ready (first prompt mark, else 600 ms of
  quiet output, else 6 s). A program that can't start shows why in its tab (no crash).
- First run: no config file (and no command on the command line) → `WelcomeWindow` (its own
  window: its layout choice decides how the main window is built) → `App.ShowWelcome`'s
  Finished writes `FirstRun.Apply` (Core, tested: from `FirstRun.Template` on first run; later,
  in place and only what changed vs the config in force) → first window opens; palette
  "Welcome…" reopens it (then a reload applies; the layout only to new windows).
  `ConfigFile.SchemeName` = the global colors.theme name. A missing config path is how tests get
  a first run (`visual.ps1` captures `welcome.png`, then `welcome-2/3.png` via UIA "Next"). Paged:
  one question per page, Back/Next and page dots (buttons named "Page N of M"); reopened, Save
  is on every page.
- Settings window: Core `Settings/SettingsCatalog.cs` (every setting: section, group, key, kind,
  default, choices; `Problem` validates a value through the real config reader; `FromText`) and
  `SettingsWriter` (ConfigEditor Set/Remove per key, defaults removed, Undo = file text at open),
  both tested — a test requires every `ConfigFile.SettingKeys` key (the reader's `Mappings`
  table) to be in the catalog or `FileOnly`, and every default to equal an empty config's. App
  `SettingsWindow.cs` (code-only, NavigationView, cards generated per kind, `Refresh` on every
  reload, read-only while HasErrors; **UI text follows `docs/ux-writing.md`**); `App.ShowSettings` (one instance; writes reach windows via
  the config watcher). Open: `open-settings` (Ctrl+,), palette, tray. Profiles (session 2): list
  + editor (`_editing`), `Profile.Found/InFile/FoundName/FoundCommand`, `ConfigFile.ProfileValues`
  / `ValueOf(id, key)`, catalog `ProfileFields` + `Discovery`, `OverrideCard` (inherit ↔ own value
  into `[profile.x]`), delete/undo via `ConfigEditor.RemoveTable` (tested; keeps the next section's
  comment). Keyboard (session 3): `SettingsWindow.Keyboard.cs` (rows in `Actions.Order`, chips,
  capture flyout: PreviewKeyDown + `InputKeyboardSource` modifier state, so posted keys can't
  drive it — the user tests it) over Core `Settings/KeyBindingEditor.cs` (tested: default key
  removed = "none", given back = line removed, other spellings edited in place, Reset skips
  sequences; `KeyChord.ToConfig`). Polish (session 4): cards via `CardLayout` (reset arrow beside
  the name = "changed" marker; `SettingChoice.Detail` explains the chosen option under the name);
  profile settings show "Inherited: …" (`OverrideCard`, arrow = inherit); `SchemeCards` (follow
  switch, scheme / "Dark scheme", "Light scheme"); profile list = a ListView of its own
  ListViewItems (drag reorder + ⋯ Move up/down) → top-level `profile-order` (ConfigFile sorts
  `Profiles` by it; tested; writing it pins `default-profile`, else the default would move); undo =
  a floating bar (`ShowChanges`); color picker writes live (150 ms settle timer). Key chips wrap
  (`WrapPanel`). `WindowPlacement`: sizes in DIPs for every window; Settings reopens at its
  saved bounds (session.json `settings`, only with save-state on) or centered over the last main
  window, on its last section during a run (`App._settingsSection`). The sidebar's selection
  pill is a `SlidingPill` (shared with `WorkspaceList`, which slides it only when the selection
  changes within an unchanged list and jumps otherwise; `.scratch/tools/pill-test.ps1` logs its moves;
  `Contrast.AccentOn` keeps it, pane-tab accent lines and focus lines at 3:1 against their surface)
  (`SettingsWindow.Indicator.cs`): NavigationView's is made transparent via
  `NavigationViewSelectionIndicatorForeground` (its own animation is a fixed 600 ms stretch-and-jump,
  see .scratch/NOTES.md); ours takes the SelectionIndicator's *size* but the item's row for its
  position on LayoutUpdated (WinUI still animates its invisible pill, starting it at the old item,
  and TransformToVisual sees that: reading it mid-animation sent ours back) and slides
  (composition Translation.Y from `this.StartingValue`, 250 ms). Undo bar counts settings
  (`SettingsWriter.ChangeCount`, tested). Pass 5 (from the WT study, 2026-09-30): `TextAndControl` (control
  wraps under the text below 440 px card width), `ControlWidth` 280 for dropdowns/text/pickers,
  `Switch` (On/Off label left of a content-less ToggleSwitch), `FocusAfterReset`; `SchemeTiles`
  (`Tile`, `ShowAsTiles` = ItemTemplate of an empty ContentPresenter filled in
  ContainerContentChanging; setting ItemContainer.Content directly showed blank rows) +
  `SchemePicker` (tile button + filterable tile list; flyout `ShouldConstrainToRootBounds=false`,
  so it's a popup window that closes when the window isn't in front: automate it with
  `.scratch/tools/pick-in-flyout.ps1`); colour flyout = scheme's 16 chips (`TerminalTheme.BaseColors`,
  libghostty's built-ins via `TerminalSession.BuiltinBaseColors`) + "Use the scheme's color" + full
  picker. Pass 5 part 2: `SettingsWindow.Search.cs` (results = settings + a named profile's own
  settings + key bindings, words AND-matched; `_cards` registry → `GoTo` (sets section without the nav
  handler via `_navigating`; search box re-renders only on `UserInput`) → `Reveal` (bring into view,
  focus, `Flash` = accent border 2px with padding -1 so nothing moves, ColorAnimation back);
  `InheritedText` = "Inherited from [section link]"). Keyboard groups = `Actions.Categories`/`Category`
  (tested: every action has one); keycaps (`Keycaps`). `DuplicateProfile` (⋯ menu), `BrowseControl`
  (pickers need `InitializeWithWindow`; async click body is try/caught). `[window] software-rendering`
  → `CanvasLifetime.SoftwareRendering` → new canvases' `ForceSoftwareRenderer`. Profiles that can't
  open: `Profile.Source` (`ProfileDiscovery.SourceOf(id)`: the [discover] key an id belongs to) and
  `Profile.Missing` (NotFound / SearchOff; hidden, named by `ProfileDiscovery.NameFor`; tested);
  settings show the reason + `MissingFix` (Delete, or turn the search on), and `FoundBy` lists each
  search's profiles under its switch. Keyboard families: `Actions.Family/FamilyTitle/TakesArgument/
  ArgumentName/Variants/VariantLabel` (tested: every variant round-trips Format/Parse; variants are
  built by Parse, which also fixed WithoutArguments' toggle-sidebar = cycle, Index -1) → `FamilyCard`
  (variants with keys, in variant order, + "Add a key for another …" = `CaptureFlyout(..., variants)`
  with a "For" chooser). The profile list re-renders only when the profiles
  differ from what it shows (`_profilesShown`; a drag already shows its order). Folds
  (`SettingsWindow.Folds.cs`, our own card, not WinUI's Expander, so a fold is one Border for
  search's Reveal/Flash): catalog `SettingsCatalog.Folds` (a lead setting's card as header + the
  settings under it, or a titled bundle with a "Changed: …" summary; tested: neighbors in one group),
  each group of a profile page ("Changed here: …"), key families with more than 3 variants bound.
  Clicks on controls in a header don't fold; `_foldOf` lets Reveal open a fold first; open folds
  stay open for the window's life (`_openFolds`), and slide (Height animation). Live preview
  (`SettingsWindow.Preview.cs`): `TerminalControl.Preview(config, script)` = a session with no
  program, fed a canned VT script, cursor drawn focused, not hit-testable; pinned above the
  scroller (`_pinned` takes the heading + back link) on Appearance and profile pages;
  `ApplyConfig` on every reload; hidden below 560 px of page height or by its Hide/Show button
  (`PreviewHidden`, session.json `settingsPreviewHidden`). `SchemePicker.Highlighted` (pointer or
  keyboard focus; `SingleSelectionFollowsFocus = false`, so arrows don't pick) → `PreviewScheme`
  (config text edited in memory + Parse: explicit colors still win); `HighlightEnded` on a close
  without a pick. `.scratch/tools/focus-in-flyout.ps1` focuses a list item for captures. Reset arrow =
  `UTurnArrow` (Heroicons' arrow-uturn-left shape, drawn as a Path). Tools:
  `.scratch/tools/settings-open.ps1`, `settings-remember.ps1`, `uia-scrollpct.ps1`. Test: `.scratch/tools/find-window.ps1` + `uia.ps1 -Window
  "Zinc Settings"` (invoke toggles/selects nav items), config copy in .scratch/settings-test.
- zn, the CLI (design: claude.ai/code/artifact/1ee17a45-fbe4-4e67-874c-b2bf68270445): `src/Zinc.Cli` = zn.exe
  (Native AOT on publish, net10.0, **compiles Core's `Cli/CliProtocol|CliPipe|CliCommandLine|CliText.cs` in as linked
  source** (no Core reference: keep those four free of other Core types). Core `Cli/`: request/reply JSON lines
  (source-gen), pipe names (`CliPipe`: `<InstanceKey>.cli` per session, `Zinc.p<pid>.cli` without one;
  `SessionStore.DefaultPath/InstanceKey` delegate here), the parser (`zn <thing> <verb>`, per-verb option lists,
  help, send's escapes), list tables, `CliEnvironment` (ZINC_TERMINAL_ID/ZINC_PIPE, zn's folder at the end of PATH,
  ZINC_ZN = zn.exe; WSLENV passes the ids and ZINC_ZN/p: **WSL may not append Windows' PATH** (the user's Fedora has
  appendWindowsPath = false), so the bash/zsh integration scripts define `zn` from ZINC_ZN), `CliLaunch.WithProgram`
  (a `--` program under a WSL profile runs in its distro); all tested in `CliTests`. `TerminalSession.ReadText`
  (terminal read: the formatter gives one line per row, scrollback first, blank bottom rows left out, so the screen
  starts at row Total - Len). `Pty/UserPath` (`[cli] path`: zn's folder on HKCU Environment Path, the entry
  remembered in HKCU\Software\Zinc so moves/off/uninstall take back only it; `InstallCleanup` too; tested on a
  scratch key; never in ZINC_CONFIG runs). App: `CliServer` (pipe thread, ACL =
  current user, network denied; one request per connection; `NewTerminal` hands out terminal ids and sets
  `TerminalControl.LaunchEnvironment`) → `App.Cli.cs` (UI thread: version always, else `[cli] remote-control`;
  `FindCliTarget`: flag > caller's terminal > window used last (`Defaulted` → `Target` note); `CliTree`) →
  `MainWindow.Cli.cs` (commands through CreatePane/Wire/Split/ClosePane/ConfirmClose/BringToPane, so they behave like
  keys). Ids: `TerminalTab.Id`, `TabGroup.Id` (pane), `WorkspaceTab.Id` (kept through `WorkspaceTransfer` when a
  workspace moves window), `MainWindow.Id`, per run. `TerminalTab.CustomTitle` (terminal title; saved in TabState.Title)
  feeds `TerminalTab.Title`, which every title display uses. Shipping: Zinc.csproj's `CopyZn` puts zn's
  framework-dependent build in `bin\` on every build; `PublishZn` runs its own AOT `dotnet publish` into the publish
  folder's `bin\` with vswhere's folder on PATH (**ILCompiler's link step fails without it**; Exec's
  EnvironmentVariables needs the PATH MSBuild-escaped); release.ps1 checks `bin\zn.exe`.
  E2E: `.scratch/tools/zn-test.ps1 [-Zn <exe>]` (own config + session, so its own pipe; 46 checks incl. WSL Fedora).
- `Commands/` — `FuzzyMatch` (in-order, word-start/consecutive scoring, match positions) and
  `CommandFilter` for the command palette (tested). `KeyChord.ToString()` / `Keybindings.ChordFor`
  give shortcut labels.

`src/Zinc` (WinUI app): `MainWindow` = TabView of workspaces in the title bar (ExtendsContentIntoTitleBar,
Mica); each `WorkspaceTab` holds a `Workspace<TerminalTab>` (Core/Panes/Workspace.cs, tested: a
`PaneTree` of `TabGroup`s = panes, each holding terminal tabs). A group's host is a `Border`
(focus border, drop target, `Tag` = group) around a `GroupView` (its strip of tabs, shown per
`tab-groups`/`group-tab-strip`, over the active tab's terminal; strip items are built once
and updated in place). `UpdateFocusBorders` refreshes every group view; call it after any
group change. All rendered into its persistent
`Container` Grid by `Rebuild` (nested Grids + `PaneDivider`); window actions arrive via
`pane.ActionHandler`; `App.OpenWindow` for new windows. `MainWindow.CommandPalette.cs` builds the palette's command
list fresh on open and runs actions through `TerminalControl.Invoke` (same path as keys);
`MainWindow.TabMenu.cs` is the tab context menu. Moving: a workspace leaves a window via
`DetachWorkspace` (hosts released, terminals `Unwire`d, still running) and arrives via
`AddWorkspace` (re-`Wire`s each `TerminalTab`); live TerminalControls re-parent across windows
fine (same UI thread; Win2D keeps its resources; the pane re-subscribes to the new XamlRoot for
DPI). Close the source window *after* the target has the workspace, or the app may quit with
no windows. `workspace-tabs = left`: `MainWindow.WorkspaceList.cs` + `WorkspaceList.cs` — the
TabView stays (holds workspaces, shows the selected one) with its strip (`TabContainerGrid`)
collapsed; the list mirrors `Tabs.TabItems` via `RefreshWorkspaceList` (call it after item,
selection or color changes). Its title row (`MainWindow.TitleRow.cs`: app mark + command
center) sits in the caption `DragRegion`; clickable things there need a `KeepClickThrough`
(Passthrough region rects, physical px; a WM_NCHITTEST probe still answers HTCAPTION there, but
real clicks do pass through — user-verified). `MainWindow.Chrome.cs`: `sidebar` full/rail/
auto-hide and `title-bar` show/auto-hide (config + toggle actions, per window); hidden bars are
overlays revealed by edge hot zones and hidden by a GetCursorPos poll; `test:sidebar-show`,
`test:titlebar-show` (pinned) and `test:chrome-hide`; `.scratch/tools/chrome-shot.ps1`
launches a mode and captures it. Never remove + re-insert the *selected* TabViewItem: its content is
still attached until the next layout pass → 0x800F1000 (`MoveWorkspace` moves the others around
it). `MainWindow.Drag.cs`: dragging workspaces (TabView events or list items) and pane tabs
(`GroupView` items with `CanDrag`; their strip is its own drop target and marks `Handled` so the
pane host below doesn't also take it). `App.Drag` hands the dragged `WorkspaceTab` or
`TerminalTab` over in-process; a drop target clears it, so a completion handler still holding it
knows nobody took the drop (tear-off if the pointer is outside every window). Terminal tabs move
via `TakeTab` + `MoveTerminalTab`. Drag input can't be automated — the user tests it; the logic
behind the drops can be, through `test:drop-middle:N`/`test:drop-right:N` (workspaces) and
`test:tab-to:N`/`test:tab-right:N`/`test:tab-at:I` (the focused tab), plus the menu paths
(`menu:Move workspace to new window`, `menu:Split pane/Move pane to …`) and palette entries
(`palette:Move tab to new workspace`). Quick terminal: layout/hotkey parsing in
Core `QuickTerminal.cs` (tested); `HotkeyListener` (RegisterHotKey on the `MessageWindow`),
`App.ToggleQuickTerminal`, `MainWindow.Quick.cs` (placement, autohide on deactivate,
hotkey-taken InfoBar; rework 2026-10-04: no frame at all (SetBorderAndTitleBar(false, false), IsResizable false, the
classic border styles stripped, DWMWA_BORDER_COLOR none, DWMWCP_ROUND; no ExtendsContentIntoTitleBar: each left a light
strip or outline), the TabView strip collapsed, ClearBackdrop untinted (ApplyBackdrop), center by default; appear =
Root Opacity/Scale/Translation with WinUI implicit transitions from Core `QuickTerminalAppear.Start` (tested), hide =
the reverse then AppWindow.Hide after `animation-duration` (`_fadeOutTimer`). A window region slide was tried first:
it showed a white classic frame. AppWindow.Closing cancelled -> hide unless `App.Quitting` or no tabs left; foreground
handed back to `_previousForeground` on a dismissal (not on autohide); placed twice when the size came out wrong (a
monitor with another scale). One terminal: `NotInQuickTerminal` (tab/workspace actions no-op), `ShowsStrip` false,
palette drops tab/workspace commands. Shadow: comes with DWMWCP_ROUND, so `SetShadow` = square corners while fading,
round once whole (DWMWA_NCRENDERING_POLICY did nothing). Resize (`MainWindow.QuickResize.cs`): no frame, so
`QuickResizeEdge` strips (6 DIPs, ZIndex 5; none against its screen edge) drag it via GetCursorPos deltas -> Core
`QuickTerminalResize.Size` (centered axes grow both ways) -> `Place(work, settings, size)`; remembered per monitor
(OuterBounds key) in Core `QuickTerminalSizes` (session.json `quickTerminalSizes`, valid while `BaseOf(settings)`
matches; tested); double-click an edge / palette "Reset quick terminal size". Drags can't be automated: the user tests.
Test: `.scratch/tools/quick-test.ps1` (summon, WM_CLOSE hides, same session), `quick-size-test.ps1` (a saved size applies). `MessageWindow` is a hidden *top-level* window (message-only windows
miss Explorer's TaskbarCreated broadcast) that logs its hwnd and dispatches WM_HOTKEY and the
`TrayIcon` callbacks. App lifetime: `DispatcherShutdownMode.OnExplicitShutdown`;
`App.OnWindowClosed` decides stay-in-tray vs `Quit()` (tray icon must be removed on exit).
Test a press with `.scratch/tools/quick-toggle.ps1` and lifetime with `lifetime.ps1` (both post
to the logged hwnd; need ZINC_LOG). Don't launch `powershell.exe` consoles
mid-test: they take the foreground and the quick window autohides. `Terminal/`:
- `TerminalRenderer.cs` — fonts, `CellMetrics`, render state → Win2D (glyph runs, fallback, cursor).
  A frame reads every row first (`_frameCells`, one codepoint buffer per frame), draws all backgrounds as one
  cols x rows bitmap (`DrawBackgrounds`, nearest-neighbor, default bg transparent), then each row's matches,
  text, decorations, cursor. Measure drawing changes A/B with `.scratch/tools/ab-bench.ps1`-style runs, each
  benchmark in its OWN pwsh process (`pwsh -File bench-test.ps1`): UI Automation inside one PowerShell session
  fails to find windows after the first run's process exits. Direct2D defers work, so per-section timings move
  between sections: judge by the frame total.
  Text goes through `GlyphAtlas` (Terminal/GlyphAtlas.cs; `ZINC_GLYPH_ATLAS=0` = the old glyph runs): each glyph drawn
  once (tile = 3x2 cells, the glyph's cell in the middle, at the grid's fractional pixel offset; a light and a dark
  variant, Direct2D's grayscale contrast depends on the text color; stored as white coverage; new glyphs are only
  reserved during the frame, `Flush` draws them all on a scratch page and whitens it onto the atlas page with one
  ColorMatrixEffect draw: no GPU read-back, two sessions per frame. NB: CanvasBlend.Copy + an effect drew nothing), then ONE
  `CanvasSpriteBatch` per frame for all glyphs + box-drawing sprites, then decorations and the cursor over it
  (`_batching`; the cursor redraws its glyph directly). Each new glyph is clipped to its tile (some Nerd Font glyphs
  reach ~5% of a cell past it, onto a neighbor's tile); pages grow past 512 px when one tile is bigger; a frame that
  failed halfway leaves nothing behind (sprite lists cleared at frame start, a failed Flush clears the atlas), and
  OnDraw hands a lost device to Win2D (`RaiseDeviceLost`; it can't be faked: Win2D refuses on a healthy device). Characters the font lacks (CJK, symbols,
  clusters) go in the atlas too, rendered once with a DirectWrite layout (`GetText`, white coverage, keyed by codepoint
  or cluster text); emoji (`IsEmoji`: VS16/ZWJ/keycap, wide pictographs) on color pages of their own (`GetColor`, keyed
  by text color too, copied untinted). A layout per cell per frame cost ~205 ms a frame on a CJK/emoji screen
  (benchmark "Wide characters", 2026-10-04) -> 1.2 ms. Capped at `GlyphAtlas.MaxPages` (32, ~1 MB each): past it
  `Trim` (frame start, before Prepare) starts over with what's on screen (logs `atlas: N pages, starting over`).
  Shared between panes (2026-10-04): `GlyphAtlases.Acquire/Release` by (device's native pointer, font, size,
  CellMetrics, sub-pixel offset), ref-counted, UI thread only; a pane whose key changes (zoom, font, scale, device)
  moves to the matching atlas (`ReleaseAtlas` in ClearSprites/Dispose). Logs `atlas: new for ... (N in use)` / `freed`. Still immediate: the bundled box-drawing fallback, the glyph
  under a block cursor. Reset with `ClearSprites`. Pixel diff vs glyph runs: mostly 1/255, colored text edges up to
  ~24/255. Colors workload 9.5 -> 2.9 ms (GPU), 16.7 -> 3.5 ms (software).
  Built-in glyphs (`[font] builtin-glyphs`, default on): box drawing, blocks, Legacy Computing,
  Powerline, Braille come from **Ghostty's own sprite rasterizer** built as
  `native/<arch>/zinc-sprite.dll` (`scripts/build-sprites.sh`, run by build-libghostty.sh:
  `native-src/sprite/zinc_sprite.zig` is the C API, copied into the checkout as
  src/zinc_sprite.zig; the build repoints canvas.zig's one `global.io()` call and restores it).
  Core `Ghostty/Sprites.cs` (Has/Draw → coverage + offset from the cell's top-left, may reach
  past it; skips Ghostty's branch-drawing PUA set) — tested. Renderer caches a white premultiplied
  bitmap per codepoint per cell size and draws each row's sprites in one `CanvasSpriteBatch` tinted
  with the cell color (block cursor redraw too). Line thickness = the font's underline thickness.
  Fallback when off / not a sprite: `DrawBundled` (cell-filling ranges from the bundled Cascadia).
  Test screen: `.scratch/tools/legacy-test.ps1`.
  Default font Cascadia Mono NF ships in `assets/fonts` → `Fonts\` (OFL; variable upright +
  italic files) and is used *uninstalled*: `BundledFonts` loads a `CanvasFontSet(file Uri)` for
  glyph faces and names the file in the text format (`file:///…ttf#Cascadia Mono NF`); an
  installed family of the same name wins (the log says `font: … (bundled)`).
- `TerminalControl.cs` — one pane: session lifecycle, grid layout, blink, focus.
  `.Keyboard.cs` (keybindings → actions, KeyDecision → encoder / WM_CHAR path, clipboard),
  `.Mouse.cs` (mouse reporting, selection, wheel, pointer shape), `.Scrollbar.cs` (overlay
  ScrollBar synced from `GetScrollbar()` each frame; `ScrollToRow` uses the same units).

`src/Zinc.Native.targets` — imported by every *runnable* project (app, tests): copies
ghostty-vt.dll and references the ConPTY package (its OpenConsole copy step is in build/,
not buildTransitive/, so it must be referenced directly, not via Core).

`tests/Zinc.Tests` — `AbiTests` (structs/enums/exports vs manifest; new binding structs
and enums are picked up automatically, enums must be added to the `Enums` map), `SessionTests`
(real cmd.exe on ConPTY, screen read back via formatter), unit tests.

Threading: PTY output is parsed on the ConPTY reader thread; UI thread renders and encodes
input. **Every libghostty call touching the terminal must hold `TerminalSession.Lock`.**
libghostty callbacks (write_pty, title, bell, size) fire *during* vt_write/resize, i.e. on
whatever thread holds the lock — don't call back into the terminal from them; set flags
and read state after the write (see `_titleDirty`).

## libghostty-vt footguns

- Bumping the commit also rebuilds zinc-sprite.dll (Ghostty's sprite code is internal, not
  API: if build-sprites.sh fails, look at the sprite Face/canvas/Metrics changes; SpriteTests pin
  the behavior we rely on).
- The C API is unstable. After bumping the commit: rerun gen-keys.py, then `dotnet test`
  (AbiTests checks every struct/enum/export against `ghostty_type_json()`). Structs use
  explicit offsets; C `bool` is mapped to `byte` to stay blittable. A struct field that
  flattens a C union needs `[AbiField("value.coordinate.x")]` so the check can find it.
- The manifest describes types only, not function signatures — argument types/order of
  LibraryImports are still unchecked; only that each export exists.
- Sized structs (`GHOSTTY_INIT_SIZED`) need `.Size = sizeof(struct)` set before get calls.
- `ghostty_terminal_set` takes callbacks/pointers **directly** as the value (not pointer-to).
- Features silently do nothing unless their callback is installed. Known cases:
  `OPT_WRITE_PTY` (all query replies — ConPTY waits on a DSR reply at startup!),
  `OPT_SIZE` (XTWINOPS 14/16/18t and the mode-2048 report on enable; missing it broke
  SGR-pixel mouse in flow). Check the option list in terminal.h when a feature "doesn't work".
- `ghostty_render_state_get(..., ROW_ITERATOR, &iter)` / `row_get(CELLS, &cells)` rebind
  *pre-created* handles; pass the address of your existing handle.
- `ROW_CELLS_DATA_FG_COLOR/BG_COLOR` return resolved RGB or `INVALID_VALUE` for "default".
- Grid refs are untracked: only valid until the next terminal mutation. Resolve ref →
  run selection gesture → `OPT_SELECTION` all under one lock.
- `ghostty_paste_encode` mutates its input buffer in place.
- Alloc-style APIs (`formatter_format_alloc`, `selection_format_alloc`) return (NULL, 0) for
  empty output — guard before `Encoding.GetString`.
- Grapheme clustering (mode 2027) is off by default; we enable it only with the bundled
  ConPTY, which is created with `PSEUDOCONSOLE_GLYPH_WIDTH_GRAPHEMES` so widths match.
- For quick API experiments, Python + ctypes against `native/x64/ghostty-vt.dll` is the
  fastest loop (no app rebuild); see `.scratch/NOTES.md` for examples.

## Build / test footguns

- .NET 10 `dotnet test` needs `global.json` `{"test":{"runner":"Microsoft.Testing.Platform"}}`
  (xUnit v3 is MTP-based; the VSTest path errors out).
- Zinc.slnx maps every project to `x64` explicitly. Without that mapping, `dotnet build`
  (solution) built into `bin\Debug\` while project builds used `bin\x64\Debug\`, and the
  exe we launched for testing was silently stale. If an app change "doesn't take", check
  the timestamp of Zinc.Core.dll next to the exe you launched. ARM64 is deliberately
  not in the solution until native/arm64 exists.
- Solution builds can pass `Platform=AnyCPU` as a *global* property, which a csproj's
  Platform default can't override; WinAppSDK self-contained then fails ("requires a supported
  Windows architecture") unless a RuntimeIdentifier is set — hence the `win-x64` RID
  fallback in the app and test csprojs.
- Long inline heredocs mixing Python and shell quoting can fail to parse and silently
  apply nothing; put multi-edit scripts in a file under .scratch/ instead.
- `dotnet publish` of an unpackaged WinUI app omits `Zinc.pri`/`.xbf` unless
  `EnableMsixTooling=true` → the published exe dies at startup with 0xC000027B in
  Microsoft.UI.Xaml.dll. `bin/` builds don't show this; smoke-test a *publish* after
  csproj changes (scripts/install.ps1 publishes).
- Reference the WinAppSDK component packages (WinUI/Foundation/InteractiveExperiences),
  not the `Microsoft.WindowsAppSDK` metapackage: 2.x's metapackage adds AI/ML/Search/Widgets
  (onnxruntime, DirectML) — 165 MB vs 105 MB published. Keep versions in step with the
  metapackage release they came from.
- Publishes are trimmed (`PublishTrimmed`, `TrimMode=partial`: only assemblies marked trimmable, i.e. .NET and the
  Windows SDK projection; ours, Tomlyn, Velopack stay whole; 2026-10-03 size audit: 200 -> 95 MB installed, Setup 85
  -> 49 MB). `BuiltInComInteropSupport=true` is required (trimming turns built-in COM off: `[ComImport]` users threw
  at startup). Reflection over framework types (JsonSerializer without source gen, etc.) can break only in a publish:
  check `warning IL` in a publish log, and test a publish, not bin/. `DropUnusedFromPublish` leaves out the .NET
  debugger's files and WebView2 (bring it back if a tab ever shows web content). `.scratch/size-audit.py <dir>`.
- CHANGELOG.md (user-facing, docs/ux-writing.md voice; newest first; `## Unreleased` then `## 0.1.0-dev.N (date)`):
  add to Unreleased with each user-visible change. Releasing: rename Unreleased to the version the NEXT commit makes
  (`git rev-list --count HEAD` + 1), add a new empty Unreleased, commit, then `release.ps1 -Publish` (refuses without an
  entry for its version or with anything left under Unreleased; passes the entry to vpk `--releaseNotes`; `-Changelog`
  for tests). Core `ReleaseNotes` (Entry/Newest/Pieces, tested incl. the real file); `Updater.NewNotes` -> About's
  "What's new in X" dialog. Test: `.scratch/tools/update-notes-test.ps1` (Zinc.Test packages, own changelog).
- Tester builds = `scripts/release.ps1` (below; package.ps1, the zip-only script, is gone): `dotnet publish
  --self-contained true` after clearing the app's obj\x64\Release (the .NET runtime inside; install.ps1 publishes
  self-contained too), `README.txt` from `scripts/package-readme.txt` ({version} filled in). ~197 MB installed /
  77 MB packed (.NET + WinAppSDK; language folders are tiny). Smoke-test with ZINC_CONFIG set to a fresh path
  (else it redirects into the user's Zinc). **install.ps1 and release.ps1 take the `Local\ZincPublish` mutex (they
  share obj\).** A self-contained publish right after a framework-dependent one reused its zinc.deps.json, which
  names no runtime: zinc.exe then dies with "Could not resolve CoreCLR path" (Application event log, nothing on
  screen); the first zip sent to a tester was built so. release.ps1 refuses a deps.json without
  `runtimepack.Microsoft.NETCore.App.Runtime`.
- Installer + updates (Velopack 1.2.161; plan: claude.ai/code/artifact/33953d1b-1792-4f70-aeef-07dc7d80bcb0):
  `scripts/release.ps1` (refuses a dirty tree unless -AllowDirty; `-UpdateUrl` is built in via the `ZincUpdateUrl`
  MSBuild property → AssemblyMetadata, required for the `Zinc` id; publish under the mutex above;
  `dotnet vpk pack` → `<id>-win-Setup.exe`, full/delta nupkg, `releases.win.json`, portable zip; -From fetches the
  previous release for deltas; `vpk` pinned in root `dotnet-tools.json`, keep it equal to the PackageReference).
  Package id `Zinc` installs to `%LOCALAPPDATA%\Zinc` (Velopack replaces/removes that whole folder on
  install/uninstall), so **everything of the user's lives in `%APPDATA%\Zinc`** (Core `AppFolders`: logs, crash
  log, icon cache; `MoveOldLocalData` moves the old Local ones once, after the single-instance check, not in
  test runs). `Program.Main` starts with `VelopackApp...Run()`; `BeforeUninstall` → Core `Pty/InstallCleanup`
  (sign-in entry, default terminal: only what points into the install root; tested on a scratch key).
  `Updater` (App, UI thread): `ZINC_UPDATE_URL` (folder or URL) else the built-in address; only copies the installer
  put in place update (`UpdateManager.IsInstalled`, else `UpdateStatus.NotInstalled`). `[updates] check` (default on)
  → `Schedule`: first check 1 min after start, then daily; `CheckAsync` downloads a newer version → `Ready` →
  `MainWindow.Updates.cs` notice (not in the quick terminal; Restart now / Later = `DismissNotice`) and About's
  Updates card (`SettingsWindow.UpdatesGroup`, live via `Updater.Changed`). Installing always goes through App's
  quit: `RestartNow` = `AskQuit(restartForUpdate: true)`; `Quit` calls `ApplyOnExit` last thing
  (`WaitExitThenApplyUpdates`: Update.exe waits for the process to end, installs, restarts if asked), so any quit
  installs a ready update. `test:update` (check), `test:update-restart` (Restart now). An installed copy has its
  own app identity (Velopack's AppUserModelID), so AppInstance never hands launches between an installed copy and
  a zip/dev copy (copies of one install still do): `Program.OlderCopyRunning` (installed copy + the session's lock
  already held + a zinc.exe outside the install) shows a message and exits, instead of a second Zinc without the
  session. A silent Setup.exe doesn't start the app (the interactive one does). Tests:
  `.scratch/tools/update-roundtrip.ps1` (packs `Zinc.Test` test.1-3 with no shortcuts into .scratch/feed,
  installs silently into .scratch/update-test; A: Restart now, B: Later + palette Quit; uninstall; checks the real
  registrations are unchanged), `updates-card-shot.ps1`, `oldcopy-test.ps1`. **Never
  install a `Zinc`-id package on this machine** (it would take the dev install's Start Menu shortcut, sign-in
  entry and default terminal), and run every Setup/Update.exe with ZINC_CONFIG set: the installer starts Zinc,
  which would otherwise hand itself to the user's running Zinc.
- Installed copies live in `%LOCALAPPDATA%\Programs\Zinc\versions\<stamp>-<version>` (install.ps1
  computes the version like Directory.Build.props; the timestamp keeps names sortable); the
  user's Start Menu shortcut points at the newest. A post-commit hook (if installed)
  reinstalls in the background after every commit — including Claude's; log at
  `.git/zinc-install.log`. Installed zinc.exe processes are the user's own
  windows: never kill `Get-Process zinc` blindly — filter by Path to the dev bin/.

## Windows / WinUI / ConPTY footguns

- WinUI 3: `FontWeights` is `Microsoft.UI.Text.FontWeights`; `Colors` is `Microsoft.UI.Colors`.
- TabView's style defaults to `VerticalAlignment=Top`: without `Stretch` it's only as tall as
  its strip and tab content gets 0 px (panes never start their session).
- TabView unloads hidden tabs' content and reloads it on selection. Never tie session
  lifetime to `Unloaded` — panes are closed explicitly by their window (`CloseTab`,
  `Closed`); `Loaded` handlers must tolerate running more than once.
- Flyouts/menus are separate popup windows: PrintWindow of the main HWND won't show them.
- TabViewItem applies Horizontal/VerticalContentAlignment to its *header*: Stretch there
  pushed the label 4 px above the icon.
- Never set TabViewItem.Header/IconSource to new values: each set replays the header's content
  transition (label "slides in") and re-decodes the icon. Tabs use a persistent `TabHeader`
  (one TextBlock + one pre-decoded icon element per profile, toggled by Visibility).
- The tab strip needs `DragRegion.MinWidth >= AppWindow.TitleBar.RightInset/scale`, or tabs
  and +/▾ slide under the caption buttons instead of shrinking/scrolling.
- TabView's inner `TabListView` has entrance transitions; new tabs slid in after their
  terminal rendered — `DisableTabAnimations` clears them.
- Per-item lightweight styling (tab color) set after the template applied only takes effect
  after flipping the item's RequestedTheme (Light → Dark) to re-resolve ThemeResources.
- `FrameworkElement.Parent` is null for children added to a Panel in code — don't use it to
  detach reusable elements. Pane hosts are reused across layout rebuilds, so `Rebuild`
  dismantles the old Grids first; adding an element that still has a parent fails fast
  inside XAML (0x800F1000 in combase, no managed exception).
- Set a tab's content once (a Grid) and rebuild inside it: a ContentControl's old content
  isn't detached until its next layout pass.
- Panels (Grid) have no OnPointer* overrides — subscribe to PointerPressed etc.
- Standalone ScrollBar: `IndicatorMode.None` *hides* it (it doesn't mean "full bar"). Widen with
  `VisualStateManager.GoToState(bar, "Expanded"/"Collapsed")` (what ScrollViewer does); GoToState
  returns false until the template has applied. Expansion is driven by a right-edge hot zone as
  wide as the expanded bar (16 DIPs, Windows Terminal's) from a handledEventsToo PointerMoved
  handler (the bar handles its own pointer events); held down (thumb drag, paging) it stays
  expanded until release, even off the pane. Look: `Terminal/TerminalScrollBar.xaml`, a copy of
  WinUI's DefaultScrollBarStyle with a few substitutions (its header says which) — its sizes are
  StaticResources, which per-bar Resources can't override. `test:scrollbar-expand` pins it open
  for captures (`.scratch/tools/sb-shot.ps1`; WT reference shots in `.scratch/tmp/wt-scrollbar-*`).
- **A pointer shape that flickers back to the arrow is (almost always) not Zinc** (2026-10-04, hours spent): any window
  change anywhere (Task Manager's once-a-second list redraw!) makes Windows refresh the pointer, and every WinUI 3 window
  answers with the arrow until the next pointer move, with no WM_SETCURSOR to it. (Quitting Zinc "fixed" it: Task
  Manager's Details view was filtered to zinc, and with no rows left it stopped redrawing.) A bare WinUI app
  (`.scratch/cursor-demo`, `CursorDemo.exe watch` logs cursor changes + other apps' WinEvents in one file) shows the same.
  The quick terminal's edge drag through XAML showed the arrow from the press on (the divider's doesn't: unknown why),
  and setting the shape after each step, layout pass or frame lost to it. A Win32 SetCapture drag got no moves at all
  (WinUI's pointer input doesn't come as window messages, even to the capture window). Windows' size loop doesn't
  fit either: WM_NCLBUTTONDOWN with an HT border code ends at once (no resizing border), and SC_SIZE runs the
  click-to-finish form (Windows' key state never sees WinUI's press), following the mouse only once the button is up
  (the pointer did hold steady in it). What works (`QuickFrame`): a real resizing frame (IsResizable + WS_THICKFRAME) drawn nowhere (WM_NCCALCSIZE -> 0: no light strip, no margins) + `InputNonClientPointerSource` Left/Right/Top/BottomBorder regions (corners come by themselves; probe with quick-nchittest.ps1 -Position top): Windows' own pointer and press-drag loop, each WM_SIZING step our rectangle, WM_NCLBUTTONDBLCLK on a border = reset. The frame must not paint: WM_NCPAINT -> 0 and WM_NCACTIVATE with lParam -1 (else a white hollow frame showed around the fade on a monitor's first show and after losing focus). Win+arrows snap any resizable window: WM_WINDOWPOSCHANGING outside `QuickFrame.Placing` (our `PlaceQuick`) or a border drag gets SWP_NOMOVE|NOSIZE (quick-resize-churn.ps1's outside SetWindowPos checks it). Palette "Trace the pointer (15 s)" (logging on) = `PointerTrace`.
- Clearing WinUI's top-level class cursor (the arrow) didn't stop non-arrow pointers flickering and left stale shapes
  (a resize arrow after a dropdown in Settings): don't. `.scratch/tools/quick-nchittest.ps1` shows class cursors, child
  rects and WM_NCHITTEST answers.
  `InputNonClientPointerSource` border regions FailFast (8000FFFF) on a window that isn't resizable; fine on a resizable one.
- Hover and pointer shape can't be tested here (posted WM_MOUSEMOVE doesn't become WM_POINTER;
  screenshots omit the cursor) — ask the user.
- Caption buttons: keep `TitleBarHeightOption.Standard` (32 px) with TabView's default ~41 px strip —
  the user's choice, matching File Explorer (short buttons, tabs on the strip's bottom edge).
  Tall (48 px) hung over the terminal; Tall + a 48 px strip was tried and reverted at the user's
  request. Don't describe other apps' UI from memory — ask for or check a screenshot.
- The title bar's empty area (`DragRegion`, via `SetTitleBar`) is caption (non-client): Windows
  refuses drag-and-drop there (the user saw the "not allowed" cursor). It can't be released
  temporarily: `InputNonClientPointerSource` Clear/SetRegionRects(Caption) and
  `SetTitleBar(0x0 element)` all left the caption rect in place (verified with GetRegionRects
  and WM_NCHITTEST). Tab drops are accepted over the tabs and +/▾ only.
  Once the title bar element is *collapsed*, though, WinUI stops re-asserting it and
  `ClearRegionRects(Caption)` does free the area (auto-hidden title bar, `MainWindow.Chrome.cs`);
  after revealing it, call `SetTitleBar` again *once it's in place* (after the slide), or the
  row isn't draggable. Check both with WM_NCHITTEST: 2 = drag, 1 = client, 20 = close.
- Alt+character chords beep unless `MenuKeySuppressor` swallows WM_SYSCOMMAND/SC_KEYMENU:
  handling KeyDown doesn't stop DefWindowProc's menu-mnemonic lookup (no menu bar → beep).
  Alt+Space (system menu) and a bare Alt tap are passed through.
- Focus is the window's job (`FocusActive`, low-priority dispatch after layout); panes must
  not grab focus on Loaded, since every rebuild re-loads every pane.
- App icon: `src/Zinc/Assets/Zinc.ico` = the brand's `assets/brand/icon/quadrant.ico` (docs/brand.md §7:
  hand-placed whole-pixel frames 16-64, vector master 96+; every frame verified identical to its PNG master,
  never resample them). The old SVG pipeline (gen-icon.py) is gone. `AppWindow.SetIcon` rescales one frame
  (smeared at 16px); `WindowIcon.cs` uses LoadImage + WM_SETICON at the DPI's exact small/large sizes instead.
- **An exception in a `DispatcherQueue` callback or a `DispatcherQueueTimer.Tick` ends the
  process on the spot, unlogged** — it bypasses `Application.UnhandledException` (found with
  `test:throw`). Queue work with `SafeEnqueue` and ticks with `OnTick` (`Ui.cs`: `Ui.Guard`
  records + shows + survives); code after an `await` in an `async void` resumes the same way, so
  guard it too. Native callbacks (`UnmanagedCallersOnly`, the `MessageWindow` WndProc, ConPty's
  reader/exit threads) catch everything themselves. XAML event handlers do reach
  `App.UnhandledException`, which now marks them handled and shows the ErrorNotice. Every
  escaped exception goes to `Log.Crash` → `%APPDATA%\Zinc\crash.log` (always, even
  without ZINC_LOG; `ZINC_CRASH_LOG=<file>` for tests). Code reachable after a window
  closes checks `_windowClosed` (its AppWindow is null); `Closed` stops the timers and unwires
  every terminal first. Config/theme reads share access and retry (an editor mid-save); an
  unreadable config counts as HasErrors (report, keep settings).
- Keep every `DispatcherQueueTimer` in a field: one only a local references is garbage-collected
  and silently stops firing. The session save timer did this — saves stopped after the first GC,
  which short test runs never reach (`test:gc` forces one).
- A new `CanvasRenderTarget` can hold recycled GPU memory (an old one's pixels): clear it before drawing
  into it with normal blending. The glyph atlas didn't, and zooming garbled text with the previous size's tiles.
- **Nothing in a frame may pump messages.** A Win2D CanvasTextFormat naming a font *file* (`file:///…ttf#Family`, the
  bundled Cascadia) resolves it through Windows.Storage on its first layout: a COM call that pumps, and a pointer
  message pumped inside XAML's render tick is a FailFast (CXcpDispatcher::CheckReentrancy, 8000FFFF, no managed
  exception, nothing in crash.log; WER dump in %LOCALAPPDATA%\CrashDumps, read with cdb `.ecxr; kn`). So formats are
  realized in SetFont (`GlyphAtlas.PrepareFormat`, shared and never disposed) and never changed per draw (`_centered`).
  Also: a frame can run *inside* SetFont's own pump (sprites keyed by CellMetrics for that), and the atlas key's
  sub-pixel offset is rounded to 1/64 px (float noise made a new atlas per window size).
- `await` is illegal inside an `unsafe` class → async code lives in `ClipboardText`.
- Keyboard: a key's WM_CHAR arrives after its KeyDown and before its KeyUp. Suppress the
  char only when KeyDown already encoded the key (`_charHandledByKeyDown`), and clear the
  flag on KeyUp — otherwise WM_CHAR-only input (on-screen keyboards, automation) is eaten.
  Forward unsuppressed control characters verbatim.
- AltGr arrives as Ctrl+Alt; printable Ctrl+Alt text is treated as text.
- A popup that commits on Enter's KeyDown (rename box, palette) hands focus to the terminal
  *before* that key's WM_CHAR arrives, and the char-only path forwarded the `\r` (ran the
  prompt's line). The terminal notes Enter/Escape/Tab/Backspace held at GotFocus
  (`_foreignKeys`) and drops their char and release; a KeyDown here makes the key ours again.
  Posted input doesn't set key state, so automation is unaffected.
- ConPTY package (`Microsoft.Windows.Console.ConPTY`) decides which OpenConsole.exe to copy
  from `$(Platform)` in its .props, which runs before our csproj sets Platform — hence
  the explicit `ConptyRequires*Host` properties in Zinc.csproj. If `x64\OpenConsole.exe`
  is missing from output, ConPty silently falls back to inbox kernel32 ConPTY.
- ConPTY does not rewrite mouse input: SGR and SGR-pixel sequences reach WSL byte-for-byte.
- Throughput: MSYS/Git Bash programs write one console call per line → ~200k tiny pipe
  writes for `seq 1 200000` (~10 s, Zinc idle waiting). Bulk writers are fast
  (`cat` 44 MB ≈ 0.5 s; libghostty parses ~500 MB/s). Measure reader blocked-vs-busy time
  before optimizing rendering.
- Fallback glyphs (DirectWrite layout) use color fonts only for wide cells or clusters
  with VS16 (U+FE0F); otherwise text-presentation symbols like U+2714 turn into emoji.
- Nerd Font "NF" icons intentionally overflow their single cell (the "NFM" variant
  doesn't); same as Windows Terminal.
- ~190 native threads at idle is normal here (GPU driver/framework; .NET pool is 0).
- Children inherit Zinc's environment. Claude Code's shell sets `NO_COLOR=1` — clear it
  when launching Zinc for tests or everything renders colorless.

## Testing without a human (see `.scratch/tools/`)

- Rendering regressions: `.scratch/tools/visual.ps1 -OutDir <dir>` captures the render test
  scripts under a pinned config (`.scratch/visual-config`); `.scratch/venv/Scripts/python
  .scratch/tools/visual-diff.py <before> <after>` compares pixel by pixel (title bar ignored:
  its active/inactive look is noise). Capture a baseline *before* touching rendering code.
  It also captures `splits` and `tabs` layouts (driven by ZINC_TEST_KEYS) for layout work.
  Runs are deterministic because visual.ps1 sets `ZINC_TEST_DETERMINISTIC=1` (`TestHooks`:
  no cursor blink, cursor drawn as focused, no Mica — each of those once made two captures of
  the same build differ). Verify determinism by capturing twice when adding scenarios.

- Screenshot ONLY our window with `PrintWindow(PW_RENDERFULLCONTENT)` (`shot.ps1`).
  **Never** `CopyFromScreen` — it captured another app's private window once.
- **Never** use SendKeys / SendInput (they hit whatever window is focused). Post messages
  to our own `InputSiteWindowClass` child instead (`keys.ps1`, `win.ps1`).
- PrintWindow doesn't capture the mouse pointer, so pointer-shape changes need the user.
- UI actions (tabs, buttons, menus): `.scratch/tools/uia.ps1 -ProcessId N -Action
  list|invoke|select` drives our window via UI Automation — no mouse, no focus stealing.
  Give buttons an `AutomationProperties.Name` (also needed for screen readers).
  Keybindings with modifiers can't be posted (no modifier state). `ZINC_TEST_KEYS=
  "alt+shift+plus,alt+left"` replays chords (1/s) through the real keybindings and the focused
  pane's `Invoke` (pane actions, then the window's; see `MainWindow.ReplayTestKeys`; logs to
  ZINC_LOG). `uia.ps1 -Action invoke` also toggles ToggleButtons.
  The chord→action map itself is unit-tested; the user confirms real key delivery.
  Entries like `menu:Split pane/Split right` run that workspace context-menu item's command
  (context menus can't be opened by posted input or UIA), and `palette:<exact title>` runs a
  command palette entry. `ctrl+f24` (unbound) works as a one-second pause.
- To check a pane keeps updating, give it output that changes every line (a counter:
  `bash -c 'i=0; while true; do i=$((i+1)); echo tick $i; sleep 0.25; done'` via wsl.exe).
  `ping -t` once misled me: its lines are identical, so a full pane scrolling looks frozen in
  a pixel diff (only the scrollbar moves). `.scratch/tools/freeze-repro.ps1` replays the one
  real freeze (below).
- **Win2D's CanvasControl counts Loaded/Unloaded and draws only while loads exceed unloads.**
  XAML reports an element's removal *twice* if it's taken out of a subtree that was itself
  just detached (e.g. `Dismantle` removes a pane host, then the pane's shown terminal is
  swapped) — the canvas then never draws again, even on resize (user-visible "frozen pane").
  So: change what a pane shows *before* detaching layouts (`Rebuild` calls
  `UpdateFocusBorders` first; `DetachWorkspace` releases hosts before `Dismantle`), and
  `TerminalControl` mirrors the count and swaps in a fresh canvas if a Loaded leaves it ≤ 0.
  Out-of-order pairs (a moved pane's Unloaded after its new Loaded) are harmless.
- **Never let a CanvasControl be freed while Win2D has it registered with a window.** Win2D
  registers for XamlRoot.Changed on *every* Loaded and unregisters only when an Unloaded brings
  its count to exactly 0 — not on destruction. Unbalanced events are common here (tab/pane moves),
  so a replaced or closed canvas freed by the GC left a dangling handler: the next minimize/restore
  hung the app (user's dump) or crashed in Microsoft.Graphics.Canvas.dll (reproduced).
  `Terminal/CanvasLifetime.cs` creates every canvas, mirrors Win2D's count/registration, and holds
  retired canvases until they're unregistered (logs unbalanced counts with ZINC_LOG). Repro:
  `.scratch/tools/canvas-stress.ps1` (uses `test:gc`; `-Exe` to run another build).
- Screenshots are downscaled when viewed; before claiming a rendering defect (gaps,
  misalignment), crop + upscale or scan pixel columns with PIL (installed). A "gap" in
  box drawing once turned out to be anti-aliasing seen at low resolution.
- Posted WM_CHAR/WM_KEYDOWN and WM_MOUSEWHEEL work; posted WM_LBUTTON* do NOT reach
  WinUI pointer events (it uses WM_POINTER). Posted keys don't update GetKeyboardState,
  so modifiers look released. Verify mouse/selection logic by replaying libghostty calls
  in ctypes, and ask the user to confirm real mouse behavior.
- WSL probes: `wsl --distribution-id <id> --exec script` (plain `--` goes through the
  login shell, zsh, which splits on `;`). Use `timeout --foreground` when reading the tty
  (otherwise SIGTTIN stops the reader). In Git Bash set `MSYS_NO_PATHCONV=1` before passing
  `/tmp/...` paths to wsl.exe.
- PowerShell tool quirk: `Remove-Item Env:X` next to quoted `C:\Program Files` paths was
  blocked as a "system path" removal; use `$env:X = $null`.
- Writing C# via Python string literals mangles `\n`, `\r`, `\x1b`, `\\` into real control
  characters — this has bitten three times (C# "Newline in constant" errors), and Python
  replace scripts also fail to *match* lines containing backslashes. Python edit scripts are
  fine for plain code; anything with a backslash goes through the Edit tool.
- The repo mixes LF and CRLF files (`git ls-files --eol`). Python's `read_text`/`write_text`
  (or `open` without `newline=""`) rewrites every line ending to CRLF, turning a small edit
  into a whole-file diff; Git Bash's `sed -i` does the opposite (CRLF → LF). Pass
  `newline=""` in Python, prefer the Edit tool for CRLF files, and check `git diff --stat`.
