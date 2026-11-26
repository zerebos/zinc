# How Zinc works

This guide is about how Zinc works inside. It covers where things live, how the pieces talk to each other, and the rules the code relies on. Most of those rules exist because something in WinUI, Win2D, ConPTY, or libghostty behaves in a way you wouldn't guess. Building, testing, and releasing are in [development.md](development.md).

## The three layers

```
 keyboard/mouse ──► TerminalControl ──encode (libghostty key/mouse encoders)──► ConPTY ──► shell
                          ▲                                                       │
                     Win2D renderer ◄── render state ◄── libghostty-vt terminal ◄─┘ (reader thread)
```

A key press starts in the pane's `TerminalControl`, which asks libghostty's encoders for the bytes the program expects and writes them to ConPTY, which hands them to the shell. The shell's output comes back the other way. A reader thread takes it from ConPTY and feeds it to the libghostty terminal, and the renderer draws from the terminal's render state.

- **libghostty-vt** is Ghostty's terminal core, built as `ghostty-vt.dll` and called through P/Invoke. It parses the shell's output, keeps the screen and its scrollback, reflows text on a resize, handles selection, and encodes keys, mouse events, and pastes for the program that's running.
- **ConPTY** is how Windows runs a console program inside a terminal. Zinc ships Windows Terminal's newer ConPTY (`conpty.dll` and `OpenConsole.exe`, from the `Microsoft.Windows.Console.ConPTY` package), and falls back to the one built into Windows when those files are missing.
- **Win2D** draws the render state. Backgrounds go down as one bitmap. Text comes from a glyph atlas, where each glyph is drawn once and then copied into its cell, so a whole frame of text is one sprite batch instead of thousands of separate glyph runs. Characters the font lacks (CJK, emoji, symbols) are laid out by DirectWrite once and kept in the atlas too. Box drawing, blocks, Powerline, and Braille come from Ghostty's own sprite code, built as `zinc-sprite.dll`, so their lines meet exactly at the cell edges whatever the font.

Everything else, from the windows and tabs to the settings, is Zinc's own WinUI 3 code.

## Where things live

`src/Zinc.Core` holds everything that doesn't need a window, which is also what the tests load.

- `Ghostty/` has the bindings (`Native.cs`, plus `GhosttyKey.g.cs`, which `scripts/build/gen-keys.ps1` generates), the check that the loaded library is a release the bindings fit (`LibraryVersion.cs`), and the sprite bindings (`Sprites.cs`).
- `Pty/` has `IPty.cs` (what a session needs from a pseudoterminal, and `PseudoTerminal.Start`, which makes this platform's), `ConPty.cs` (Windows' pseudoconsole, the child process, and the reader thread), `PosixPty.cs` (the same for macOS and Linux, which only the builds off Windows compile), the child's environment, the default terminal, the user's `PATH`, and install cleanup.
- `Terminal/TerminalSession*.cs` owns one libghostty terminal, its encoders, and its `IPty`. Around that it handles libghostty's callbacks, color schemes, find, links, prompt clicks, notifications, and the commands it learns about from shell marks.
- `Terminal/FrameReader.cs` turns libghostty's render state into cells with their final colors, for a renderer that isn't Win2D. The Windows renderer still does this itself in `TerminalRenderer.ReadRow`, by the same rules, so a change to one belongs in the other until they're joined.
- `Terminal/` also has shell integration (`ShellIntegration.cs` and `ShellMarks.cs`, with the scripts in `assets/shell-integration`), where new terminals start (`ShellDirectory`, `WorkspaceFolder`, `NewTerminalStart`), Kitty images, links, file drops, and the benchmark workloads.
- `Config.cs` is one resolved set of settings, and `ConfigFile.cs` reads `config.toml` into it, with profiles and renamed keys. `ProfileDiscovery.cs` finds the installed shells, `ConfigEditor.cs` writes changes back without touching the rest of the file, and `ShellResolver.cs` picks the shell a terminal starts. The config and scheme files are read with shared access and a few short retries (`ConfigFile.ReadShared`), because an editor may be partway through saving one. A config that can't be read counts as a config with errors, so Zinc reports it instead of falling back to the defaults.
- `Input/` has key handling (`KeyDecision`, `Keybindings`, and key sequences) and the actions in `Actions.cs`. Keys are `KeyCode`s, which are Windows virtual-key codes under Core's own name. The app casts WinRT's `VirtualKey` to one, because Core also builds without the Windows SDK and can't name WinRT types.
- `Panes/` has workspaces, the pane tree, tab groups, start layouts, and how a workspace looks. `Session/` has `session.json`, capturing and restoring layouts, recents, and closed workspaces and windows.
- `Settings/` has the settings catalog (`SettingsCatalog*.cs`, every setting), the writer, the key binding editor, and scheme files.
- `Shaders/` has the custom shader library, `Commands/` the palette's filtering, and `Shell/` what Zinc adds to Windows' shell (the Explorer menu entry and the jump list's content).
- `Cli/` is `zn`'s protocol. `zn` compiles four of these files as linked source, without the rest of Core (`CliProtocol`, `CliPipe`, `CliCommandLine`, `CliText`), so those four can't use other Core types.
- The files at the top level are app-wide pieces without a folder of their own, such as `Log.cs`, `AppFolders.cs`, `FirstRun.cs`, `UpdateFeed.cs`, `ReleaseNotes.cs`, and `ReferenceExport.cs`.

`src/Zinc` is the WinUI app.

- `MainWindow*.cs` is the window. That covers workspaces (as tabs or the sidebar list), drags, the palette, menus, the new-workspace page, the quick terminal, and the title bar. `GroupView*.cs` is one pane's frame, with its tab strip or header around the terminal.
- `Terminal/TerminalControl*.cs` is one terminal: the session's lifetime, keyboard, mouse, IME, the scrollbar, find, and menus. `Terminal/TerminalRenderer*.cs`, `GlyphAtlas.cs`, `SwapChainSurface.cs`, and `ShaderPass.cs` draw the terminal.
- `SettingsWindow*.cs` is the Settings window, built in code. `WelcomeWindow.cs` is the welcome window, which opens on the first run and from the palette.
- `Brand.cs` holds the app's colors for each theme, and `Assets/` the icon and the other images the app ships. `WindowIcon.cs` gives each window the `.ico` frames that exactly match its DPI, because `AppWindow.SetIcon` rescales a single frame and smears the 16-pixel icon.
- `App*.cs` is the app's lifetime, and it wires up the pieces that live in their own files, like `TrayIcon.cs`, `Updater.cs`, `CliServer.cs` (`zn`'s server), and the jump list.

The rest of the repo:

- `src/Zinc.Cli` is `zn`, the command-line tool, compiled with Native AOT.
- `src/Zinc.Native.targets` brings the native DLLs and the bundled ConPTY into the projects that run Zinc.Core, which are the app and the tests.
- `tests/Zinc.Tests` has the unit tests, `AbiTests` (the bindings against libghostty's own type manifest), and the session tests, which run `cmd.exe` and other shells on a real ConPTY.
- `native-src/` has the C APIs Zinc builds around Ghostty's sprite code and around glslang and SPIRV-Cross.
- `patches/ghostty/` has Zinc's fixes to Ghostty that aren't upstream yet. The build applies them.
- `assets/` has the bundled fonts, color schemes, shaders, shell integration scripts, and license texts.
- `scripts/` has the build, dev, and release scripts, which [development.md](development.md#the-scripts) lists.

## Threads and the terminal lock

A shell's output is read and parsed on ConPTY's reader thread, one thread per pane. The UI thread draws and encodes input. Both touch the same libghostty terminal, so **every libghostty call that touches a terminal holds `TerminalSession.Lock`**. libghostty's terminal isn't thread-safe, so a call without the lock races the reader's next write.

libghostty's callbacks (writing to the PTY, the title, the bell, size queries) fire during `vt_write` or a resize, on whichever thread holds the lock at that moment. So a callback must not call back into the terminal. It sets a flag or notes a value, and the code reads the state after the write returns. The title works this way, with the reader setting `_titleDirty` and the pane taking the title on the UI thread.

A closed session stays safe to call. A pane's UI can still call into its session after the pane closed (a late resize, wheel, or key event), and the reader thread can be partway through a read. So every public member of `TerminalSession` that reaches the terminal checks `_disposed` inside the lock and returns an empty value. Code outside the session that uses its handle checks `IsDisposed` in the same lock. `ClosedSessionTests` calls each of these members on a closed session, and a new member belongs in its list.

About 190 native threads at idle is normal for Zinc, and nearly all of them belong to the GPU driver and the frameworks. The .NET thread pool is empty at idle.

The reader thread parses on the same thread that reads, so ConPTY and libghostty take turns. That's fine for most output, but ConPTY takes in a whole large write before it emits any of it, so a program writing tens of megabytes at once makes the screen pause between batches.

## libghostty-vt

Zinc builds a libghostty-vt release. Its C ABI is stable within a major version, because a 1.x release only adds functions, enum values, and fields at the end of sized structs. A binding that doesn't match C still corrupts memory without a word, so most of these rules are about keeping the bindings honest.

- The bindings are checked against the library. Structs are laid out field by field to match C, most with explicit offsets, and C `bool` is mapped to `byte` so the structs stay blittable. `AbiTests` compares the structs and enums with libghostty's own manifest (`ghostty_type_json()`). A struct field that flattens a C union needs `[AbiField("value.coordinate.x")]` so the test can find it. The manifest describes types only, though, so the argument types and order of each imported function aren't checked. The test only confirms each export exists.
- What a value means is in the header's comments, not its types, so read them too. A setter given NULL restores the option's default, which for the scrollback byte limit is 10,000 bytes, so Zinc passes `SIZE_MAX` for no limit. A get call's output type can change while the key stays the same, as `GRAPHEMES_BUF` did when it started taking a `GhosttyCodepointBuffer`.
- `LibraryVersion.EnsureCompatible` runs before the first terminal is made, and refuses a `ghostty-vt.dll` from another major version or an older minor one.
- A sized struct (one with a `size` field, made with `GHOSTTY_INIT_SIZED` in C) needs `.Size = sizeof(struct)` set before any get call. Frozen structs, such as `GhosttyStyle`, grid refs, selections, and callback payloads, have no `size` field.
- `ghostty_terminal_set` takes callbacks and pointers as the value itself, not a pointer to them.
- A feature does nothing until its callback is installed. Without `OPT_WRITE_PTY`, the terminal answers no queries, and ConPTY waits for a cursor position reply when it starts. Without `OPT_SIZE`, the size reports (XTWINOPS 14, 16, and 18, and the mode 2048 report) go missing, which breaks SGR-pixel mouse reporting in programs that ask for the cell size. When a feature "doesn't work", check the option list in `terminal.h` first.
- `ghostty_render_state_get(..., ROW_ITERATOR, &iter)` and `row_get(CELLS, &cells)` fill in a handle created beforehand, so pass the address of your existing handle.
- `ROW_CELLS_DATA_FG_COLOR` and `BG_COLOR` return a resolved RGB value, or `NO_VALUE` for "use the default".
- A grid ref is only valid until the terminal's next change. So resolving a ref, running a selection gesture, and setting `OPT_SELECTION` all happen under one hold of the lock.
- `ghostty_paste_encode` changes its input buffer in place.
- The mouse encoder's `TRACK_LAST_CELL` option forgets the last cell whenever the encoder is synced from the terminal or given a size, which `EncodeMouse` does before every event. So `EncodeMouse` drops repeated motion reports itself, by comparing each one with the last one sent.
- `ghostty_formatter_format_alloc` can come back with a null pointer and a length of 0, so check before decoding. Copying a selection reads it with `TERMINAL_DATA_SELECTION` and formats it with `FORMATTER_OPT_SELECTION`, under one hold of the lock. HTML output needs the palette extra (`FORMATTER_OPT_EXTRAS`), which writes the `<style>` block that the HTML's colors refer to. `formatter.h` says HTML ignores extras, but it uses this one.
- Grapheme clustering (mode 2027) is off by default. Zinc turns it on only with the bundled ConPTY, which it creates with `PSEUDOCONSOLE_GLYPH_WIDTH_GRAPHEMES`, so both sides agree on how wide a character is.

## ConPTY

ConPTY mostly stays out of the way. These are the places where Zinc's code depends on how it behaves.

- The ConPTY package picks the `OpenConsole.exe` to copy from `$(Platform)` in its `.props`, which runs before a project sets its platform. So `src/Zinc.Native.targets` sets the `ConptyRequires*Host` properties itself. If `x64\OpenConsole.exe` is missing from the output, Zinc quietly falls back to the ConPTY built into Windows, which lacks grapheme widths and console handoff.
- A handed-over console uses two pipes, like a pseudoconsole Zinc creates, instead of Windows Terminal's one duplex pipe. A synchronous duplex handle serializes its reads and writes, so a read waiting for output would block the reply to the console's first query, and both sides would wait forever.
- ConPTY doesn't rewrite SGR or SGR-pixel mouse sequences, so they reach WSL byte for byte.
- `TerminalSession.Resize` resizes libghostty at once, so text reflows live, but tells ConPTY only after 100 ms without another resize. A window drag makes dozens of sizes, and a prompt that redraws on every one (Powerlevel10k, for one) piles up copies of itself. The layout animations skip the wait (`atOnce`), so a program repaints while its pane moves. Tests that check the child's size wait for the settle.
- Git Bash and MSYS2 programs write one console call per line, so `seq 1 200000` is about 200,000 tiny pipe writes and takes around 10 seconds, with Zinc mostly idle waiting. Programs that write in bulk are fast. Before optimizing the drawing, measure how long the reader waits compared to how long it works.
- ConPTY passes Kitty graphics sequences through without understanding them. Placing an image moves the cursor past it, unless the program sends `C=1`. That move happens in Zinc's terminal but not in ConPTY's. A Windows console program that reads the cursor from the console, like PSReadLine, then draws at ConPTY's position, where the image started, until the screen clears. WSL programs aren't affected, because they track the cursor themselves.

## The UI thread and the dispatcher

An exception in a `DispatcherQueue` callback or a `DispatcherQueueTimer.Tick` ends the process on the spot. It never reaches `Application.UnhandledException`, so nothing gets logged either. That's why work is queued with `SafeEnqueue` and ticks are handled with `OnTick` (both in `Ui.cs`). They go through `Ui.Guard`, which records the exception, shows it, and carries on.

The same goes for code after an `await` in an `async void` method, which resumes on the dispatcher without a guard, so guard it too. The native callbacks that can throw, such as the `MessageWindow` window procedure and ConPty's reader and exit threads, catch everything themselves. XAML event handlers are the exception to all this. They do reach `App.UnhandledException`, which marks them handled and shows the error notice.

The exceptions that reach Zinc's handlers (from the UI thread, from background threads, and from unobserved tasks) go to `Log.Crash`, which writes `%APPDATA%\Zinc\crash.log` even with the debug log off.

Two smaller rules come from the same place. Code that can run after a window closes checks `_windowClosed`, because the window's `AppWindow` is null by then. And every `DispatcherQueueTimer` lives in a field, because a timer that only a local variable references gets garbage-collected and silently stops firing. Short test runs often finish before the first collection, so `test:gc` forces one.

### What runs while the UI thread waits on a lock?

More than you'd think. While the UI thread waits on a contended `lock`, `Monitor`, or `System.Threading.Lock`, it runs any High-priority dispatcher work that's queued. Normal-priority work doesn't run, and a `Monitor.TryEnter` spin runs nothing. The usual wait is on `TerminalSession.Lock` while the reader is parsing.

A pane queues its first try at a frame at High priority, so a frame can start inside another frame's lock wait. Each `SwapChainSurface` notices a nested `Present` of its own and puts it off until the next refresh. Any other code that queues High-priority work has to expect to run inside a lock wait on the UI thread too.

### Why can't a frame pump messages?

XAML treats a message pumped inside its render tick as reentrancy and ends the process with a FailFast. There's no managed exception and nothing in `crash.log`, so the only trace is a crash dump.

The usual way to hit this is a COM call that pumps, and one hides in font handling. A Win2D `CanvasTextFormat` that names a font *file* (the bundled Cascadia is one) resolves that file through `Windows.Storage` on its first layout, and that pumps. So the text formats a pane draws with are realized in `SetFont`, never during a frame. The glyph atlas's formats (`GlyphAtlas.PrepareFormat`) are shared and never disposed, and a pane's own formats are rebuilt only when its font, size, or scale changes.

## Drawing

Each pane draws into a `CanvasSwapChain` through `SwapChainSurface`, which presents a frame as soon as the output pauses, at most once per refresh. With `low-latency-drawing = false`, a pane draws into a `CanvasControl` instead, which waits for WinUI's own frame. The pane reaches its surface through the same `Surface` and `Redraw()` in both modes.

- A swap chain pane that can't be seen does not ask for frames. That covers a pane in a hidden tab, one in a hidden or minimized window, and one whose swap chain was freed. When the pane shows again, the panel's `Loaded` event or `SetWindowHidden(false)` draws a frame at once. Anything new that asks for frames has to keep to this, because waiting for quiet output on a pane that never draws keeps the UI thread busy for as long as its program writes.
- `SetFont` does work that pumps messages, so a frame can start partway through it. The sprite cache remembers the cell metrics it was drawn for and is dropped when they change, so a frame never draws sprites at a stale cell size.
- A new `CanvasRenderTarget` can hold another target's old pixels, because GPU memory gets recycled. Clear it before drawing into it with normal blending. Otherwise, the glyph atlas can show the previous size's glyphs after a zoom.
- When glyphs are drawn in one sprite batch, the padding's sub-pixel offset is rounded to 1/64 of a pixel before it becomes part of the atlas key. Without that, floating-point noise makes a new atlas for every window size.
- A character the font lacks is drawn from a color font only when it's an emoji, meaning a pictograph in a wide cell, or a cluster with VS16 (U+FE0F), a zero-width joiner, or a keycap. Otherwise a text-style symbol like ✔ (U+2714) would turn into an emoji, and CJK would come out in an emoji font.
- Nerd Font icons from an "NF" font overflow their cell on purpose, as they do in Windows Terminal, and the atlas keeps the overhang. The "NFM" variants don't overflow.

### Kitty images

libghostty works out which Kitty images to draw during each render state update, including images that a program shows with Unicode placeholder cells, and `ImageList` reads that list. Each entry is one rectangle of one image, with its place in the viewport, its size, and its layer.

- The list is read again only when the render state's placements generation changes, which it doesn't on most frames.
- The render state doesn't copy pixels, so the renderer copies an image's pixels (`TerminalSession.CopyImagePixels`) under the terminal's lock, right after the update. It does that only for an image whose bitmap is missing or out of date. Each image has a generation number that changes when its pixels do, and a bitmap remembers the generation it was made from.
- Images go in three layers: below the cell backgrounds, between the backgrounds and the text, and above the text. A Kitty image's z-index picks its layer, and orders it within the layer.
- While a program holds the screen (synchronized output), the render state isn't updated, so the images drawn are the ones from when the hold began. No pixels are copied during a hold either, because the program may already have replaced an image. An image that has no bitmap yet shows once the hold ends.
- A placeholder cell's character, U+10EEEE, isn't drawn as text, because the image list already has an entry for the image that the cell shows.
- libghostty has no timer for animations. Before each frame's update, the renderer calls `TerminalSession.TickAnimations`, which moves every playing animation to its due frame and says when the next one is due. The pane then sets a one-shot timer for that time, at least 8 ms away, which asks for one more frame. A new animation frame is a new image generation, so the bitmap cache picks it up, and a frame of the same size is written into the existing bitmap.
- The animation timer keeps to the rule above about panes that can't be seen. Hiding a window stops it, and in a hidden tab it fires once, asks for a frame that never comes, and isn't set again until the pane draws. Animations aren't ticked while a program holds the screen, so they wait for the hold to end.

## Canvas lifetime

This applies to the fallback drawing path (`low-latency-drawing = false`), where each pane draws into a Win2D `CanvasControl`. The default swap chain path has no `CanvasControl`.

### Why would a pane stop drawing?

Win2D's `CanvasControl` counts its own `Loaded` and `Unloaded` events, and it draws only while it has seen more loads than unloads. That works until XAML reports one removal twice, which it does when an element is taken out of a subtree that was itself just detached. The count ends up one behind, so a canvas that's back on screen still counts as unloaded and never draws again. Nothing fails and nothing is logged. The pane keeps its last picture, even through a resize.

Zinc guards against it in two ways.

- Code that rearranges panes changes what each one shows before it takes the old layout apart. `RebuildLayout` calls `UpdateFocusBorders` first, which swaps each pane's shown terminal while its host is still in the tree, and only then calls `Dismantle`. `DetachWorkspace` releases each host (`ReleaseHost`) before `Dismantle` for the same reason. The other order is what produces the double removal.
- `TerminalControl` checks the count anyway, because XAML has other ways to unbalance it, such as the shown tab of a restored pane closing. If a `Loaded` leaves the count at zero or below, or a canvas that's still in the window sits at zero after an `Unloaded`, the pane swaps in a fresh canvas. The session, the scrollback, and everything else stay, and only the drawing surface changes.

### Why can't the old canvas just be dropped?

You'd expect a replaced canvas to be collected like anything else, but Win2D may still have it registered with the window. It registers every canvas for its window's `XamlRoot.Changed` event on each `Loaded`, and it unregisters the canvas only when an `Unloaded` brings the count to exactly zero. Nothing unregisters it when the canvas is destroyed. So a canvas freed while its count is off leaves a handler pointing at freed memory, and the window's next minimize, restore, or DPI change can hang Zinc.

`CanvasLifetime` keeps that from happening. Every canvas is created through it, and it mirrors Win2D's count and registration by Win2D's own rules. A retired canvas is held until an `Unloaded` unregisters it, or for good if that never happens. That's a small leak and a rare one, because the pane's handlers come off the canvas first, and a replaced canvas also releases its device resources.

So, for any code that touches a pane's canvas:

- Create every `CanvasControl` with `CanvasLifetime.Create`, and hand it to `CanvasLifetime.Retire` once your own handlers are off it. Never let one go to the garbage collector any other way.
- Change what a pane shows before you detach the layout around it.

With the debug log on, a count outside 0 and 1 is logged as "left its load count at", which tells you which path unbalanced it. Pairs that only arrive out of order, like a moved pane's `Unloaded` coming after its new `Loaded`, are harmless.

## Layout and panes

A workspace's panes are a tree (`PaneTree` in Core), and `MainWindow` builds nested `Grid`s from it. Splitting, closing, or moving a pane rebuilds that workspace's layout, reusing each pane's host element. Dragging a divider and maximizing a pane change the layout in place instead.

Before building the new layout, `Dismantle` takes the old one apart, clearing every old `Grid`. Hosts are reused, and a XAML element can't have two parents, so adding one that still has a parent fails fast deep inside XAML with `0x800F1000`. You can't detach a host by asking for its parent, either, because `FrameworkElement.Parent` is null for children added to a `Panel` in code.

A tab's content is set once, and the layout is rebuilt inside it. A `ContentControl` doesn't detach its old content until its next layout pass, so swapping content and reusing the old element right away runs into the same two-parents failure.

A pane's lifetime is explicit. TabView unloads a hidden tab's content and loads it again when the tab is shown, and rebuilds move panes between parents, so `Unloaded` never ends a session. Panes are closed by their window (`CloseWorkspace`, `Closed`), and `Loaded` handlers have to cope with running more than once.

Focus is the window's job. `FocusActive` moves focus at low priority, after layout. A pane never takes focus on `Loaded`, because every rebuild loads every pane again.

### Swap chains during animations

XAML doesn't reliably order swap chains against each other. When one pane is drawn over others (the maximize animation raises it with `Canvas.ZIndex` and grows it with negative margins), some of the covered panes' terminals can still be composited on top of it, whatever the z-index and the tree order say. Opacity tricks don't help. So the animations that draw one pane over others, maximize and restore and moving a pane, show the covered terminals as still pictures while they run (`TerminalControl.ShowStill`), drawn through their shaders. This affects swap chain panes only, so a `CanvasControl` pane stays live.

## WinUI quirks

WinUI 3 has a lot of behavior you only find by running into it. These are the cases Zinc works around, grouped by where they bite.

### TabView

- Its style defaults to `VerticalAlignment=Top`. Without `Stretch`, a TabView is only as tall as its tab strip, so the tab's content gets 0 pixels and its panes never start their sessions.
- A TabViewItem applies `HorizontalContentAlignment` and `VerticalContentAlignment` to its header, not its content. `Stretch` there pushes the label above the icon.
- Setting a TabViewItem's `Header` or `IconSource` replays the header's content transition, so the label slides in again, and decodes the icon again. So each tab has one `TabHeader` for good, with one `TextBlock` and a decoded icon per profile, shown and hidden through `Visibility`.
- The inner list has entrance transitions, which make new tabs slide in after their terminal had already drawn. `DisableTabAnimations` clears them.
- Lightweight styling set after the template has applied, like a tab's color, doesn't take until the item's `RequestedTheme` flips to the other theme and back, which makes it resolve its theme resources again.

### The title bar

Zinc keeps the standard caption buttons (`TitleBarHeightOption.Standard`, 32 pixels) with TabView's own strip of about 41 pixels, so the buttons are short and the tabs sit on the strip's bottom edge, as in File Explorer. Tall caption buttons would hang over the terminal.

The tab strip's `DragRegion` needs a `MinWidth` of at least the caption buttons' width (`AppWindow.TitleBar.RightInset` divided by the scale). Otherwise the tabs and the + and ▾ buttons slide under the caption buttons instead of shrinking and then scrolling.

The empty part of the title bar refuses drops, because it's caption area to Windows, which shows the "not allowed" pointer over it. WinUI keeps putting the caption region back however you clear it, so tab drops are accepted over the tabs and the + and ▾ buttons only. The one way out is collapsing the title bar element, after which WinUI stops putting the region back. The auto-hidden title bar uses that to free the area with `ClearRegionRects`. After the title bar shows again, call `SetTitleBar` once it's in place, or the row isn't draggable.

### Panels and controls

- Panels like `Grid` have no `OnPointer*` overrides, so subscribe to `PointerPressed` and the rest instead.
- Panels have no automation peer either, so an `AutomationProperties.Name` on a `Grid` reaches no screen reader. Items built from panels, like the sidebar's workspaces and the pane tabs, use `SelectableItemPeer`, which shows them as list items or tab items, where selecting one is a click.
- A C# subclass of `SplitButton` gets no template and throws `0x800F1000`, even with `DefaultStyleKey` set. Change template parts from outside once they exist instead. An element that starts collapsed has no template at `Loaded`, but its first `SizeChanged` comes right after the template is built (`LikePlainButton` in `SettingsWindow.SchemesPage.cs`).
- A standalone `ScrollBar` with `IndicatorMode.None` is hidden, not shown in full. Widen it with `VisualStateManager.GoToState(bar, "Expanded")`, as `ScrollViewer` does, which returns false until the template has applied. Zinc's bar (`Terminal/TerminalScrollBar.xaml`) is a copy of WinUI's default style with a few changes, because the default style's sizes are `StaticResource`s, which a bar's own resources can't override. The bar expands while the pointer is in a zone along the pane's right edge as wide as the expanded bar (16 DIPs, Windows Terminal's size), tracked by a `PointerMoved` handler that sees handled events too, because the bar handles its own pointer events. While the thumb is dragged or the bar is paging, it stays expanded until the button is released, even with the pointer off the pane.
- `FontWeights` is `Microsoft.UI.Text.FontWeights`, and `Colors` is `Microsoft.UI.Colors`, not the UWP namespaces most examples online use.
- C# doesn't allow `await` inside unsafe code, so async code that would sit next to it lives apart, like the clipboard helpers in `ClipboardText` and the drop reader in `TerminalControl.Drop.cs`.

### Windows and menus

Flyouts and menus are separate popup windows, so a capture of the main window doesn't show them.

Alt plus a character beeps, because handling `KeyDown` doesn't stop the window's default menu-mnemonic lookup, and with no menu bar that lookup beeps. `MenuKeySuppressor` swallows the `WM_SYSCOMMAND` with `SC_KEYMENU` that starts it. Alt+Space (the system menu) and a bare Alt tap still pass through.

## Keyboard input

A key's `WM_CHAR` arrives after its `KeyDown` and before its `KeyUp`. The terminal skips the character only when `KeyDown` already dealt with that key (`_charHandledByKeyDown`), by encoding it or running a key binding, and clears the flag on `KeyUp`. Skipping every character after a `KeyDown` would eat input that comes only as characters, from on-screen keyboards and automation. Control characters that aren't skipped go to the program as they are.

AltGr arrives as Ctrl+Alt, so while the kitty keyboard protocol is off, printable text typed with Ctrl+Alt is treated as text, not as a chord.

A popup that commits on Enter's `KeyDown` hands focus back before that key's `WM_CHAR` arrives. The rename box and the palette both do, and without a guard the terminal gets the `\r` and runs the prompt's line. So the terminal notes Enter, Escape, Tab, and Backspace held down when it gets focus (`_foreignKeys`), and drops their character and release. A `KeyDown` in the terminal makes the key its own again. Posted input doesn't set the keyboard state, so automation is unaffected.

## Pointer shape

**A pointer that flickers back to the arrow is almost never Zinc's doing.** When any window changes anywhere, Windows asks windows for their pointer again, and every WinUI 3 window answers with the arrow until the pointer moves. Clearing WinUI's top-level class cursor doesn't stop the flicker, and it leaves stale pointers behind, so don't. With the debug log on, the palette's "Trace the pointer (15 s)" logs what set the shape in every window.

`InputNonClientPointerSource` border regions, which the quick terminal uses for its own resize edges, end the process on a window that isn't resizable (a FailFast with `0x8000FFFF`). The quick terminal turns `IsResizable` on before it sets them.

## The published app

A release is a self-contained, trimmed publish. The first four of these settings in `src/Zinc/Zinc.csproj` matter only to a publish, so a `bin/` build never shows what goes wrong without them. Test a publish after changing them.

- **`EnableMsixTooling=true`.** Without it, `dotnet publish` of an unpackaged WinUI app leaves out `Zinc.pri` and the `.xbf` files, and the published exe dies at startup with `0xC000027B`.
- **`TrimMode=partial`.** Only assemblies marked trimmable are trimmed (.NET and the Windows SDK projection), and everything else, Zinc's own assemblies included, stays whole. Reflection over framework types, such as `JsonSerializer` without source generation, can still break in a publish only, so check the publish log for `warning IL`.
- **`BuiltInComInteropSupport=true`.** Trimming turns built-in COM off, and the `[ComImport]` interfaces (such as the taskbar progress, the folder picker, and console handoff) then throw when they're used.
- **The `DropUnusedFromPublish` target** leaves out the .NET debugger's files and WebView2, which Zinc doesn't use. Bring WebView2 back if a tab ever shows web content.

Two more choices affect every build.

- The Windows App SDK comes as its component packages (WinUI, Foundation, InteractiveExperiences), not the `Microsoft.WindowsAppSDK` metapackage. The metapackage adds AI, ML, search, and widgets packages, which made the published app much bigger. Keep the component versions in step with the metapackage release they came from.
- Solution builds can pass `Platform=AnyCPU` as a global property, which a project's own default can't override. A self-contained WinUI build then fails with "requires a supported Windows architecture", so the projects that run (the app, `zn`, and the tests) fall back to the `win-x64` runtime identifier. `Zinc.slnx` also maps every project to x64 explicitly, so that solution builds and project builds use the same output folder.
