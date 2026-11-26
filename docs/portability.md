# Running Zinc.Core off Windows

Zinc is a Windows app, and this isn't a plan to change that. It's a record of the work that lets `Zinc.Core` build, run, and be tested on macOS, what that work was checked against, and what it found out about a port. The work was done on a Mac with no Windows machine at hand, so read "What hasn't been checked" before trusting any of it.

The experiments behind it (an Uno window that draws a terminal with Skia, a headless probe, and the survey below) are throwaway code and aren't in this tree. They live on the `spike/uno-macos` branch, which sits on top of this work.

## What works now

- **`Zinc.Core` and the tests build for plain .NET** anywhere but Windows (`src/Zinc.Platform.props`). On Windows nothing about the build changes.
- **The PTY is an interface.** `TerminalSession` holds an `IPty` and gets it from `PseudoTerminal.Start`, which makes `ConPty` on Windows and `PosixPty` elsewhere. `PosixPty` uses a 40-line C helper (`native-src/pty/zinc_pty.c`) for the fork and exec, because a .NET process can't fork safely from managed code.
- **Key bindings use Core's own `KeyCode`**, an enum with the names and values of WinRT's `VirtualKey`. The app casts to it where a WinRT key comes in. Decoding PNGs for Kitty images is the one WinRT use left in Core.
- **`FrameReader` reads a frame for any renderer.** It turns libghostty's render state into cells with their final colors, with no UI types. A Skia renderer draws from it in the spike this work came from.

libghostty needed no changes. The pinned release builds as a macOS library with Zig, and `AbiTests` finds every binding struct and enum laid out the way that library says, on ARM64 macOS as on Windows.

## How it was checked

- **`dotnet test` on macOS.** 1,616 of 1,738 tests pass. The 112 that fail failed before this work too, and all of them need Windows: the registry (`InstallTests`, `UserPathTests`, `DefaultTerminalTests`), `cmd.exe` and Windows paths (`SessionTests`, `ShellInjectionTests`, `LaunchRequestTests`), and the sprite and shader DLLs, which aren't built for macOS (`SpriteTests`, `ShaderTests`). Every commit was held to that same list of failures.
- **`scripts/dev/check-windows.sh`.** It compiles the Windows build of Core, the tests, and the app's C# on the Mac. It caught one real break along the way, a macOS-only file that had leaked into the Windows build.
- **The spike app.** It runs a real shell in an Uno window with one terminal drawn in Skia, and held 60 frames a second on every benchmark workload before and after it moved to `FrameReader`.

## What hasn't been checked

Nothing here has run on Windows. The compile check sees C# errors and nothing else. So before this merges, on Windows:

1. `dotnet build Zinc.slnx --no-incremental`, with 0 warnings. The compile check doesn't see the XAML, and it can't tell a warning that only a full build raises.
2. `dotnet test`. `KeyCodeTests.MatchesWinRtsVirtualKey` is the one to look at first. `KeyCode`'s members were written from memory, and that test compares them with WinRT's, name by name.
3. Start Zinc and press a few bound keys, a key with Ctrl, and the quick terminal's hotkey. The key change is a cast at six call sites, but whether a key reaches Zinc needs someone at the keyboard.
4. Open a console with Zinc as the default terminal. A handed-over console still arrives as a `ConPty`, and `TerminalSession.Attach` now takes it as an `IPty`.

## How much of the app could move to Core

A survey tool on the spike branch reads the compiled `zinc.dll` and weighs every method by the size of its IL. A method is free when its signature, its locals, and its body name no type from WinUI, Win2D, WinRT, or Direct3D, and no P/Invoke.

| | Share of the app's 435 KB of IL |
|:---|---:|
| Names one of those types | 81% |
| Free | 19% |
| Free, and so is every app method it calls | 12% |

Each method that isn't free is counted under the first such type the tool meets in it, which splits the 81% into 61% WinUI, 12% WinRT, and 6% Win2D. That split is rough, because a method that names both a WinUI and a WinRT type lands under whichever comes first.

So there isn't a second Core hiding in the app. `SettingsWindow` and `MainWindow` are a quarter of the app each, and about a tenth of each is free all the way down. The split between Core and the app is already close to where it can go without rewriting UI code, and that decides the shape of a port. Other platforms would either run this UI code on a toolkit that speaks WinUI's API, or get a UI of their own.

## What's left

- **The Windows renderer doesn't use `FrameReader` yet.** `TerminalRenderer.ReadRow` does the same work by the same rules, and the two are copies until it's switched over. That switch is on a frame's hot path, so it wants Zinc's benchmark on Windows before and after.
- **The rest of the renderer is still Win2D.** The glyph atlas, the sprite cache, ligature shaping, images, and the custom shaders have no shared half. The spike draws text as Skia text blobs instead of from an atlas, and that was fast enough, so the atlas may not need to cross over at all.
- **The sprite and shader libraries aren't built for macOS.** Both are Zig projects that should cross-build, and nothing has tried.
- **Core still has Windows-only code that compiles everywhere and fails when called.** The registry users are the bulk of it (`ProfileDiscovery`, `DefaultTerminal`, `UserPath`, `InstallCleanup`, `ExplorerMenu`), and the build off Windows reports 57 analyzer warnings for them. They want the same treatment the PTY got, one interface each where another platform has an equivalent, and nothing where it doesn't.
- **`PosixPty` throws `FileNotFoundException` for a program that doesn't exist**, where `ConPty` throws `Win32Exception`. The pane catches the Windows one.
- **Keys off Windows come without scan codes.** `KeyMap.FromScanCode` is built on Windows' scan codes, which tell the punctuation keys apart whatever the layout. The spike falls back to the virtual key, so punctuation only works there because the character event carries it.
