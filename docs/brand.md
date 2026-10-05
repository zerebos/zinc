# Zinc — Brand Guidelines

This is the source of truth for Zinc's name, identity, in-app branding and bundled colour schemes. It is written for implementation: wherever a value matters, the exact value is given.

## 1. The idea

A terminal is a grid of cells, and everything it shows comes from deciding, cell by cell, what is lit. Zinc's identity takes that literally. **Lit cells on a dark phosphor screen** is the whole visual language: the mark is built from whole cells, the app icon is a little dot-matrix screen, and in-app accents behave like lit pixels.

The name ties in quietly. Copper-doped zinc sulfide is the classic phosphor, and it glows green. The icon tells the "screen" story visually, so the name doesn't have to explain it.

## 2. Name

- **Zinc**, capitalised, in prose. The wordmark is always lowercase: `zinc`.
- CLI and executable: `zinc`.
- Never "Zinc Terminal" as the product name. Where context is needed, use the descriptor: "Zinc, a precise, native terminal for Windows."
- Ghostty lineage line (About page, README): *"Zinc: what makes ghosts glow in the dark."* This is a story nod to libghostty, never a visual one.

## 3. Positioning

**One line:** A precise, native terminal for Windows, built on Ghostty's core, organized around how you actually work.

**Tagline:** *Every cell, exactly right.*

Claims come in a fixed order. Lead with the first; never lead with the last two.

1. **Precision and craft: the headline.** Pixel-exact box drawing, blocks and Powerline shapes that meet without seams in any font; synchronized output; correct rendering of every program's output.
2. **Workflow: the reason to stay.** Workspaces that reopen where you left them, automatic shell integration and the attention system. "A terminal that knows what your commands are doing."
3. **Native Windows polish: the proof of care.** WinUI 3, Mica, tray, global hotkey, per-DPI icons, automatic shell discovery.
4. **Speed: a given, not a headline.** No "fastest" or comparative speed claims until benchmarks are published.
5. **Flexibility: a supporting point only.**

**Audience:** developers on Windows who live in the terminal and move between PowerShell, WSL, Git Bash and SSH; people who've used Ghostty, kitty or WezTerm elsewhere; people who notice when a prompt segment doesn't join.

## 4. Voice

**Calm and precise. Zinc gets out of your way, but notices things for you.**

- **In the interface**, follow `ux-writing.md`. It is the source of truth for every word inside the app.
- **In marketing, the README, docs and release notes**:
  - **Specific over superlative.** Name the detail: "Box drawing meets without seams in any font" beats "beautiful rendering". (The interface does the opposite and names the familiar thing; that's deliberate.)
  - **Calm.** No exclamation marks, no "blazing", "insanely" or "game-changing".
  - **Honest about limits.** Docs and release notes state plainly what isn't there yet. The interface doesn't advertise limits.
  - **Short sentences, plain words.**

**Ghostty lineage**
- Say: "built on libghostty", "uses Ghostty's terminal engine", "the same core that powers Ghostty". Credit it in the About page, README and docs.
- Never say "Ghostty for Windows", "a Ghostty port", or anything implying an official Ghostty project.
- Never borrow Ghostty's visuals: no ghosts, its icon style or its colours.
- In the interface, don't compare Zinc to other terminals at all (see `ux-writing.md`).

## 5. The mark: Quadrant

Two square blocks that share exactly one corner point, like the ▚ "legacy computing" glyph, which is one of the very shapes Zinc's rasterizer draws pixel-perfect.

- **Top-left block: dim (50%).** Context, what came before.
- **Bottom-right block: bright (100%).** The focal point, the cursor.
- The blocks touch at **one corner point only**: never overlapping, never separated.
- At 64 px and up, each block is a **6 × 6 grid of lit cells**. At smaller sizes it simplifies; see §7.

**Lit cell (pixel) geometry**
- A rounded square **86% of the grid pitch** (so the gap is 14%), with corner radius **12% of the pitch**.
- Rich treatment: vertical gradient `#C6FFD0` (top) to `#2FE07A` (bottom), spanning the whole mark's height.
- Flat treatment: solid `#3DF2A0` on dark; `#0B7A4B` on light. Ink `#0F1311` is for one-colour reproduction only.
- Optional glow, on lit cells only: a rounded square 1.5× the pitch, radius 50% of the pitch, `#3DF2A0` at ~6% opacity.

**Unlit dot (screen texture):** a circle with radius **20% of the pitch**, `#3DF2A0` at **10%**, in every grid cell not occupied by a lit cell. The grid must align with the mark.

## 6. App icon: the screen tile

The tile *is* the phosphor screen. There is no bezel. Master geometry in a 240 × 240 viewBox:

| Layer | Spec |
|---|---|
| Tile | Rounded rect, full size, `rx = 52`; clips everything inside |
| Screen | Radial gradient at `cx 0.35, cy 0.28, r 0.95`: `#113623` (0) → `#07180F` (0.55) → `#020805` (1) |
| Dot grid | Edge to edge, pitch 10, aligned to the mark |
| Mark | Centred, about 50% of the tile, 6 × 6 cells per block at pitch 10 |
| Top sheen | Vertical: white 16% (top) → 2% (40%) → 0% (bottom) |
| Edge | 3 px stroke inset 1.5 px, `rx 50.5`; vertical gradient `#BDF5D3` 35% → 6% |

One icon works on both light and dark taskbars. The dark tile carries its own background and keeps the green.

## 7. Icon sizes (`.ico` and PNG)

Every frame from 16 to 64 px is a **hand-placed, whole-pixel master**, not a downscale. The mark stays at **about 50% of the tile** at every size, matching the large icon.

| Size | Treatment | Mark |
|---|---|---|
| 16 | Solid 4 px blocks, no grid | 50% |
| 20 | Solid 5 px blocks + dot grid | 50% |
| 24 | Solid 6 px blocks + dot grid | 50% |
| 32 | 3 × 3 lit cells (2 px, pitch 3) + dot grid | ~53% |
| 40 | 3 × 3 lit cells + dot grid | ~43% |
| 48 | 4 × 4 lit cells + dot grid | ~48% |
| 64 | 6 × 6 lit cells (2 px, pitch 3) + dot grid | ~55% |
| 96, 128, 256 | Vector tile master | 50% |

- All small frames carry the tile's top sheen and a 1 px edge (brighter at the top).
- The `.ico` must embed the exact masters. Verify the frames aren't resampled.
- The Windows 11 taskbar uses the **32 px** frame at 100% scaling, so treat 32 as the most important size.
- For MSIX packaging, the same masters feed the scale-specific PNG assets.

## 8. Colour

### Brand palette
| Name | Hex | Role |
|---|---|---|
| Phosphor green | `#3DF2A0` | Signature colour; the mark and accents on dark |
| Lit gradient | `#C6FFD0` → `#2FE07A` | Rich lit cells only |
| Screen | `#0F1311` | Dark ground; ink for the mark on light |
| Paper | `#EDEFEA` | Light ground; wordmark on dark |
| Amber | `#FFB000` | Caution only. Never in the mark |

### UI tokens (app chrome)
Brand colours apply to the **app chrome only**. They never touch terminal content: the user's colour scheme always wins. Contrast ratios are measured against Mica (≈ `#202020` dark, ≈ `#F3F3F3` light).

| Token | Dark | Light | Use |
|---|---|---|---|
| `accent` | `#3DF2A0` (11.2:1) | `#0B7A4B` (4.9:1) | Focus, selected tab, active workspace, primary buttons, links |
| `accent-subtle` | accent @16% | accent @12% | Hover and selected backgrounds |
| `attention` | `#3DF2A0` | `#0B7A4B` | Attention dot, workspace counts |
| `caution` | `#FFB000` (8.9:1) | `#9A5B00` (4.9:1) | Needs-you states: close-while-running, awaiting input, bell |
| `error` | `#FF6B6B` (5.9:1) | `#B42A28` (5.7:1) | Failed-command marks, errors (red, never amber) |
| `on-accent` | `#0F1311` | `#FFFFFF` | Text and icons on filled accent |

- Offer a setting to follow the Windows accent colour; the brand green is the default.
- Chrome is nearly colourless unless something is focused, selected or asking for attention.
- Everything text-bearing meets WCAG AA (4.5:1). Recheck any new surface.

## 9. Brand inside the app

**The attention dot is a lit cell.**
- Shape: rounded square, corner radius 12% of its size. Never a circle.
- Size: 6 epx on tabs; 8 epx next to workspace counts.
- Colour: `attention` when work finished out of view or a program sends a notification; `caution` when a program is waiting on the user; `error` when the command that finished out of view failed.
- Pulse: opacity 100% → 35% → 100% over 1.6 s, ease-in-out, until the item is viewed. Hold steady when Windows "Show animations" is off. Never scale, bounce or flash.
- Counts: `on-accent` text on an `attention` pill, monospace, capped at `9+`.

**Other touches**
- **Command center:** the running command name in monospace, with a static `attention` lit cell beside it. The pulse is reserved for things that need you.
- **Taskbar progress:** use the system states (normal, paused, error) as-is.
- **Welcome flow:** the one place the mark appears large in-app. Use the glow mark on Screen in dark theme, and the flat mark in light theme.
- **Title bar:** the app icon from the per-DPI set.
- **Typography:** UI text in Segoe UI Variable (the system font); Geist Mono only for counts, command names and the wordmark; the terminal uses the user's font (bundled default: Cascadia Mono NF).

## 10. Wordmark and lockup

- **Geist Mono Regular, lowercase**, outlined in exported assets.
- Order: **wordmark, then mark**, like a cursor at the end of a typed line. The mark never comes first.
- The mark's height equals the ascender height (baseline to the top of the `h`), and it sits on the baseline.
- The gap is half a monospace character's advance, measured from the end of the text.
- Clear space is 25% of the ascender height on every side, measured from visible ink.
- **Dark lockup:** Paper wordmark, rich mark.
- **Light lockup:** Screen-ink wordmark, mark in the light-theme accent `#0B7A4B` (bright block 100%, dim block 50%). The mark stays green on light grounds.
- **Mono lockup** (`quadrant-zinc-mono.svg`): everything in ink, for one-colour print, embossing or stamps only. It reads as greyscale, so never use it as the everyday light version.

## 11. Bundled colour schemes: Lit Cell

Zinc ships two original schemes built from the identity. **Lit Cell is the default dark scheme and Lit Cell Light the default light scheme.** Users can switch to any other scheme, which is consistent with §8: these are content schemes users choose, not brand colours forced onto content.

Both were checked against all 658 schemes in iTerm2-Color-Schemes (CIEDE2000). Neither is a copy or near-copy; the closest are After Hours (6.1) for dark and Warm Burnout Light (6.8) for light.

### Lit Cell (dark)
| Slot | Hex | | Slot | Hex |
|---|---|---|---|---|
| background | `#0C110E` | | foreground | `#CFDDD3` |
| cursor | `#3DF2A0` | | cursor text | `#0C110E` |
| selection bg | `#1E4533` | | selection fg | `#EDEFEA` |
| 0 black | `#1C2420` | | 8 bright black | `#758679` |
| 1 red | `#FF6B6B` | | 9 bright red | `#FF9494` |
| 2 green | `#3DF2A0` | | 10 bright green | `#86FFC6` |
| 3 yellow | `#FFB000` | | 11 bright yellow | `#FFCB5C` |
| 4 blue | `#6FB3F2` | | 12 bright blue | `#9CCBFF` |
| 5 magenta | `#C99AF2` | | 13 bright magenta | `#DDBCFF` |
| 6 cyan | `#48D6CF` | | 14 bright cyan | `#86ECE6` |
| 7 white | `#B9C7BE` | | 15 bright white | `#EDEFEA` |

### Lit Cell Light
| Slot | Hex | | Slot | Hex |
|---|---|---|---|---|
| background | `#F1F4EF` | | foreground | `#1B2620` |
| cursor | `#0B7A4B` | | cursor text | `#F1F4EF` |
| selection bg | `#CBE6D6` | | selection fg | `#0F1311` |
| 0 black | `#1B2620` | | 8 bright black | `#5F6E66` |
| 1 red | `#AA2826` | | 9 bright red | `#C9382F` |
| 2 green | `#09663F` | | 10 bright green | `#0C7E4F` |
| 3 yellow | `#834D00` | | 11 bright yellow | `#9E5F00` |
| 4 blue | `#225B9D` | | 12 bright blue | `#316FB7` |
| 5 magenta | `#7E3E98` | | 13 bright magenta | `#9A4BB9` |
| 6 cyan | `#096561` | | 14 bright cyan | `#0C7B75` |
| 7 white | `#C3CEC7` | | 15 bright white | `#FFFFFF` |

**Scheme design notes**
- Neutrals carry a slight green cast; that is what makes these schemes distinctive.
- Dark: every colour except black is ≥ 4.5:1 against the background (bright black for comments is 4.9:1).
- Light: normal colours are ~6.2:1 and bright colours ~4.6:1; whites stay low-contrast by design.
- Red, green and yellow match the UI's `error`, `accent` and `caution` tokens, so a failure looks the same in the terminal and in the chrome.

## 12. Assets

In this repository: `assets/brand/icon/`, `assets/brand/mark/`, `assets/brand/lockups/`; the app's
icon is `src/Zinc/Assets/Zinc.ico` (a copy of `quadrant.ico`); the schemes are bundled from
`assets/zinc-themes/`.
The repository's copies carry no C2PA provenance block (the exports do): after copying new exports in,
run `python scripts/strip-c2pa.py` (lossless; it checks every file renders the same).

| File | Use |
|---|---|
| `quadrant.ico` | Final app icon, 16–256 px, hand-placed small frames |
| `quadrant-{16..256}.png` | Per-size masters (MSIX assets, title bar, tray) |
| `quadrant-app-tile.svg` | Vector tile master (96 px and up) |
| `quadrant-flat.svg`, `-glow.svg`, `-ink.svg`, `-rich.svg` | Mark treatments |
| `quadrant-zinc-dark.svg`, `quadrant-zinc-light.svg` | Lockups (outlined); light uses the green accent |
| `quadrant-zinc-mono.svg` | One-colour lockup for print only |
| `quadrant-construction.png` | Construction sheet |
| `Lit Cell`, `Lit Cell Light` | Ghostty-format scheme files |
| `lit-cell*.windows-terminal.json` | The same schemes for Windows Terminal |
| `ux-writing.md` | How Zinc's interface is written |

## 13. Don't

- Don't add a `>_` prompt, chevrons, ghosts or gears to the mark.
- Don't separate or overlap the Quadrant blocks, or swap which one is bright.
- Don't put anything but lit cells in the mark: no smooth shapes, letterforms or diagonals.
- Don't add glow to anything but lit cells, and use no bloom, blur, scanlines or CRT curvature.
- Don't let brand colours touch terminal content (bundled schemes are fine; they're user-selectable).
- Don't use amber in the mark or for failures.
- Don't downscale the vector tile for 64 px and below; use the masters.
- Don't set the wordmark in capitals or another typeface, or put the mark in front of it.
- Don't call Zinc "Ghostty for Windows", or lead with speed claims.

## 14. How the app applies it (decided 2026-10-01, after a side-by-side review)

Where the app differs from the guide above, on purpose:

- **No Geist Mono in the UI.** Window titles say "Zinc" in Segoe UI, like every native Windows app (the
  Settings window too); counts and the command center use the system font. Geist Mono appears only as
  outlined artwork: the wordmark in the About lockup.
- **Attention dots are 8 epx after a title** (6 was easy to miss; 7 is the fallback). On an icon's corner
  (the rail) they stay 9 epx with a ring. Their colors are settings (`[notify] finished-color`,
  `bell-color`, `failed-color`), defaulting to the tokens in section 8. The pulse is the section 9 fade,
  until the tab is viewed.
- **Count pills** take the most urgent tab's color (red beats green), not always `attention`.
- **The welcome page shows the app tile** before "Welcome to Zinc" (the icon people just clicked), not
  the large mark.
- **About's heading is the lockup**, centered, with no tagline under it.