# Writing Zinc's interface

How Zinc talks to people inside the app: settings, menus, notices, the palette, the welcome page and the About page. These rules come from building the settings window and from studying Windows Terminal's (2026-09-30), where the good and the weak parts of its wording were easy to compare side by side. Zinc is a power tool; the aim is still to be easier to understand than the tools it competes with.

**Scope.** This guide covers in-product copy. Marketing, the README, docs and release notes follow the brand voice in `BRAND.md`. The two agree on tone and differ on purpose: the interface helps someone do something right now, and the marketing explains why Zinc is worth switching to.

## Personality

**Calm and precise. Zinc gets out of your way, but notices things for you.**

When a situation isn't covered below (a new notice, a hint, a first-run moment), write it the way that sentence sounds: quiet, exact, and useful.

- **No hype.** No exclamation marks, no "awesome", "blazing" or "magic", no celebratory confirmations. "Saved to config.toml and applied" is the right temperature.
- **No apologies.** Say what happened and what to do; "sorry" adds nothing.

## Voice

- **Write from the user's side of the screen.** Name things by what people recognize, not by how they're built: "Programs may copy", not "OSC 52 write". Put the mechanism in the description ("Copying with OSC 52, as tmux, neovim and remote shells do.") for people who look for it. This is where the interface differs from the marketing: the marketing names the precise detail, and the interface names the familiar thing.
- **Plain, direct, specific.** Short sentences, active voice, no jargon a user wouldn't search for. Prefer "the reset arrow gives one back" to "reverts to the inherited value".
- **Sentence case everywhere**: names, buttons, section titles, menu items. ("Tab width mode", not "Tab Width Mode".) Windows Terminal slips here ("Application Theme", "Add Entry"); we don't. The one exception is a window's title, which follows Windows' own title case: "Zinc Settings".
- **Zinc on its own terms.** Don't compare Zinc to other terminals in the interface ("the way Windows Terminal draws", "keys follow Windows Terminal's defaults"). Name another app only when it affects the person reading: "Windows Terminal uses Win+` for its quake mode" says who took their hotkey.
- **Limitations stay out of the interface.** Don't advertise what Zinc doesn't do yet (no "Ligatures aren't supported yet" next to the font picker); that belongs in the docs and release notes. Mention a limit only when it affects what the person is doing right now, and then name the real cause (see State).

## The product's name

- **"Zinc"**, capitalized, in all interface text. Never "zinc" (that's the wordmark only) and never "Zinc Terminal".
- **Ghostty** is credited where people look for credits: the About page says Zinc is "built on libghostty". Elsewhere in the interface, Ghostty is named only when it affects the person (for example, a Ghostty-format theme they're importing).
- **Brand language stays out of settings.** No phosphor or glow puns, no "lit cell" metaphors in labels or descriptions. The tagline ("Every cell, exactly right.") may appear on the welcome page if it fits the layout, and nowhere else in the interface.

## Names

- **The word people recognize** for a label or badge, even when it's less exact: "Animated", not "moves on its own"; "Visual bell", "Focus follows mouse". The precise meaning (what it costs, how it works) goes in the description or a tooltip.
- **Short noun phrases** for settings: "Font", "Line thickness", "Unfocused panes", "Long command finishes". A long yes/no sentence as a name ("Show a confirmation dialog before closing tabs or windows") is the weakest pattern; the nuance belongs in the description.
- **Positive, even when the config key is negative.** "Pane animations" on, not "Disable animations" off.
- **Say what's experimental in the name**: "Experimental: …". Don't hide it in the description.
- **Count from 1** wherever a person counts: "Go to workspace 3" is the third one. Never show a zero-based index.

## Descriptions

- **Say the consequence, not the mechanism.** "When off, the window resizes smoothly." "Compact shrinks inactive tabs to their icon." A description that restates the name adds nothing; leave it out instead.
- **Say when a change doesn't apply right away**: "Applies to windows opened from now on.", "Restart Zinc to apply." Put it in the description, where it's read before changing it.
- **Recommend when there's a safe answer**: *If your shell doesn't support bracketed paste, choose "Always".*
- **Refer to other settings and choices by their exact label, in quotes**: *Only with the "Vintage" cursor.*

## Choices

- **Choice labels are short** (they're menu items): "Automatic", "Quit", "Stay in the tray".
- **Explain the chosen option under the setting's name** (`SettingChoice.Detail`), not in the label: "Stays in the tray while the quick terminal's hotkey is set; otherwise quits." Only options that need it get a detail.
- **Show, don't describe, where you can**: a cursor shape with its glyph ("Bar ( ┃ )"), a color scheme as a tile in its own colors, a path translation with an example ("WSL (C:\ → /mnt/c)").

## State

- **Show where a value comes from.** A profile's setting reads "Inherited: 11 pt" until the profile has its own; then "Its own. Everywhere else: 11 pt." Never make people hover a disabled button to find out.
- **Mark what's changed, and offer the way back in the same place**: the reset arrow beside a setting's name, shown only while there's something to reset. After a reset, focus moves to the setting's control.
- **Disable, don't hide, when a setting depends on another**, and say why in its description. Hide only what can't apply on this machine.
- **Name the real cause** when something can't be used: "SSH hosts are turned off", not "appears to have been uninstalled".
- **Show the value on anything collapsed**: a swatch and "#1E1E2E", "8, 8, 8, 8", "None".

## Attention and notifications

The attention dot's colours and the words around it say the same thing. Use these verbs, matching the UI tokens in `BRAND.md`:

| State | Colour token | Wording |
|---|---|---|
| Work finished out of view | `attention` | "Finished": "Build finished in workspace 2" |
| A program is waiting on the person | `caution` | "Needs you": *ssh needs you in "prod"* |
| The command that finished out of view failed | `error` | "Failed": "Tests failed (exit 1) in workspace 2" |

- Lead with the command or program, then what happened, then where.
- Include the exit code when something fails; leave it out when it succeeds.
- Windows notifications follow the same wording, with no app-name prefix (Windows already shows it).

## Changes and undo

- **Apply immediately; make undo easy.** Settings write as you change them and say so ("Saved to config.toml and applied"), with "Undo N changes" counting what Undo would put back. No Save/Discard step.
- **Buttons say what they do; reset arrows say where they go.** A text button is a verb phrase that names its object: "Restore the original", "Delete profile", "Open folder", "Open in editor". A reset arrow (icon only) has a tooltip that starts "Back to …": "Back to the default: no shaders".
- **Destructive actions** (delete a profile, reset everything): a button that says what it does, a confirmation that states the consequence ("Removes its section from config.toml."), and a confirm button that repeats the action with its object ("Delete profile", "Reset everything"). Never "OK", "Yes" or "Delete it": a button has to make sense when a screen reader announces it on its own. Put destructive actions last on a page.
- **Errors say what's wrong and what to do**: "config.toml has an error; fix it first (see the top of the page)." No apologies, no vague "something went wrong".

## Punctuation

- **No period** on names, labels, buttons, choice labels or tooltips that are a single phrase.
- **Periods** on descriptions and any text of more than one sentence.
- **Straight double quotes** for quoting labels and names ("Vintage"), and straight apostrophes (Zinc's). Never curly quotes, never single quotes for quoting. Consistency matters more than style, and straight quotes are what everyone types.
- (In this guide, an example that itself contains a quoted label is shown in *italics* instead of quotes.)
- **Code, keys, paths and values** appear exactly as typed, never quoted or recapitalized: `config.toml`, Ctrl+A > C, `C:\Users`.
- **Keyboard shortcuts** use Windows' notation: Ctrl+Shift+T, Win+`.

## Order

- Within a page: what people change most first, technical settings after, destructive actions last (under "Troubleshooting" if there's more than one).
- Group settings that depend on each other visually, and keep a group's main control at its top.

## Checklist for a new setting

1. A short, positive name in sentence case, with no period.
2. A description that says the consequence (or none, if the name says it all), including "applies to new windows" or "restart" if so.
3. Choice labels of a word or two; a detail for any choice that needs explaining.
4. Its config key shown on the card (the settings window does this).
5. Where it's inherited from, if profiles can override it.
6. Read it aloud: calm, no hype, no apology, and no comparison to another terminal.
