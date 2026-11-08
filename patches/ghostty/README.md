# Ghostty patches

Fixes to Ghostty that Zinc needs and that aren't upstream yet. `scripts/build/libghostty.ps1` resets the Ghostty
checkout and applies every `*.patch` here before it builds. Each patch's header says what upstream change makes it
unnecessary.

These patches are offered under Ghostty's MIT license, not Zinc's Apache-2.0, so they can go upstream as they are.
