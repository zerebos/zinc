# Writing Zinc's interface

How Zinc talks to people: settings, menus, notices, the palette, the welcome page. These
rules come from building the settings window and from studying Windows Terminal's (2026-09-30),
where the good and the weak parts of its wording were easy to compare side by side. Zinc is a
power tool; the aim is still to be easier to understand than the tools it competes with.

## Voice

- **Write from the user's side of the screen.** Name things by what people recognize, not by how
  they're built: "Programs may copy", not "OSC 52 write". Put the mechanism in the description
  ("Copying with OSC 52, as tmux, neovim and remote shells do.") for people who look for it.
- **Plain, direct, specific.** Short sentences, active voice, no jargon a user wouldn't search for.
  Prefer "the reset arrow gives one back" to "reverts to the inherited value".
- **Sentence case everywhere**: names, buttons, section titles, menu items. ("Tab width mode", not
  "Tab Width Mode".) Windows Terminal slips here ("Application Theme", "Add Entry"); we don't. The one
  exception is a window's title, which follows Windows' own title case: "Zinc Settings".
- **Zinc on its own terms.** Don't compare Zinc to other terminals in the interface ("the way Windows Terminal
  draws", "keys follow Windows Terminal's defaults"). Name another app only when it affects the person reading:
  "Windows Terminal uses Win+` for its quake mode" says who took their hotkey.

## Names

- **The word people recognize** for a label or badge, even when it's less exact: "Animated", not "moves on its
  own"; "Visual bell", "Focus follows mouse". The precise meaning (what it costs, how it works) goes in the
  description or a tooltip.
- **Short noun phrases** for settings: "Font", "Line thickness", "Unfocused panes", "Long command
  finishes". A long yes/no sentence as a name ("Show a confirmation dialog before closing tabs or
  windows") is the weakest pattern; the nuance belongs in the description.
- **Positive, even when the config key is negative.** "Pane animations" on, not "Disable
  animations" off.
- **Say what's experimental in the name**: "Experimental: …". Don't hide it in the description.
- **Count from 1** wherever a person counts: "Go to workspace 3" is the third one. Never show a
  zero-based index.

## Descriptions

- **Say the consequence, not the mechanism.** "When off, the window resizes smoothly." "Compact
  shrinks inactive tabs to their icon." A description that restates the name adds nothing; leave it
  out instead.
- **Say when a change doesn't apply right away**: "Applies to windows opened from now on.",
  "Restart Zinc to apply." Put it in the description, where it's read before changing it.
- **Recommend when there's a safe answer**: "If your shell doesn't support bracketed paste, choose
  Always."
- **Refer to other settings and choices by their exact label, in quotes**: "Only with the
  'Vintage' cursor."

## Choices

- **Choice labels are short** (they're menu items): "Automatic", "Quit", "Stay in the tray".
- **Explain the chosen option under the setting's name** (`SettingChoice.Detail`), not in the label:
  "Stays in the tray while the quick terminal's hotkey is set; otherwise quits." Only options that
  need it get a detail.
- **Show, don't describe, where you can**: a cursor shape with its glyph ("Bar ( ┃ )"), a color
  scheme as a tile in its own colors, a path translation with an example ("WSL (C:\ → /mnt/c)").

## State

- **Show where a value comes from.** A profile's setting reads "Inherited: 11 pt" until the profile
  has its own; then "Its own. Everywhere else: 11 pt." Never make people hover a disabled button to
  find out.
- **Mark what's changed, and offer the way back in the same place**: the reset arrow beside a
  setting's name, shown only while there's something to reset. After a reset, focus moves to the
  setting's control.
- **Disable, don't hide, when a setting depends on another**, and say why in its description.
  Hide only what can't apply on this machine.
- **Name the real cause** when something can't be used: "SSH hosts are turned off", not "appears to
  have been uninstalled".
- **Show the value on anything collapsed**: a swatch and "#1E1E2E", "8, 8, 8, 8", "None".

## Changes and undo

- **Apply immediately; make undo easy.** Settings write as you change them and say so ("Saved to
  config.toml and applied"), with "Undo N changes" counting what Undo would put back. No
  Save/Discard step.
- **Buttons say what they do; reset arrows say where they go.** A text button is a verb: "Restore the original",
  "Delete it", "Open folder", "Open in editor". A reset arrow (icon only) has a tooltip that starts "Back to …":
  "Back to the default: no shaders".
- **Destructive actions** (delete a profile, reset everything): a button that says what it does, a
  confirmation that states the consequence ("Removes its section from config.toml."), and a confirm
  button that restates the action ("Delete it"). Put them last on a page.
- **Errors say what's wrong and what to do**: "config.toml has an error; fix it first (see the top
  of the page)." No apologies, no vague "something went wrong".

## Order

- Within a page: what people change most first, technical settings after, destructive actions
  last (under "Troubleshooting" if there's more than one).
- Group settings that depend on each other visually, and keep a group's main control at its top.

## Checklist for a new setting

1. A short, positive name in sentence case.
2. A description that says the consequence (or none, if the name says it all), including "applies
   to new windows" or "restart" if so.
3. Choice labels of a word or two; a detail for any choice that needs explaining.
4. Its config key shown on the card (the settings window does this).
5. Where it's inherited from, if profiles can override it.
