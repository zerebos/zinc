# Security

## Reporting a problem

Please don't report a security problem in a public issue. Use GitHub's private reporting instead, from the [Security tab](https://github.com/zerebos/zinc/security/advisories/new). Only the maintainer sees the report, and you can talk it through there.

Say what's affected, how to reproduce it, and the version from Settings > About. A debug log helps, but check it first, because it can contain folder names and window titles from your machine.

## Supported versions

Fixes go into the latest release. Zinc updates itself, so that's the version almost everyone has. Older releases and the `0.1.0-dev` test builds don't get fixes.

## What counts

Anything that lets a program, a file or a web page do more than it should through Zinc is worth reporting. Examples include a terminal sequence that runs a command, reads the clipboard without asking, or writes a file, a link that opens something it shouldn't, and `zn`'s pipe accepting commands from another user. Zinc's own update path and installer count too.
