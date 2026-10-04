# Zinc's own shaders

Custom shaders written for Zinc (so they ship with it), copied to `Shaders\` next to the exe and used by name in
`[shaders] files` (`ShaderPaths`): `files = ["crt", "glow"]`. A file of the same name in a `shaders` folder next
to config.toml takes the place of one of these.

Each starts with `// Zinc: <name>. <one sentence>.`, which Settings shows in its Add menu (ShaderTests checks it,
that each compiles, and that only `crt` and `cursor-trail` use time: the rest draw only when the terminal changes).
Settings to tweak are the constants at the top of each file. Positions are in pixels with y going down.
