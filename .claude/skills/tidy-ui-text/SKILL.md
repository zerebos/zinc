---
name: tidy-ui-text
description: Rewrite text shown in the product (settings, menus, notices, the palette, zn's output) in the current diff, or in the files given, to docs/ux-writing.md. Wording only; keeps tests, README and docs in step.
argument-hint: "[file or folder ...]  (default: UI text added or changed in the uncommitted diff)"
disable-model-invocation: true
---

# Tidy UI text

Rewrite text people see in Zinc so it follows `docs/ux-writing.md`. Read the whole guide first and follow it over
anything here. This skill adds what a pass over existing text needs: what to look for, what to leave, and what else
has to change with a string.

## Scope

- No arguments: only the UI strings added or changed in `git diff HEAD` (staged and unstaged).
- Files or folders given: every UI string in them. For more than a few files, do one file at a time and report as
  you go.
- UI text means what a person can read in the app or in zn's output: setting names, descriptions and choice details
  (`SettingsCatalog`), labels, buttons, tooltips, `AutomationProperties.Name`, notices and errors, palette titles
  (`Actions.cs`), the welcome and About pages, zn's messages.
- Not UI text: `Log.Write` lines, exception messages that only reach the log, config keys and values, test data,
  comments (that's `/tidy-comments`).

## For each string

Find where it's shown before rewriting it, because a label, a description and an error follow different rules in
the guide. Then decide:

1. **Leave it.** It already follows the guide. Most strings should end up here. Don't rewrite a string only to
   make it longer.
2. **Reword it.** Watch for these signs:
   - a colon standing in for "because", "so" or "use it for"
   - any semicolon (two sentences instead)
   - dropped words ("check the file can be opened")
   - a vague "it" or "them"
   - the mechanism named where the familiar thing should be (see the guide's Voice section)
   - several ideas packed into one sentence
3. **Ask.** If you can't tell from the code what the string should say, or a fix would change what the setting
   means to the person, list it for the user instead of guessing.

Keep these colons, which the guide allows: before a list, between a label and its value ("Color: automatic",
"Empty: removed for these programs"), and in a reset arrow's tooltip ("Back to the default: no shaders").

## Before changing a string

Search the repo for its exact text (and for a distinctive part of it, if it's built from pieces): `tests/`, README,
`docs/`, `CHANGELOG.md` and `.scratch/tools/`. Then change every copy together:

- A test that asserts the string is updated with it. A palette title is also in the README's action table, which a
  test checks, and test scripts may run it by name (`palette:<exact title>`).
- README and docs that quote it are updated to match. `docs/ux-writing.md` quotes some real strings as examples, so
  keep those in step too.
- CHANGELOG entries for past releases stay as they are.

Keep format placeholders, interpolations, `{0}`, keyboard notation and anything the guide says appears exactly as
typed. A string split across lines or built with `+` keeps its structure.

## When done

- If any wording changed in a way users would notice, add one line under `## Unreleased` in CHANGELOG.md, such as
  "Clearer wording in settings descriptions." One line for the whole pass, not one per string.
- String changes can break tests, so follow CLAUDE.md's flow: `dotnet build` and `dotnet test`, then stage exactly
  these files and propose a commit message. Don't commit.

## Report

End with a short list: each string you changed (`file:line`, old, new), other files changed to match (tests,
README, docs), and anything you left for the user to decide.
