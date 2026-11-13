# Working on Zinc

This is the guide to building Zinc, running it next to an installed copy, testing it and releasing it. How the app works inside, and the rules its code relies on, are in [architecture.md](architecture.md).

## What you need

- Windows 10 version 2004 (build 19041) or later, or Windows 11. Zinc builds for x64 only for now, and ARM64 stays out of the solution until there's a `native/arm64` build.
- The .NET 10 SDK.
- [Zig](https://ziglang.org) 0.16.x on `PATH`, for the native DLLs. It's the version Ghostty requires.
- PowerShell 7.4 or later (`pwsh`), for the build scripts.
- For publishing only (a release, or `dotnet publish`), Visual Studio Build Tools with the C++ workload. `zn`, the command-line tool, is compiled with Native AOT, which links with MSVC's linker. Everything else builds without Visual Studio.

## Building

```powershell
# 1. Fetch Ghostty at the pinned commit and build the native DLLs into native/x64
pwsh scripts/build/libghostty.ps1

# 2. Build everything
dotnet build

# 3. Run the tests
dotnet test
```

Step 1 builds three DLLs: `ghostty-vt.dll` (Ghostty's terminal core), `zinc-sprite.dll` (Ghostty's built-in glyphs for box drawing, blocks, Powerline and Braille) and `zinc-shader.dll` (the custom shader translator). They're all built for Zig's `windows-gnu` target, as libghostty's own Windows builds are, so they import only what Windows itself has and Zinc ships no C runtime. The script resets the Ghostty checkout in `external/ghostty` and applies the patches in `patches/ghostty` before every build, so an edit left in the checkout never reaches a build. `external/` and `native/` are build output and stay out of git. You only need to run it again when the Ghostty commit or a patch changes.

A native dependency added later has to keep that promise, so check what a new DLL imports. Anything beyond the Universal CRT, kernel32 and ntdll is a DLL a fresh Windows may lack. A `windows-msvc` build, for one, imports `vcruntime140.dll`.

Zig 0.16 on Windows sometimes fails with "unable to load '<file>': Unexpected". The scripts retry those, because each run caches what it finished and the next one gets further.

The app is unpackaged and carries the Windows App SDK with it, so the exe in `src/Zinc/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/zinc.exe` runs as it is. Add `-c Release` to the build for anything that measures speed.

`dotnet test` builds the test project and what it references, which doesn't include the app. So a compile error in `src/Zinc` only shows up in `dotnet build`. Run both before calling a change done.

A running `zinc.exe` from `bin/` locks the build output, and the build then fails with MSB3027. Close that copy first. This happens with dev builds only, because an installed Zinc runs from its own folder.

A git worktree has no `native/` folder, because it's build output. Either run the native build in the worktree, or point it at the main checkout's with a junction (`cmd /c mklink /J ..\zinc-feature\native native`). Before removing the worktree, unlink the junction first (`cmd /c rmdir ..\zinc-feature\native`), because `git worktree remove` with the junction in place can delete the real folder's contents through it.

## Running a dev build

Zinc allows one running copy per session file, and a second launch hands its command line to the copy that's already running. So a dev build started with your normal settings opens a window inside your installed Zinc instead of starting. Give every dev run its own config file:

```powershell
$env:ZINC_CONFIG = "$env:TEMP\zinc-dev\config.toml"
dotnet run --project src/Zinc
```

The file doesn't have to exist. A run with `ZINC_CONFIG` set also leaves your real session alone, because it neither restores nor saves `session.json` unless `ZINC_SESSION` is set too. Without a session it's a separate process that no other launch is handed to, so several test runs can run side by side. It also leaves alone everything an installed Zinc registers with Windows: the jump list, the sign-in entry, the Explorer menu entry, `zn` on `PATH`, and the default terminal, whose consoles it doesn't listen for. Color schemes are looked up in a `themes` folder next to the config file, so a test config somewhere else needs its own `themes` folder for anything but the bundled schemes.

Programs started in Zinc inherit its environment. If `NO_COLOR` is set where you launch Zinc from (some tools set it for their child processes), everything in the dev build renders without color. Clear it first.

### Environment variables

| Variable | What it does |
|:---|:---|
| `ZINC_CONFIG=<file>` | Reads and writes this config file instead of `%APPDATA%\Zinc\config.toml`, and turns off the session and everything Zinc registers with Windows, as above. |
| `ZINC_SESSION=<file>` | The session file to restore and save, instead of `%APPDATA%\Zinc\session.json`. A launch is handed to the running copy with the same session file. |
| `ZINC_SHELL=<command>` | The shell the first tab starts, unless the command line names one. Without either, it's the profile's `command`, or else the first of pwsh, Windows PowerShell and cmd on `PATH`. |
| `ZINC_LOG=<file>` | Turns on the debug log and writes it to this file, with short times that scripts can read. |
| `ZINC_PERF=1` | With `ZINC_LOG`, every pane logs its frame times. |
| `ZINC_TRACE=<file>` | Records every byte the shell sends, which shows what a program asks of the terminal. |
| `ZINC_CRASH_LOG=<file>` | Where crash reports go, instead of `%APPDATA%\Zinc\crash.log`. |
| `ZINC_UPDATE_URL=<address>` | Where to check for updates, instead of the built-in source. A folder or a web address. |
| `ZINC_JUMP_LIST=1` | Updates the taskbar jump list even with `ZINC_CONFIG` set. |
| `ZINC_DEFTERM_TEST=1` | Listens for handed-over consoles under a test CLSID, so default-terminal tests don't take over the real one. |

Zinc also sets some variables for the shells it starts, such as `ZINC_PIPE`, `ZINC_TERMINAL_ID` and `ZINC_ZN` (for `zn`), and the `ZINC_BASH_*`, `ZINC_ZDOTDIR`, `ZINC_CMD_PROMPT` and `ZINC_ELVISH_RC` ones that load shell integration. Those are Zinc's own plumbing, not settings.

### The test hooks

These ship in every build, because the update tests drive a published build with them. They're for development only, and nothing about them is stable.

- `ZINC_TEST_KEYS="alt+shift+plus,ctrl+f24,alt+left"` replays key chords, one a second, through the real key bindings, as if pressed in the focused pane. Posted window messages can't carry modifier state, so this is how a script presses Ctrl or Alt. `ctrl+f24` isn't bound to anything, so it works as a one-second pause.
- In the same list, `palette:<exact title>` runs a command palette entry, and `menu:Split pane/Split right` runs a workspace context menu item. `test:` entries do what posted input can't, such as drags (`test:pane-right:1`, `test:tab-to:1`), clicks on a cell, pastes, file drops, forcing a garbage collection (`test:gc`) or throwing from a dispatcher callback (`test:throw`). `MainWindow.TestKeys.cs` lists them all.
- Every window replays the keys from when it starts. A `w2:` prefix runs a key only in the second window, so one run can set up two windows, for example `w1:ctrl+shift+n` plus `w2:` keys, with `new-workspace-page = false` in the test config. `test:pane-window-left:0` then drops the focused pane on another window's pane 0.
- `ZINC_TEST_DETERMINISTIC=1` makes captures repeatable. The cursor doesn't blink and is drawn as focused, Mica is off and attention dots don't pulse.
- `ZINC_TEST_STILL=1` only stops the attention dots from pulsing.
- `ZINC_TEST_APPEARANCE=light|dark` forces the app's look, whatever Windows and the config say.
- `ZINC_TEST_CLIPBOARD=<text>` stands in for the clipboard, so tests never read yours.
- `ZINC_TEST_UNFINISHED_SETTINGS=1` shows settings that are parsed and tested but not drawn yet (`SettingDef.Unfinished`).

A palette title that's renamed silently breaks any script that runs it by name, because the replay only logs the missing entry and carries on. Search the scripts for a title before renaming it.

## Testing

`dotnet test` runs the whole suite (xUnit v3 on Microsoft.Testing.Platform, which `global.json` selects, because .NET 10's older VSTest path doesn't run xUnit v3). Besides unit tests, it has:

- `AbiTests`, which compare every binding struct and enum against libghostty's own manifest (`ghostty_type_json()`) and check that every imported function exists. They catch a C API change after a Ghostty bump.
- `SessionTests` and the other session tests, which run real `cmd.exe` and other shells on ConPTY, headless.
- Tests marked `[Trait("Category", "LocalShells")]`, which need shells that may not be installed (pwsh, Git Bash, elvish, Clink, a WSL distro). Each one skips itself when its shell is missing, so a fresh machine sees a few skips and no failures. To leave them out entirely, run `dotnet test -- --filter-not-trait "Category=LocalShells"`.

Run the tests after any change to the bindings, `TerminalSession` or `ConPty`, and always after bumping the Ghostty commit.

Posted input can't do everything a person can. Posted `WM_CHAR`, `WM_KEYDOWN` and `WM_MOUSEWHEEL` messages work, but posted keys don't update the keyboard state, so modifiers look released. Posted mouse clicks don't reach WinUI at all, because WinUI reads pointer messages, not mouse messages. Hover and the pointer's shape can't be tested at all, and screenshots leave the pointer out. Changes there need a person at the mouse. What scripts can do is drive the window through UI Automation, which is one more reason every button gets an `AutomationProperties.Name`, besides screen readers.

Your own settings can hide bugs that the defaults show. When a change touches layout or the window's chrome, try it with the defaults and with the settings that change the chrome: `pane-headers` (`auto` and `always`), `pane-gap` (0 and the card look), workspace tabs on top and on the left, and pane tabs on and off.

CI (`.github/workflows/ci.yml`) runs on every push to `main` and on pull requests. It builds the native DLLs (cached between runs), builds the solution with warnings as errors, builds the file-based scripts and runs the tests.

## Troubleshooting

- **The debug log.** Settings > About > Troubleshooting turns it on, and writes it to `%APPDATA%\Zinc\logs\zinc.log`. `ZINC_LOG` does the same for a dev run. `Log.Write` in Core adds a line, and it's cheap, so it's the quickest way to find out why something doesn't happen. The log is written by a background thread, so call `Log.Flush` before reading it from code.
- **Crashes.** Every exception that escapes goes to `%APPDATA%\Zinc\crash.log`, even with the debug log off. A crash inside WinUI itself often leaves nothing there, because it ends the process before .NET sees it. Windows Error Reporting keeps a dump in `%LOCALAPPDATA%\CrashDumps`, and `cdb` with `.ecxr; kn` shows where it happened.
- **A flickering pointer.** With the debug log on, the command palette has "Trace the pointer (15 s)". It logs what set the pointer's shape in every window, which is the only way to see these bugs, because posted input can't reproduce them. Most flicker turns out not to be Zinc, though: when any window changes, Windows asks every window for its pointer again, and every WinUI 3 window answers with the arrow until the pointer moves.
- **What a program sends.** `ZINC_TRACE` records the raw output, which shows the modes a program turns on.

## Adding a setting

Every setting is defined once, in the settings catalog, and the Settings window, the reference export and the config schema all read it from there.

1. Add a property to `Config` (`src/Zinc.Core/Config.cs`), with its default as the initializer. A `Config` property is named after its config key (`ConfirmClose` for `confirm-close`), even when it's a boolean.
2. Add one entry to the Settings page's catalog file, `src/Zinc.Core/Settings/SettingsCatalog.<Section>.cs`. It gives the section, group, key, label, description, kind, the range or choices, and a `Binding`. Use a `Bind` helper (`Bool`, `Number`, `Text`, `Color`, `Enum`, `Paths`) when one fits. A `Bind.Custom` reader lists the forms of TOML value the docs describe in `Accepts`, and the schema flags any other spelling the reader forgives.
3. Write the code that uses it, and add a line to `CHANGELOG.md` under Unreleased.
4. Run `dotnet test`. `SettingsCatalogTests` checks the binding, the ends of the range, and that every `Config` property is set by some setting. `ConfigSchemaTests` checks that the schema takes what the reader takes.

A setting that's parsed and tested but not drawn yet gets `Unfinished = true`, which keeps it out of Settings and the reference until it's done. The change that finishes it removes the flag.

The setting's label and description are text in the product, so they follow [ux-writing.md](ux-writing.md).

## The reference and the schema

`scripts/dev/export-reference.cs` writes two files that the release attaches and the website reads. `zinc-reference.json` describes the settings, actions, keys and notices of that build, and `config.schema.json` is a JSON Schema for `config.toml`. They're built from the same code the app reads: the settings catalog (with its top-level keys in `SettingsCatalog.TopLevel.cs`), `Actions.cs` (the actions and their arguments), `KeyChord` (the key names) and `OpenSourceNotices`. So a new setting, action or key name reaches them with no other change. Both are written with LF line ends.

The reference has a `format` number (`ReferenceExport.Format`), and the website refuses a format it doesn't know.

- Adding a field doesn't change the format, because a reader that doesn't know the field skips it.
- Removing or renaming a field, or giving one another meaning, raises the format by one, in the same change. Say so in the changelog, so the website knows to update its reader before it takes that release's file.

The schema lives at one address, `ConfigSchema.Url`, which is the schema's `$id`, and a new `config.toml` names it in its first line (`#:schema`). The address stays the same across releases, so the schema there always describes the latest release.

- A renamed key keeps working, because Zinc reads the old name (`ConfigFile.RenamedKeys`) and Settings writes the new one the next time it changes that setting. The schema keeps the old name too, marked deprecated with a description that says it still works, so an editor can point it out without calling it an error.
- If a release ever changes `config.toml` in a way Zinc can't keep reading, the schema moves to a new address with a version in it, and `ConfigSchema.Url` changes with it. Old configs keep their old `#:schema` line, which still describes them.

## Adding an action

An action is anything a key can be bound to. It needs an entry in `src/Zinc.Core/Input/Actions.cs` (its name, palette title, description and category) and a handler. The reference export picks the entry up for the website. A pane's actions are handled in `TerminalControl.RunAction`, and a window's in `MainWindow.HandleAction`.

## Bumping Ghostty

Ghostty's C API is explicitly unstable, so a new commit can change structs, enums and functions under you.

1. Run `dotnet run scripts/dev/vt-bench.cs -- --save before`, which records how fast the current DLL parses output.
2. Change `$Commit` in `scripts/build/libghostty.ps1`. Delete each patch in `patches/ghostty` whose fix the new commit already has (its header says what to look for), and fix up any other that no longer applies. A patch's tests run in the checkout with `zig build test-lib-vt -Dtest-filter=<name>`, because plain `zig build test` needs POSIX headers that Windows doesn't have.
3. Run `pwsh scripts/build/libghostty.ps1`. It also rebuilds `zinc-sprite.dll` from Ghostty's sprite code, which isn't part of Ghostty's API. If that fails, look at what changed in the sprite code's `Face`, canvas and `Metrics`. `SpriteTests` pin what Zinc relies on.
4. Run `pwsh scripts/build/gen-keys.ps1`, which regenerates `GhosttyKey.g.cs` from the new key header.
5. Run `dotnet test`. `AbiTests` fails on any struct, enum or export that changed. It can't check the arguments of imported functions, though, because the manifest describes types only, so read the header's diff for those.
6. Run `dotnet run scripts/dev/vt-bench.cs -- --compare before`.

The bench makes its workloads (plain text, mixed UTF-8 and styled text) in memory from a fixed seed on every run, so every run and every machine measures the same bytes, and nothing is cached between runs. Each workload gets a warm-up, then the best of seven runs counts. The results go to `dist/bench`, one file per Ghostty commit. Runs vary by about 2% on a quiet machine and about 5% on a busy one, so more than 10% slower is worth a look, after running it once more to be sure.

## The scripts

| Script | What it's for |
|:---|:---|
| `scripts/build/libghostty.ps1` | Fetches Ghostty at the pinned commit, applies the patches and builds `ghostty-vt.dll`, then runs the next two. |
| `scripts/build/sprites.ps1` | Builds Ghostty's sprite code as `zinc-sprite.dll`, with the C API in `native-src/sprite`. |
| `scripts/build/shader.ps1` | Builds the GLSL to HLSL shader translator as `zinc-shader.dll`, from `native-src/shader`, glslang and SPIRV-Cross. |
| `scripts/build/zig.ps1` | Shared by the three above: the Zig targets and `zig build` with its retry. |
| `scripts/build/gen-keys.ps1` | Regenerates `GhosttyKey.g.cs` from libghostty's key header. |
| `scripts/dev/vt-bench.cs` | Measures how fast `ghostty-vt.dll` parses output, before and after a Ghostty bump. |
| `scripts/dev/export-reference.cs` | Writes `zinc-reference.json` (settings, actions, notices) and `config.schema.json` for the website. |
| `scripts/dev/update-themes.ps1` | Refreshes the bundled color schemes in `assets/themes` from iTerm2-Color-Schemes. Check the diff before committing. |
| `scripts/release/release.ps1` | Publishes and packs a release with Velopack, into `dist\release`. |
| `scripts/release/release-notes.cs` | Prints a version's CHANGELOG entry, or with `--check`, fails when the changelog isn't ready for that release. |
| `scripts/release/check-publish.ps1` | Lists assemblies a published folder needs but its `zinc.deps.json` leaves out, which trimming can cause. |

The `.cs` scripts are .NET file-based apps that reference `Zinc.Core`, so they run with `dotnet run <file>` and need nothing but the SDK. A new script is written that way, unless it mostly runs other programs, where PowerShell fits better.

## Releasing

A release is a version tag. `ZincVersion` in `Directory.Build.props` is the release being worked toward.

- A build of the commit tagged `v<ZincVersion>` is that release, such as `0.1.0+a1b2c3d`.
- A build of a commit tagged with a pre-release of it, such as `v0.1.0-rc.1`, is that pre-release. Pre-releases use `rc.N`.
- Every other build is a dev build, `0.1.0-dev+a1b2c3d`, and is never released.

The version shows in Settings > About, and Zinc answers programs that ask the terminal's version (XTVERSION) with it. The build reads it from git, so it needs the full history and the tags, not a shallow clone.

To make a release:

1. Rename `## Unreleased` in `CHANGELOG.md` to `## <version> (<date>)`, and add a new, empty Unreleased above it. The changelog is for users, newest first, with `### New`, `### Changed` and `### Fixed` under each version. The test builds from before the first release keep their old headings, `## 0.1.0-dev.N (<date>)`.
2. Commit that, tag the commit `v<version>`, and push both.
3. The release workflow (`.github/workflows/release.yml`) builds and tests the tagged commit, packs it with [Velopack](https://velopack.io) and publishes it under the repository's GitHub Releases, marked as a pre-release when the version has an `rc.N` part.

The workflow refuses a tag that doesn't match the version the build works out, a version that isn't above every earlier release, a version with no changelog entry, and a changelog with notes still under Unreleased. The release gets `Zinc-win-Setup.exe` (a per-user install, with no admin prompt), the update packages, `releases.win.json` (what installed copies check once a day), `Zinc-win-Portable.zip`, and `zinc-reference.json` and `config.schema.json` for that version. The .NET runtime and the Windows App SDK are inside, at about 50 MB. Its notes are the version's changelog entry. Started by hand, the workflow is a dry run, which builds and packs the same way and keeps the files with the run for a week, without making a release.

After a release, move `ZincVersion` on to the next version.

Installed copies update themselves from the releases of the repository that `ZincRepositoryUrl` names in `Directory.Build.props`. A pre-release gets later pre-releases and releases, and a release gets releases only.

`pwsh scripts/release/release.ps1` does the publishing and packing, and runs on your own machine too. `-UpdateSource <address>` builds in a different place for installed copies to look, which is a folder or a web address that holds the folder's contents. `-From <address>` fetches the previous release first, so the new one also gets a small delta package. `scripts/release/package-readme.txt` goes into every install folder and the portable zip as `README.txt`. The script refuses a working tree with uncommitted changes unless you pass `-AllowDirty`. `-PackId Zinc.Test` packs a test package with its own id, so trying an install or an update never touches a real Zinc. Never install a package with the `Zinc` id on a machine where you use Zinc, because it takes over that copy's Start menu shortcut, sign-in entry and default-terminal registration. Run every `Setup.exe` with `ZINC_CONFIG` set, because the installer starts Zinc when it's done.

`release.ps1 -Publish` uploads to the update feed Zinc used before GitHub Releases. It exists for the last release there, the one that moves earlier test builds over to GitHub.

A published build is trimmed, and a `bin/` build isn't. Reflection over framework types can break only in a publish, so after changing the project files, try a publish rather than a `bin/` build, and check its log for `warning IL`.

## Forking

The license doesn't cover the Zinc name or icon, so a fork gets its own. A fork also needs its own identifiers, because a build that keeps Zinc's takes over an installed Zinc's Start menu shortcut, sign-in entry and registry keys. Change these:

- The Velopack pack id, `Zinc`, which is `release.ps1`'s `-PackId` default. It's also the pack's title.
- The default-terminal CLSIDs, `DefaultTerminal.Clsid` and `DefaultTerminal.TestClsid` in `src/Zinc.Core/Pty/DefaultTerminal.cs`.
- The update source, `ZincRepositoryUrl` in `Directory.Build.props`.
- The sign-in entry's name, `Zinc`, in `InstallCleanup.RunValue` (`src/Zinc.Core/Pty/InstallCleanup.cs`) and `LoginStartup.ValueName` (`src/Zinc/LoginStartup.cs`).
- The data folders, `%APPDATA%\Zinc` and `%LOCALAPPDATA%\Zinc`. `AppFolders` names them, but `ConfigFile.cs` and `Cli/CliPipe.cs` build the `%APPDATA%` path themselves. `CliPipe.cs` has to, because `zn` compiles it without the rest of Core.

## Code style

The build has no warnings, and CI treats warnings as errors. `.editorconfig` makes dead code (unused private members, parameters and assignments) a warning, so remove it or say why it stays. An incremental build only reports warnings for the files it recompiled, so use `dotnet build --no-incremental` to see them all.

For a wider sweep, these list suggestions rather than rules. Keep `--verify-no-changes`, because without it `dotnet format` rewrites the code.

```powershell
dotnet format style Zinc.slnx --verify-no-changes --severity info
dotnet format analyzers Zinc.slnx --verify-no-changes --severity info
```

- **Names.** A boolean is named after the state it reports (`IsOpen`, `HasFocus`), except a `Config` property, which is named after its config key. A private static field is `_name`, like an instance field.
- **ESC** in a string is written `\e`.
- **Line width.** There's no hard limit, and nothing is rewrapped for width alone. A comment that spans several lines wraps at about 120 columns. A comment that fits on one line stays on one line, even a long one.
- **Comments** say what the code does or what it prevents, in the present tense, in plain sentences a new reader can follow. The history behind a choice goes in the commit message instead. Leave out comments that only restate the code. Keep a `<summary>` when it says something the name doesn't (a unit, what null means, when the value changes), and drop one that only repeats the name.
- **Text in the product** (labels, descriptions, tooltips, notices, errors, the palette, `zn`'s output, screen reader names) follows [ux-writing.md](ux-writing.md). So do the README, the docs and the changelog, through its "Outside the app" section.
- **Markdown.** A paragraph or list item stays on one line, with no wrapping at a fixed width, so an edit never reflows the lines after it. Leave Markdown that's already wrapped as it is, unless you're rewriting that part anyway.
- **Line endings.** Every text file is LF, and `.gitattributes` keeps it that way in every checkout, whatever `core.autocrlf` says. An editor or tool that writes CRLF gets converted back when the file is staged. The one exception is a `.cmd` or `.bat` file, which stays CRLF for cmd.exe.
- Every user-visible change gets a line in `CHANGELOG.md` under Unreleased.
