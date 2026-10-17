---
name: tidy-comments
description: Rewrite code comments in the current diff, or in the files given, to the standard in CLAUDE.md's "Code comments" section. Comments only; never changes code.
argument-hint: "[file or folder ...]  (default: comments added or changed in the uncommitted diff)"
disable-model-invocation: true
---

# Tidy comments

Rewrite comments so a new reader can follow them on the first read. The standard is the "Code comments" section of
CLAUDE.md, so read it first and follow it over anything here.

## Scope

- No arguments: only the comments added or changed in `git diff HEAD` (staged and unstaged). Leave comments the
  diff didn't touch alone, even ones next to it.
- Files or folders given: every comment in them. For more than a few files, do one file at a time and report as you go.
- `//`, `///` and `/* */` comments in C#, plus comments in scripts (`#`) when a script is named. Not strings, not
  log messages, not UI text (that follows `docs/ux-writing.md`).

## For each comment

Read the code it describes before rewriting it, because the meaning has to come from the code, not from the old
wording. Then decide:

1. **Leave it.** It's already clear, including short fragments and casual ones. Most comments should end up here.
   Don't rewrite a comment only to make it longer or more formal.
2. **Delete it.** It only restates the code, so the line below says the same thing without it. Keep `<summary>`
   doc comments even when they're thin (they show up in IntelliSense), and suggest a better one in the report instead.
3. **Reword it.** Watch for these signs:
   - colons or semicolons standing in for "because", "so", "meaning" or "for example"
   - dropped words ("noted before a rebuild", "the card less the terminal")
   - vague references ("that one's", "it" when there are two candidates)
   - bug history told in the past tense instead of what the code prevents
   - odd word choices ("less" for minus, "follows from", "noted")
   - several ideas packed into one sentence
4. **Move the story out.** If a comment narrates what broke, what was tried, measurements or dates, keep one short
   clause about the risk in the comment and put the rest in the matching section of `.scratch/internals.md`. Check
   that section first: often the story is already there, and the comment only needs trimming.
5. **Ask.** If you can't tell what the comment means from the code, don't guess. List it for the user instead.

Keep every fact the comment states unless it's wrong. If it's wrong, say so in the report and don't silently fix it.

## Editing

- Change comments only. If a rewrite seems to need a code change, mention it in the report.
- Wrap at about 120 columns (or the file's own width, if it clearly uses another) and keep its indentation. Running
  a few characters over, up to about 125, is fine when it saves a line, for example when only a word or two would
  spill onto the next one. Don't rewrap a comment only to bring it under 120.
- Use the Edit tool, which keeps each file's line endings (the repo mixes LF and CRLF).
- Don't build, stage or commit. Comment-only edits don't need a build, and the user's normal flow handles the rest.

## Report

End with a short list: each comment you changed (`file:line`, old, new, one line each when they fit), each one you
deleted, anything moved to internals.md, and anything you left for the user to decide.
