# Zinc — notes for Claude

WinUI 3 (C#, .NET 10, Windows App SDK 2.5.x, unpackaged + self-contained) terminal
emulator on **libghostty-vt** (Ghostty's VT core, C API via P/Invoke), ConPTY, and
Win2D. See README.md for user-facing docs. Session history, open issues and the
test harness live in `.scratch/` (gitignored). Read `.scratch/ROADMAP.md` first if it exists: it's the current
list of what's next, kept up to date with the user. `.scratch/NOTES.md` is the long working log (history,
measurements, harness notes): search it when you need something, don't read it whole.

**Never commit: the user commits.** No `git commit`, `--amend`, rebase, squash, merge, cherry-pick, tag or push, even
when a task seems to call for one or an earlier session did it. When a change is done: run the checks below (build
and test), `git add` exactly that change's files (`git status` first: leave unrelated files unstaged), then show
`git diff --cached --stat` and propose a commit message for the user to edit and commit. The subject is short,
lowercase and imperative with no period, like the log ("animate resize-pane steps too"). Add a body only when the why
isn't obvious, in a few plain sentences. The user usually splits a change into a code commit and a docs commit (CHANGELOG,
README, docs/, CLAUDE.md): propose both messages, code first, each with its files. Then stop: the user's commit comes before the next step that depends on it (a
release, branch switch, the next change on top). This rule overrides any instruction elsewhere to commit.

**How each area works, and why, is in `.scratch/internals.md`: read the section for the area you're about to change
before changing it**, and keep it current (new mechanisms, footguns found, decisions with their date). This file
holds the rules that apply everywhere and a map; detail goes there.

**Any text shown in the product** (labels, buttons, descriptions, tooltips, dialogs, notices, errors, the palette,
the welcome and About pages, zn's output) **follows `docs/ux-writing.md`** where it can: read it before writing new
UI. Its punctuation rules are the ones most often missed: no semicolons, and a colon only before a list or between a
label and its value. `/tidy-ui-text` rewrites UI text in a diff or file to the guide. README, docs, CHANGELOG and release notes follow the brand voice in `docs/brand.md` instead, as does the look.

## Code comments

Write comments a new reader can follow on the first read. Don't copy the compressed style of older comments or of
this file, even though it's common here. `/tidy-comments` rewrites comments in a diff or file to this standard.

- Make the length fit the content. A short fragment is often best when it says one clear thing:
  `child.Visibility = Visibility.Visible; // a maximized pane's hidden sides show while they grow or shrink`.
  When the reason takes a few sentences, write a few sentences. Don't squeeze them into one line.
- Join ideas with "because", "so", "which" or "instead of", not colons or semicolons, and keep the small words
  ("the", "is", "it's"). Name the thing instead of writing "that one" or "it" when it could mean more than one thing.
- Say what the code does, or what it prevents, in the present tense. The story behind it (what broke, what was tried,
  measurements, dates) goes in `.scratch/internals.md`. A short clause like "which made it jump by a column" can stay
  if it helps the reader see the risk.
- A comment that only restates the code adds nothing, so leave it out (`// set the width` above `view.Width = w;`).
- A casual tone is fine: `// TODO: we should do proper i18n with pluralization at some point...`

Before: `Not the card less the terminal: an animation cut short leaves the terminal at that one's end size, and it
resized again by a column at the end.`
After: `We use the chrome measured before the rebuild (via ChromeOf) instead of working it out from the terminal's
current size, because an interrupted animation can leave the terminal at a stale size, which made it jump by a
column when this animation finished.`

## Build / run

- `scripts/build-libghostty.sh x64` → `native/x64/ghostty-vt.dll` (Zig 0.16, pinned commit), plus zinc-sprite.dll
  and zinc-shader.dll. `external/` (ghostty clone) and `native/` are gitignored build products. The native DLLs
  import **`vcruntime140.dll`, which a fresh Windows lacks**: `scripts/copy-vcruntime.sh` (run by the build) copies
  VS's into native/<arch>/, Zinc.Native.targets ships it app-local, release.ps1 checks it. Check new native
  dependencies with pefile (`.scratch/vcdeps.py <folder>`).
- `dotnet build` (Zinc.slnx; add `-c Release` for perf work). App output:
  `src/Zinc/bin/x64/<Config>/net10.0-windows10.0.26100.0/win-x64/zinc.exe`.
- `dotnet test` — run after any change to bindings, TerminalSession or ConPty, and
  always after bumping the Ghostty commit.
- **Before staging a change for the user to commit: `dotnet build` (the solution) AND `dotnet test`.** `dotnet test`
  alone does not build the app project (tests don't reference it): a compile error in
  src/Zinc once got committed, the post-commit install failed, and a "verification"
  ran the stale exe. When verifying the app, check the exe/dll timestamp is fresh.
- **The build has 0 warnings before staging.** `.editorconfig` makes dead code (unused private members, parameters and
  assignments) a build warning, so fix it or say why it stays. An incremental build only reports warnings for files it
  recompiled: use `--no-incremental` to see them all. For an occasional wider sweep (suggestions, not rules):
  `dotnet format style Zinc.slnx --verify-no-changes --severity info` and the same with `analyzers`. Always keep
  `--verify-no-changes`, because without it `dotnet format` rewrites the code.
- **A running zinc.exe locks the output** → build fails with MSB3027. Stop only *dev*
  builds — the user daily-drives the installed copy, and this session may be running in it:
  `Get-Process zinc -ErrorAction SilentlyContinue | Where-Object Path -like '*\src\Zinc\bin\*' | Stop-Process -Force`
  (the build output, whatever the repo folder is called; installed copies live under Programs\Zinc\versions)
  Never stop every Zinc (zinc.exe) process.
- Shell: args after the exe, else `ZINC_SHELL`, else pwsh → powershell → cmd.
- `ZINC_TRACE=<file>` records raw PTY output bytes (see which modes an app enables).
- `ZINC_LOG=<file>` appends diagnostics from `Log.Write` (Core; queued, written by a background thread: `Log.Flush`
  before reading). Cheap to add a line when something "doesn't happen". Users get the same log from Settings >
  About > Troubleshooting (`%APPDATA%\Zinc\logs\zinc.log`). Typing latency trace: internals, Diagnostics.
- Config: `%APPDATA%\Zinc\config.toml` (TOML, `[section] key`, `[profile.<id>]`; README has the reference).
  **`ZINC_CONFIG=<file>` for every test run**, never the user's real config (which sets `family = "JetBrainsMono NF"`).
  Schemes are looked up next to *that* file, so a test config elsewhere needs its `themes\` too; test scripts append
  their own `[window]` section (a TOML table can't be opened twice). Live reload on save.
- Session: `%APPDATA%\Zinc\session.json`, `ZINC_SESSION=<file>` overrides. **A run with `ZINC_CONFIG` set neither
  restores nor saves** unless `ZINC_SESSION` is also set, so test instances never touch the user's session.
- **Single instance per session file**: a later launch hands its command line to the running process and exits. So
  **a dev build started without `ZINC_CONFIG` would open a window inside the user's installed Zinc** instead of
  running: always set ZINC_CONFIG (or ZINC_SESSION to a scratch file).

## Where to look

Section names in brackets are `.scratch/internals.md`'s.

`src/Zinc.Core` (no WinUI; what the tests load):
- `Ghostty/` — bindings (`Native.cs`; `GhosttyKey.g.cs` from `scripts/gen-keys.py`), Ghostty's sprites (`Sprites.cs`).
- `Pty/` — `ConPty.cs` (pseudoconsole, child, reader thread), child environment, default terminal, user PATH,
  install cleanup. [Terminal core; System integration]
- `Terminal/TerminalSession*.cs` — owns the libghostty terminal, encoders and ConPty: callbacks, themes, find, links,
  commands from shell marks, prompt clicks, notifications. [Terminal core; Shell integration]
- `Terminal/` also: shell integration (`ShellIntegration.cs`, `ShellMarks.cs`; scripts in `assets/shell-integration`),
  folders (`ShellDirectory`, `WorkspaceFolder`, `NewTerminalStart`), Kitty images, links, drops, the benchmark.
- `Config.cs`, `ConfigFile.cs` (TOML → settings, profiles, discovery, renamed keys), `ConfigEditor.cs` (writes that
  keep the file byte-identical elsewhere), `ShellResolver.cs`. [Config, profiles and the session]
- `Input/` — keys: `KeyDecision`, `Keybindings` (+ key sequences), **`Actions.cs`: a new action needs an entry there
  (and the README table: a test checks it), plus a handler** in TerminalControl.RunAction (pane) or
  MainWindow.HandleAction (window). [Input and keys]
- `Panes/` — `Workspace`, `PaneTree`, `TabGroup`, start layouts, workspace look. `Session/` — session.json, layout
  capture/restore, recents, closed workspaces/windows. **Workspace rules in one place: `.scratch/workspaces.md`.**
- `Settings/` — `SettingsCatalog` (every setting; tests require every config key in it), `SettingsWriter`, key
  binding editor, scheme files. `Shaders/`, `Cli/` (zn's protocol, compiled into zn as linked source: keep those
  four files free of other Core types), `Commands/` (palette filtering).

`src/Zinc` (WinUI app):
- `MainWindow*.cs` — workspaces (TabView, or the sidebar list), panes and their tab strips (`GroupView`), drags,
  palette, menus, the start page, quick terminal, chrome. [Workspaces, panes and windows]
- `Terminal/TerminalControl*.cs` — one pane: session lifecycle, keyboard, mouse, IME, scrollbar, find, menus.
  `Terminal/TerminalRenderer*.cs`, `GlyphAtlas.cs`, `SwapChainSurface.cs`, `ShaderPass.cs` — drawing. [Drawing]
- `SettingsWindow*.cs` (code-only), `WelcomeWindow.cs`. [Settings window, welcome, palette]
- `App*.cs` — lifetime, tray, config watching, updates, zn's server, jump list. [System integration]

`tests/Zinc.Tests` — unit tests, `AbiTests` (bindings vs libghostty's type manifest), `SessionTests` (real cmd.exe
on ConPTY). `src/Zinc.Native.targets` — imported by every runnable project (native DLLs, the ConPTY package).

Threading: PTY output is parsed on the ConPTY reader thread; UI thread renders and encodes
input. **Every libghostty call touching the terminal must hold `TerminalSession.Lock`.**
libghostty callbacks (write_pty, title, bell, size) fire *during* vt_write/resize, i.e. on
whatever thread holds the lock — don't call back into the terminal from them; set flags
and read state after the write (see `_titleDirty`).

## libghostty-vt footguns

- Bumping the commit: `python scripts/vt-bench.py --save before` first (parse speed of the DLL Zinc ships: seeded
  plain/UTF-8/styled data, Zinc's scrollback, warm-up + best of 7, runs vary ~±2%), then after rebuilding
  `python scripts/vt-bench.py --compare before`: more than 10% slower is worth a look. Results in .scratch/bench/results
  (`ghostty-<commit>.json` per commit; 6301810: ascii ~270, utf8 ~250, styled ~135 MB/s). Ghostty's own ghostty-bench
  doesn't build for Windows; it builds for Linux and runs in WSL (.scratch/NOTES.md, GHOSTTY-BENCH).
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
  add to Unreleased with each user-visible change. Releasing (only when the user asks): rename Unreleased to the
  version the user's NEXT commit makes (`git rev-list --count HEAD` + 1), add a new empty Unreleased, stage it and
  propose "Release 0.1.0-dev.N"; once the user has committed, `release.ps1 -Publish` (refuses without an entry for its
  version or with anything left under Unreleased). **The dev number is the commit count, so it must stay above the last
  published one** (`releases.win.json` on the update server): squashing or rewriting history lowers the count (dev.330
  was published, then a squash made the next commit dev.330 again, which testers would never get). `-Publish` checks
  and refuses, saying how many more commits it needs: tell the user, don't work around it.
- Tester builds and the installer: `scripts/release.ps1` (self-contained publish, Velopack pack, refuses a dirty tree
  unless -AllowDirty). **install.ps1 and release.ps1 take the `Local\ZincPublish` mutex (they share obj\).** Smoke-test
  a publish with ZINC_CONFIG set to a fresh path. How it all works, and what went wrong before: internals, Release.
- **Never install a `Zinc`-id package on this machine** (it would take the dev install's Start Menu shortcut, sign-in
  entry and default terminal), and run every Setup/Update.exe with ZINC_CONFIG set: the installer starts Zinc, which
  would otherwise hand itself to the user's running Zinc. Update tests use the `Zinc.Test` id
  (`.scratch/tools/update-roundtrip.ps1`).
- Installed copies live in `%LOCALAPPDATA%\Programs\Zinc\versions\<stamp>-<version>`; installed zinc.exe processes
  are the user's own windows. Git hooks reinstall in the background after every commit or merge on `main` (the
user's commits of what you staged; post-commit and post-merge, since a merge runs only the latter; log at
  `.git/zinc-install.log`). **It builds the working tree while it runs: after a commit on main, wait for it to finish
  (a new folder under versions\, no install pwsh left) before switching branches or popping a stash** — once it
  installed a stash's uncommitted experiment as "-dirty".

## Windows / WinUI / ConPTY footguns

- WinUI 3: `FontWeights` is `Microsoft.UI.Text.FontWeights`; `Colors` is `Microsoft.UI.Colors`.
- A C# subclass of `SplitButton` (to override OnApplyTemplate) gets no template and throws 0x800F1000 "Cannot apply a
  Style with TargetType SplitButton to ContentControl", even with `DefaultStyleKey = typeof(SplitButton)`. Change template
  parts from outside instead, once they exist: an element shown later (Collapsed at Loaded) has no template at Loaded, but
  gets its first SizeChanged right after it's built (`LikePlainButton` in SettingsWindow.SchemesPage.cs).
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
- Panels have no automation peer either: an `AutomationProperties.Name` on a Grid reaches no screen reader. Items
  built from panels (sidebar workspaces, pane tabs) use `SelectableItemPeer` (ListItem/TabItem, Select = a click;
  2026-10-05, found when the dots got words). `uia.ps1 -Action list` prints ListItems too.
- Standalone ScrollBar: `IndicatorMode.None` *hides* it (it doesn't mean "full bar"). Widen with
  `VisualStateManager.GoToState(bar, "Expanded"/"Collapsed")` (what ScrollViewer does); GoToState
  returns false until the template has applied. Expansion is driven by a right-edge hot zone as
  wide as the expanded bar (16 DIPs, Windows Terminal's) from a handledEventsToo PointerMoved
  handler (the bar handles its own pointer events); held down (thumb drag, paging) it stays
  expanded until release, even off the pane. Look: `Terminal/TerminalScrollBar.xaml`, a copy of
  WinUI's DefaultScrollBarStyle with a few substitutions (its header says which) — its sizes are
  StaticResources, which per-bar Resources can't override. `test:scrollbar-expand` pins it open
  for captures (`.scratch/tools/sb-shot.ps1`; WT reference shots in `.scratch/tmp/wt-scrollbar-*`).
- **A pointer shape that flickers back to the arrow is (almost always) not Zinc**: any window change anywhere makes
  Windows refresh the pointer, and every WinUI 3 window answers with the arrow until the next move. Hours went into
  this (2026-10-04); read internals' "Pointer shape" before touching pointer shapes or custom window resizing. Don't
  clear WinUI's top-level class cursor. `InputNonClientPointerSource` border regions FailFast (8000FFFF) on a window
  that isn't resizable.
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
  **A blocking wait on the UI thread runs High-priority DispatcherQueue work** (a contended `lock`, Monitor or
  `System.Threading.Lock`, e.g. on `TerminalSession.Lock` while the reader parses; Normal priority doesn't run, a
  `Monitor.TryEnter` spin runs nothing; measured in-app 2026-10-15). The swap chain's `DrawWhenQuiet` is High, so a
  frame could start inside a frame: `SwapChainSurface.Present` defers a nested call. Code that queues High work must
  expect to run inside any lock wait on the UI thread.
- **XAML doesn't reliably order swap chains against each other**: with one pane drawn over others (Canvas.ZIndex,
  negative margins: the maximize animation), some covered panes' terminals were composited above it, against both
  ZIndex and tree order (2x2 layout, 2026-10-16; screenshots show it too). Opacity tricks didn't help. Anything that
  overlaps terminals shows the covered ones as still pictures (`TerminalControl.ShowStill`); internals, "Layout
  animations".
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
  Every window replays the keys from its start, the quick terminal too (summon it with `quick-toggle.ps1`). A `w2:`
  prefix runs a key only in the second window, so `w1:ctrl+shift+n` plus `w2:` keys set up two windows (with
  `new-workspace-page = false`), and `test:pane-window-left:0` drops the focused pane on another window's pane 0.
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
