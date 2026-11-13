# Contributing to Zinc

Thanks for wanting to help. Zinc has one maintainer, so this page is mostly about making sure your time and mine go to things that will land.

## Before you start

Bug fixes and small changes can go straight to a pull request. For a new feature or a big refactor, open an issue or start a discussion first and say what you have in mind. A feature can be a good idea and still not fit Zinc, and it's better to find that out before you've written it.

If you're fixing a bug nobody has reported yet, a short issue alongside the pull request helps too, because it says what was wrong in a user's words.

## Reporting a bug

Use the bug report form. It asks for the version from Settings > About, your Windows build, and the shell you were using, because most of Zinc's bugs depend on at least one of those.

A debug log helps more than anything else. Turn it on in Settings > About > Troubleshooting, make the bug happen, then copy the log from the same place and paste it into the report. For a pointer that flickers or shows the wrong shape, also run "Trace the pointer (15 s)" from the command palette while it happens, because that's the only way to see those bugs in a log.

Security problems don't go in public issues. [SECURITY.md](SECURITY.md) says where they go.

## Working on the code

[docs/development.md](docs/development.md) covers what to install, building, running a dev build next to an installed Zinc, and testing. [docs/architecture.md](docs/architecture.md) explains how Zinc works inside and the rules the code relies on. Read the section for the area you're changing first, because most of those rules exist to stop a bug that's already happened once.

Before you open a pull request:

- `dotnet build Zinc.slnx --no-incremental` has 0 warnings, and `dotnet test` passes. CI checks both.
- If your change makes a section of `docs/architecture.md` or `docs/development.md` wrong, fix the section in the same pull request.
- A change users would notice gets a line in `CHANGELOG.md` under `## Unreleased`.
- Text in the app (labels, settings descriptions, notices, errors, `zn`'s output) follows [docs/ux-writing.md](docs/ux-writing.md). The README, the docs, the changelog, and code comments follow [docs/writing.md](docs/writing.md), which shares its punctuation rules.
- If the change touches the layout or the window's chrome, say which settings you tried it with. `docs/development.md` lists the ones that change the chrome.
- Say what you couldn't test. Hover, the pointer's shape, and real mouse input can't be scripted, and that's fine, as long as the pull request says so.

The code style is in [docs/development.md](docs/development.md#code-style).

## AI use

AI tools are welcome, as long as a person stands behind and understands every contribution. [AI_POLICY.md](AI_POLICY.md) says when to mention that you used AI, what to check before you send something, and why pull requests that ignore it are closed without review.

## Commit messages

You don't have to match a style. For reference, the history uses short, lowercase subjects in the imperative, with no trailing period, like `add the config schema` or `fix powerline triangles`, and a body only when the reason isn't obvious. A squashed pull request may get its message reworded to fit when it's merged.

## License

Zinc is licensed under [Apache-2.0](LICENSE), and contributions come in under the same license (its section 5). There's nothing extra to sign.
