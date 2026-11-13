# Zinc

Zinc is a terminal for Windows, a WinUI 3 app in C# (.NET 10, Windows App SDK 2.5, unpackaged and self-contained) built on libghostty-vt, ConPTY and Win2D. The README is for people who use it. The two docs below are for people who work on it, and every rule in them applies to agents too.

@docs/development.md
@docs/architecture.md

## Rules for agents

- **Before changing an area, read its section of `docs/architecture.md`.** When your change makes that section wrong, fix it in the same commit. The same goes for `docs/development.md` when you change the build, the scripts, the tests or the release.
- **Before you say a change is done**, run `dotnet build Zinc.slnx --no-incremental` with 0 warnings, then `dotnet test`. `dotnet test` doesn't build the app, so it can pass with a compile error in `src/Zinc`. When you check the app itself, make sure the `zinc.exe` and `Zinc.Core.dll` you launched are fresh from that build.
- **Set `ZINC_CONFIG` on every run of Zinc you start**, pointing at a scratch file. Without it, a dev build opens a window inside the installed Zinc instead of running, and it reads and writes the real config.
- **Stop only dev builds of Zinc**, never every `zinc.exe`. The person you're working with may be using Zinc, and your own session may be running in it. This stops only copies running from a build folder:
  `Get-Process zinc -ErrorAction SilentlyContinue | Where-Object Path -like '*\src\Zinc\bin\*' | Stop-Process -Force`
- **Screenshot only Zinc's own window**, with `PrintWindow` and `PW_RENDERFULLCONTENT`. Never capture the screen (`CopyFromScreen` and the like), because that can capture another app's private window.
- **Never use `SendKeys` or `SendInput`**, which go to whatever window has focus. Post messages to Zinc's own `InputSiteWindowClass` child window, drive it through UI Automation, or replay keys with `ZINC_TEST_KEYS`.
- **Screenshots are downscaled when you look at them.** Before calling something a rendering defect (a gap, a misaligned line), crop and upscale it or scan the pixels.
- **Ask a person about what you can't test**: hover, the pointer's shape (captures leave the pointer out), real mouse clicks, and real key presses with modifiers. The key-to-action map is unit-tested, but whether a key reaches Zinc needs someone at the keyboard.
- **Say which settings combinations you tested.** When a change touches layout or the window's chrome, test the defaults and the settings that change the chrome (`docs/development.md`, "Testing").
- **Text in the product follows `docs/ux-writing.md`**, so read it before writing any. Its punctuation rules are the ones most often missed: no semicolons, and a colon only before a list, between a label and its value, or in a reset arrow's tooltip ("Back to the default: no shaders").

## Code comments

The comment rules in `docs/development.md` ("Code style") apply. For Zinc's languages:

- `//`, `///` and `/* */` in C#, plus `#` in scripts.
- A short fragment is often best when it says one clear thing: `child.Visibility = Visibility.Visible; // a maximized pane's hidden sides show while they grow or shrink`.
- Keep a `<summary>` when it says something the name doesn't (a unit, what null means, when the value changes). Drop one that only restates the name (`None.` on `None`). The build generates no documentation file, so a member without a summary causes no warning.
- A comment that spans several lines wraps at about 120 columns. A comment that fits on one line stays on one line, even a long one, when that reads better than three. Don't rewrap an existing comment only for its width.
- Older comments in the repo sometimes use a compressed style, with arrows, semicolons and dropped words. Don't copy it.

Before: `Not the card less the terminal: an animation cut short leaves the terminal at that one's end size, and it resized again by a column at the end.`

After: `We use the chrome measured before the rebuild (via ChromeOf) instead of working it out from the terminal's current size, because an interrupted animation can leave the terminal at a stale size, which made it jump by a column when this animation finished.`
