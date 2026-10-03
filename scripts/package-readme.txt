Zinc {version}
A precise, native terminal for Windows, built on Ghostty's core. Private alpha: thanks for trying it.

INSTALL
  Run Zinc-win-Setup.exe. There's no admin prompt and nothing to choose: Zinc installs for you only,
  adds itself to the Start Menu and starts. Uninstall it from Settings > Apps like any other app.
  Needs Windows 10 version 2004 or later, or Windows 11, on an x64 PC.

  Zinc isn't signed yet, so Windows may say "Windows protected your PC". Select More info, then
  Run anyway.

  Coming from the zip version? Quit it first (right-click its tray icon, Quit Zinc), then run the
  installer. Your settings carry over; delete the old unzipped folder afterwards.

UPDATES
  Zinc checks for a new version once a day and downloads it in the background. A notice then offers
  Restart now; choose Later and it installs the next time you quit Zinc. Settings > About > Updates
  has Check for updates and the switch to turn the daily check off.

PORTABLE ZIP
  Prefer no installer? Zinc-win-Portable.zip runs from any folder: unzip all of it (right-click,
  Extract All) and open zinc.exe. If nothing happens, a file didn't make it out of the zip: extract it
  again with Extract All.

The first start asks a few questions (how you like to work, colors, your default shell).
Settings: Ctrl+, or the command palette (Ctrl+Shift+P).

WHERE ZINC KEEPS THINGS
  Settings          %APPDATA%\Zinc\config.toml (Settings > About > Config file opens it)
  Your themes       %APPDATA%\Zinc\themes\
  Logs, crash log   %APPDATA%\Zinc\
  Uninstalling leaves these, so a reinstall picks up where you were.

SENDING FEEDBACK
  Something wrong: Settings > About > Debug log, turn it on, make the problem happen, then Copy,
  and paste it with your note. If Zinc crashed, crash.log is in %APPDATA%\Zinc.
  Slow drawing: Settings > About > Drawing benchmark, Run (check "Also with software rendering"
  too if you have a minute), then Copy the results.
  Screenshots help, and so does saying which shell you use (PowerShell, WSL, Git Bash, ...).

GOOD TO KNOW
  - "Default terminal app" (Settings > Layout) opens console programs in Zinc instead of a console
    window. It needs Windows Terminal installed, which provides the other half for now.

REMOVING ZINC
  Settings > Apps > Zinc > Uninstall. It also stops Zinc starting with Windows and gives the
  default terminal back. To remove your settings and logs too, delete %APPDATA%\Zinc afterwards.
