# Zinc — Brand Guidelines

> This is the Zinc repository's copy of the brand guide. The canonical copy, with the exported artwork, is `BRAND.md` in the `zinc-brand` folder. The two match except for section 12, which here lists this repository's files.

This is the source of truth for Zinc's name, identity, in-app branding and bundled color schemes. It is written for implementation: wherever a value matters, the exact value is given.

## 1. The idea

A terminal is a grid of cells, and everything it shows comes from deciding, cell by cell, what is lit. Zinc's identity takes that literally. **Lit cells on a dark phosphor screen** is the whole visual language: the mark is built from whole cells, the app icon is a little dot-matrix screen, and in-app accents behave like lit pixels.

The name ties in quietly. Copper-doped zinc sulfide is the classic phosphor, and it glows green. The icon tells the "screen" story visually, so the name doesn't have to explain it.

## 2. Name

- **Zinc**, capitalized, in prose. The wordmark is always lowercase: `zinc`.
- App executable: `Zinc.exe`. CLI: **`zn`**. Zn is zinc's chemical symbol, so the command echoes the same story as the name and the icon. (Windows can't give one GUI executable a console, so the two are separate binaries, the same split as VS Code's `Code.exe` and `code`.)
- Website: **zincterminal.dev**. The domain needs "terminal" to be findable; the product name doesn't.
- Never "Zinc Terminal" as the product name. Where context is needed, use the descriptor: "Zinc, a precise, native terminal for Windows."
- Ghostty lineage line (About page, README): *"Zinc: what makes ghosts glow in the dark."* This is a story nod to libghostty, never a visual one.

## 3. Positioning

**One line:** A precise, native terminal for Windows, built on Ghostty's core, organized around how you actually work.

**Tagline:** *Every cell, exactly right.*

Claims come in a fixed order. Lead with the first; never lead with the last two.

1. **Precision and craft: the headline.** *Precise in what it draws, and in how you tune it.*
   - **In what it draws:** pixel-exact box drawing, blocks and Powerline shapes that meet without seams in any font; synchronized output; correct rendering of every program's output.
   - **In how you tune it:** hundreds of settings, each one explained in plain words, applied the moment you change it, and one click from undo. Lead with the clarity, which is the distinctive part; the depth ("hundreds of settings") is the supporting fact.
2. **Workflow: the reason to stay.** Workspaces that reopen where you left them, automatic shell integration and the attention system. "A terminal that knows what your commands are doing."
3. **Native Windows polish: the proof of care.** WinUI 3, Mica, tray, global hotkey, per-DPI icons, automatic shell discovery.
4. **Speed: a given, not a headline.** No "fastest" or comparative speed claims until benchmarks are published.
5. **Customization depth: show it, don't claim it.** Power users who tune ("rice") their setups respond to seeing them, so use screenshots of tuned Zinc windows and a theme gallery, not the word "flexible". Avoid counting settings against other terminals: it's a moving target, and it frames Zinc against them rather than on its own terms.

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
- Never borrow Ghostty's visuals: no ghosts, its icon style or its colors.
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
- Flat treatment: solid `#3DF2A0` on dark; `#0B7A4B` on light. Ink `#0F1311` is for one-color reproduction only.
- Optional glow, on lit cells only: a rounded square 1.5× the pitch, radius 50% of the pitch, `#3DF2A0` at ~6% opacity.

**Unlit dot (screen texture):** a circle with radius **20% of the pitch**, `#3DF2A0` at **10%**, in every grid cell not occupied by a lit cell. The grid must align with the mark.

## 6. App icon: the screen tile

The tile *is* the phosphor screen. There is no bezel. Master geometry in a 240 × 240 viewBox:

| Layer | Spec |
|---|---|
| Tile | Rounded rect, full size, `rx = 52`; clips everything inside |
| Screen | Radial gradient at `cx 0.35, cy 0.28, r 0.95`: `#113623` (0) → `#07180F` (0.55) → `#020805` (1) |
| Dot grid | Edge to edge, pitch 10, aligned to the mark |
| Mark | Centered, about 50% of the tile, 6 × 6 cells per block at pitch 10 |
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

## 8. Color

### Brand palette
| Name | Hex | Role |
|---|---|---|
| Phosphor green | `#3DF2A0` | Signature color; the mark and accents on dark |
| Lit gradient | `#C6FFD0` → `#2FE07A` | Rich lit cells only |
| Screen | `#0F1311` | Dark ground; ink for the mark on light |
| Paper | `#EDEFEA` | Light ground; wordmark on dark |
| Amber | `#FFB000` | Caution only. Never in the mark |

### UI tokens (app chrome)
Brand colors apply to the **app chrome only**. They never touch terminal content: the user's color scheme always wins. Contrast ratios are measured against Mica (≈ `#202020` dark, ≈ `#F3F3F3` light).

| Token | Dark | Light | Use |
|---|---|---|---|
| `accent` | `#3DF2A0` (11.2:1) | `#0B7A4B` (4.9:1) | Fills: focus, selected tab, active workspace, primary buttons, switches |
| `accent-text` | `#86FFC6` (15.0:1) | `#09663F` (6.9:1) | Accent text and links (WinUI's `AccentTextFillColorPrimary`) |
| `accent-subtle` | accent @16% | accent @12% | Hover and selected backgrounds |
| `attention` | `#3DF2A0` | `#0B7A4B` | Attention dot, workspace counts |
| `caution` | `#FFB000` (8.9:1) | `#9A5B00` (4.9:1) | Needs-you states: close-while-running, awaiting input, bell |
| `error` | `#FF6B6B` (5.9:1) | `#B42A28` (5.7:1) | Failed-command marks, errors (red, never amber) |
| `on-accent` | `#0F1311` | `#FFFFFF` | Text and icons on filled accent |

- Offer a setting to follow the Windows accent color; the brand green is the default (`[window] accent`).
- `accent-text` is Lit Cell's bright green on dark and Lit Cell Light's green on light, so accent text matches terminal output.
- The website's design tokens carry the full set, including WinUI's own surface and text colors for app mock-ups. When they disagree with this table, this table wins and the tokens get fixed.
- Chrome is nearly colorless unless something is focused, selected or asking for attention.
- Everything text-bearing meets WCAG AA (4.5:1). Recheck any new surface.

## 9. Brand inside the app

**The attention dot is a lit cell.**
- Shape: rounded square, corner radius 12% of its size. Never a circle.
- Size: 6 epx on tabs; 8 epx next to workspace counts.
- Color: `attention` when work finished out of view or a program sends a notification; `caution` when a program is waiting on the user; `error` when the command that finished out of view failed.
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
- **Scope:** this applies to brand lockups (the Geist Mono wordmark with the Quadrant mark, as on the site, README and welcome page). OS-native pairings of the app icon and the name in the system font (title bar, taskbar, Start, installer, Settings > Apps) follow Windows' own icon-then-name layout. That's correct, not a violation.
- The mark's height equals the ascender height (baseline to the top of the `h`), and it sits on the baseline.
- The gap is half a monospace character's advance, measured from the end of the text.
- Clear space is 25% of the ascender height on every side, measured from visible ink.
- **Dark lockup:** Paper wordmark, rich mark.
- **Light lockup:** Screen-ink wordmark, mark in the light-theme accent `#0B7A4B` (bright block 100%, dim block 50%). The mark stays green on light grounds.
- **Mono lockup** (`quadrant-zinc-mono.svg`): everything in ink, for one-color print, embossing or stamps only. It reads as grayscale, so never use it as the everyday light version.

## 11. Bundled color schemes: Lit Cell

Zinc ships two original schemes built from the identity. **Lit Cell is the default dark scheme and Lit Cell Light the default light scheme.** Users can switch to any other scheme, which is consistent with §8: these are content schemes users choose, not brand colors forced onto content.

Lit Cell has its own repository, **[zerebos/lit-cell](https://github.com/zerebos/lit-cell)** (MIT), and that is the source of truth for every value: design tokens in `tokens/`, a spec of what each color is for (`SPEC.md`), and ports for other terminals. It is also in iTerm2-Color-Schemes. Don't copy scheme values into other documents; link to the repository, or generate from its tokens.

The anchors, for reference:

| | Lit Cell | Lit Cell Light |
|---|---|---|
| Background | `#0C110E` | `#F1F4EF` |
| Foreground | `#CFDDD3` | `#1B2620` |
| Cursor | `#3DF2A0` | `#0B7A4B` |

**Scheme design notes**
- Neutrals carry a slight green cast; that is what makes these schemes distinctive.
- Every text color meets WCAG AA (4.5:1) against its background, and the Lit Cell build checks it. In Light, white and bright white are fills only.
- Red, green and yellow line up with the UI's `error`, `accent` and `caution` tokens, so a failure looks the same in the terminal and in the chrome.
- Both were checked against every scheme in iTerm2-Color-Schemes (CIEDE2000). Neither is a copy or near-copy; the closest are After Hours (6.1) for dark and Warm Burnout Light (6.8) for light.

## 12. Assets

| Path | Use |
|---|---|
| `assets/brand/icon/quadrant.ico` | Final app icon, 16–256 px, hand-placed small frames |
| `assets/brand/icon/quadrant-{16..256}.png` | Per-size masters (MSIX assets, title bar, tray) |
| `assets/brand/mark/quadrant-app-tile.svg` | Vector tile master (96 px and up) |
| `assets/brand/mark/quadrant-flat.svg`, `-glow.svg`, `-ink.svg`, `-rich.svg` | Mark treatments |
| `assets/brand/mark/quadrant-construction.png` | Construction sheet |
| `assets/brand/lockups/quadrant-zinc-dark.svg` | Dark lockup: Paper wordmark, rich mark (outlined) |
| `assets/brand/lockups/quadrant-zinc-light.svg` | Light lockup: ink wordmark, green mark (outlined) |
| `assets/brand/lockups/quadrant-zinc-mono.svg` | One-color lockup, for print only |
| `assets/brand/lockups/*@4x.png` | Raster copies of the lockups |
| `src/Zinc/Assets/Zinc.ico` | The app's icon: a copy of `quadrant.ico` |
| `assets/zinc-themes/` | Lit Cell and Lit Cell Light, bundled with the app, copied from the Lit Cell repository |
| `docs/ux-writing.md` | How Zinc's interface is written |

These are copies of the exports in `zinc-brand`, without their C2PA provenance block. After copying new exports in, run `python scripts/strip-c2pa.py` (lossless; it checks every file renders the same).

## 13. Don't

- Don't add a `>_` prompt, chevrons, ghosts or gears to the mark.
- Don't separate or overlap the Quadrant blocks, or swap which one is bright.
- Don't put anything but lit cells in the mark: no smooth shapes, letterforms or diagonals.
- Don't add glow to anything but lit cells, and use no bloom, blur, scanlines or CRT curvature.
- Don't let brand colors touch terminal content (bundled schemes are fine; they're user-selectable).
- Don't use amber in the mark or for failures.
- Don't downscale the vector tile for 64 px and below; use the masters.
- Don't set the wordmark in capitals or another typeface, or put the mark in front of it (OS-native icon-and-title layouts excepted; see §10).
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