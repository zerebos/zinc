# Zinc's own color schemes

Lit Cell and Lit Cell Light, from the brand. Together they're the default, with Lit Cell while Windows is dark and Lit Cell Light while it's light (`ThemeLibrary.ZincPair`). Lit Cell is also `TerminalTheme.Default`, the colors used when the scheme files are missing, and a test keeps the two equal. They're kept apart from `assets/themes`, because `scripts/dev/update-themes.ps1` replaces that folder wholesale. Both folders are copied to `Themes\` next to the exe.
