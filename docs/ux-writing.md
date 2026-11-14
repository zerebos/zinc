# Writing Zinc's interface

This guide is about the words inside Zinc, which means settings, menus, notices, the command palette, the welcome page, and the About page. The README, the docs, the changelog (which is also the release notes), code comments, and commit messages follow [writing.md](writing.md) instead. Both guides share a tone and the punctuation rules below. They differ because the interface helps someone do something right now, while the docs explain how Zinc works and why.

Zinc is a power tool, but its interface should still be the easiest part of it to understand.

## Personality

Zinc is calm and precise. It gets out of your way, but it notices things for you.

When a case isn't covered here, like a new notice, a hint, or a first-run moment, write it the way that sentence sounds, so it's quiet, exact, and useful.

- No hype. That means no exclamation marks, no "awesome", "blazing", "magic", "simply", or "just", and no celebrating. "Saved to config.toml and applied" is the right temperature.
- No apologies. Say what happened and what to do. "Sorry" adds nothing.

## Voice

Name things from the user's side of the screen. Use the name people recognize, not the name of how it's built. "Programs may copy" is better than "OSC 52 write". The mechanism goes in the description for people who look for it, as in "Copying with OSC 52, as tmux, Neovim, and remote shells do." This is where the interface and the docs differ. The docs name the precise detail, and the interface names the familiar thing.

Write plain, specific sentences. Use the active voice, and no jargon a user wouldn't search for. "The reset arrow gives one back" is better than "reverts to the inherited value". Contractions are fine.

Keep the small words. Write "check that the file can be opened", not "check the file can be opened". Keep "also", "still", "only", and "instead" too. They carry the logic, as in "Nerd Fonts also show prompt icons", so they aren't padding.

Name the thing instead of "it". Write "Shows and hides the quick terminal", not "Shows and hides it". When "it" or "them" could mean more than one thing, say which.

Talk to the person as "you", and call the app "Zinc". Never write "we", because Zinc speaks for itself, not for a team behind it. Most descriptions don't need either, because they start with a verb, like "Expands the focused pane's scrollbar and keeps it expanded."

Use sentence case for names, buttons, section titles, and menu items. Write "Tab width mode", not "Tab Width Mode". The one exception is a window title, which follows Windows' own title case, as in "Zinc Settings".

Keep Zinc on its own terms. Don't compare Zinc with other terminals in the interface. Name another app only when it affects the person reading. "Windows Terminal uses Win+` for its quake mode" is fine, because it tells them who took their hotkey.

Keep limitations out of the interface. Don't advertise what Zinc doesn't do yet, like "Ligatures aren't supported yet" next to the font picker. That belongs in the docs and release notes. Mention a limit only when it affects what the person is doing right now, and then name the real cause (see [State](#state)).

## The product's name

- Write "Zinc", capitalized, in all interface text. "zinc" in lowercase is only the wordmark, and it's never "Zinc Terminal".
- Credit Ghostty where people look for credits. The About page says Zinc is "built on libghostty". Elsewhere, name Ghostty only when it affects the person, for example when they import a Ghostty theme file.
- Keep brand language out of settings. That means no phosphor or glow puns, and no "lit cell" metaphors in labels or descriptions. The tagline may appear on the welcome page if it fits the layout, and nowhere else in the interface.

## Names

A setting's name is the word people recognize, even if it's less exact. Write "Animated", not "moves on its own". "Visual bell" and "Focus follows mouse" are other good names. The precise meaning, like what it costs or how it works, goes in the description or a tooltip.

Names are short noun phrases, such as "Font", "Line thickness", "Unfocused panes", or "Long command finishes". A long yes-or-no sentence as a name ("Show a confirmation dialog before closing tabs or windows") is the weakest pattern, because the nuance belongs in the description.

A few more rules for names:

- Name a setting positively, even when its config key is negative. Write "Pane animations" switched on, not "Disable animations" switched off.
- Start an experimental setting's name with "Experimental:", instead of saying so only in the description.
- Count from 1 wherever a person counts. "Go to workspace 3" means the third one. Never show a zero-based index.

## Descriptions

A description says what changes for the person, not how it works inside. "When off, the window resizes smoothly." and "Compact shrinks inactive tabs to their icon." are both good. If a description would only repeat the name, leave it out.

Write descriptions as full sentences with a subject and a verb. "Expands the focused pane's scrollbar and keeps it expanded" reads better than "The focused pane's scrollbar, expanded and kept so". Join two thoughts with a word that says how they connect, like "because", "so", "which", "when", or "instead of", or split them into two sentences.

A setting's description starts with a verb that says what the setting does, like "Expands the focused pane's scrollbar", or it's a sentence about the setting. An action's description, which the command palette and the key bindings show, is a command instead, like "Copy the selection.", because an action is something the person runs.

Describe how Zinc behaves now, in the present tense. Leave out the history of how a setting came to be.

Spell out shorthand. Write "or", not "/". Write "1 turns it off, 4.5 is the usual accessibility level, and 21 makes all text black or white", not "1: off, 4.5: usual". A rule in parentheses becomes its own sentence, as in "If the scheme doesn't set one either, it's the background color." A short parenthesis that only gives examples can stay, as in "(Cascadia Code, JetBrains Mono, Fira Code)".

A description also says these things:

- When a change doesn't apply right away, as in "Applies to windows opened from now on." or "Restart Zinc to apply." This goes in the description, so it's read before the change is made.
- What to choose, when there's a safe answer, as in *If your shell doesn't support bracketed paste, choose "Always".*
- Other settings and choices, by their exact label in quotes, as in *Only with the "Vintage" cursor.* For something outside Zinc, say where it is too, as in *while "Animation effects" is off in Windows Settings*.

## Choices

Choice labels are menu items, so keep them to a word or two, like "Automatic", "Quit", or "Stay in the tray".

When a choice needs explaining, the explanation goes under the setting's name while that choice is picked (`SettingChoice.Detail`), not in the label. For example, "While the quick terminal's hotkey is set, Zinc stays in the tray so the hotkey keeps working. Otherwise it quits." Only choices that need one get a detail.

Show instead of describing where you can. A cursor shape comes with its glyph ("Bar ( ┃ )"), a color scheme is a tile in its own colors, and a path translation shows an example ("WSL (C:\ → /mnt/c)").

## State

- Show where a value comes from. A profile's setting reads "Inherited from Appearance, where it's 11 pt." until the profile has its own value, and then "Changed here. In Appearance, it's 11 pt." Collapsed groups use the same two words, "Inherited" and "Changed here". Never make people hover over a disabled control to find out.
- Mark what's changed, and offer the way back in the same place. The reset arrow sits beside a setting's name and appears only while there's something to reset. After a reset, focus moves to the setting's control.
- When a setting depends on another, disable it instead of hiding it, and say why in its description. Hide only what can't apply on this machine.
- When something can't be used, name the real cause and whose limit it is. Write "SSH hosts are turned off", not "appears to have been uninstalled".
- Show the value on anything collapsed, like a swatch with "#1E1E2E", "8, 8, 8, 8", or "None".

## Attention and notifications

The attention dot's color and the words around it say the same thing. Use these words, which match the colors in `src/Zinc/Brand.cs`.

| State | Color in `Brand.cs` | Wording |
|---|---|---|
| Work finished out of view | `AttentionFor` | "Finished", as in "Build finished in workspace 2" |
| A program is waiting for the person | `CautionFor` | "Needs you", as in *ssh needs you in "prod"* |
| A command failed out of view | `ErrorFor` | "Failed", as in "Tests failed (exit 1) in workspace 2" |

Start with the command or program, then say what happened, then where. Include the exit code when something fails, and leave it out when it succeeds. Windows notifications use the same words, without "Zinc" in front, because Windows already shows the app's name.

## Changes and undo

Settings apply as soon as they change, and say so with "Saved to config.toml and applied". "Undo N changes" counts what Undo would put back. There's no Save or Discard step.

A text button is a verb phrase that names its object, like "Restore the original", "Delete profile", "Open folder", or "Open in editor". Never write "OK", "Yes", or "Delete it", because a button has to make sense when a screen reader announces it on its own. A reset arrow has no text, so its tooltip says where it goes, starting with "Back to", as in "Back to the default: no shaders".

Destructive actions, like deleting a profile or resetting everything, need three things:

- a button that says what it does
- a confirmation that states the consequence ("Removes its section from config.toml.")
- a confirm button that repeats the action with its object ("Delete profile", "Reset everything")

Put destructive actions last on a page.

Errors say what's wrong and what to do, as in "config.toml has an error. Fix it first. The error is shown at the top of this page." No apologies, and never a vague "something went wrong".

## Punctuation

- No period on names, labels, buttons, choice labels, or single-phrase tooltips.
- A period on descriptions, and on any text of more than one sentence.
- Colons only introduce a list or code, separate a label from its value ("Color: automatic", "Inherited: 11 pt"), or lead into a reset arrow's destination ("Back to the default: no shaders"). A colon never stands in for "because" or "so", and text never opens with one.
- No semicolons. Write two sentences instead, as in "Zinc shows a dot on the pane's tab. The taskbar button also flashes if Zinc is in the background."
- A comma before the last item in a list of three or more (the serial comma), as in "Tabs, splits, and folders".
- An ellipsis has three uses, and Windows uses all three. It ends a command that asks for more before it acts ("Browse…", "Workspace folder…"), it ends a status for work that's still going ("Checking for updates…"), and it shows where text was cut to fit. Never use one as punctuation in a sentence.
- Use straight double quotes for quoting labels and names ("Vintage"), and straight apostrophes (Zinc's). Never use curly quotes, and never use single quotes for quoting. (In this guide, an example that itself contains a quoted label is shown in *italics* instead.)
- Code, keys, paths, and values appear exactly as typed, never recapitalized, like `config.toml`, Ctrl+A > C, and `C:\Users`.
- Product text is plain, with no code style. So when a word someone types in config.toml, like an action, a key chord, or a value such as none or last, sits in a sentence where it could read as an ordinary word, put it in double quotes, as in *A key bound to "none" goes to the program instead.* File names, paths, and keyboard shortcuts stay bare, because they can't be misread. Config warnings quote what the person wrote the same way, as in *unknown action "teleport"*.
- Keyboard shortcuts use Windows' notation, like Ctrl+Shift+T and Win+`.
- No em dashes and no exclamation marks.

## Order

Within a page, put what people change most first, technical settings after, and destructive actions last. If there's more than one destructive action, they go under "Troubleshooting".

Group settings that depend on each other, and keep a group's main control at its top.

## Checklist for a new setting

1. A short, positive name in sentence case, with no period.
2. A description in full sentences that says what changes, or none if the name already says it. It mentions "applies to new windows" or "restart" when that's true.
3. Choice labels of a word or two, plus a detail for any choice that needs one.
4. Its config key shown on the card. The settings window does this.
5. Where its value is inherited from, if profiles can override it.
6. Read it aloud. It should sound calm, with no hype, no apology, no shorthand, and no comparison with another terminal.
