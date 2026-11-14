Zinc {version}
A native terminal for Windows, organized around how you actually work. It's built on libghostty, the
same core that powers Ghostty.

Website: https://zincterminal.dev
Source and releases: https://github.com/zerebos/zinc

GETTING STARTED
  The first start asks a few questions (how you like to work, colors, your default shell).
  Settings: Ctrl+, or the command palette (Ctrl+Shift+P).

PORTABLE ZIP
  Zinc-win-Portable.zip runs from any folder. Unzip all of it (right-click, Extract All) and open
  zinc.exe. If nothing happens, a file didn't make it out of the zip, so extract it again with
  Extract All. The portable copy doesn't update itself. Get a new zip from the releases page instead.

UPDATES
  An installed copy checks for a new version once a day and downloads it in the background. A notice
  then offers Restart now. Choose Later and it installs the next time you quit Zinc.
  Settings > About > Updates has Check for updates and the switch that turns the daily check off.

WHERE ZINC KEEPS THINGS
  Settings          %APPDATA%\Zinc\config.toml (Settings > About > Config file opens it)
  Your themes       %APPDATA%\Zinc\themes\
  Logs, crash log   %APPDATA%\Zinc\
  Uninstalling leaves these, so a reinstall picks up where you were.

REPORTING A PROBLEM
  Open an issue at https://github.com/zerebos/zinc/issues. A debug log helps most. Turn it on in
  Settings > About > Troubleshooting, make the problem happen, then copy the log into the issue.
  Check it first, because it can contain folder names from your PC. If Zinc crashed, crash.log is in
  %APPDATA%\Zinc. Security problems go through the Security tab on GitHub instead, privately.

GOOD TO KNOW
  "Default terminal app" (Settings > Layout) opens console programs in Zinc instead of a console
  window. It needs Windows Terminal installed, which provides the other half.

REMOVING ZINC
  An installed copy: Settings > Apps > Zinc > Uninstall. It also stops Zinc starting with Windows and
  gives the default terminal back. A portable copy: delete its folder. To remove your settings and
  logs too, delete %APPDATA%\Zinc afterward.
