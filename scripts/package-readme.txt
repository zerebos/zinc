Zinc {version}
A precise, native terminal for Windows, built on Ghostty's core. Private alpha: thanks for trying it.

START
  Unzip this folder anywhere you like (Documents, or %LOCALAPPDATA%\Programs) and open zinc.exe.
  There is nothing to install: everything Zinc needs is in the folder, about 390 files and folders,
  so unzip all of it (right-click the zip, Extract All) rather than opening zinc.exe from inside the
  zip.

  If nothing happens when you open zinc.exe, a file didn't make it out of the zip: Windows' Event
  Viewer (Windows Logs > Application) then shows ".NET Runtime" with "Could not resolve CoreCLR path".
  Check that coreclr.dll is next to zinc.exe; if it isn't, extract the zip again with Extract All,
  and look in Windows Security > Virus & threat protection > Protection history in case something
  removed it.
  Needs Windows 10 version 2004 or later, or Windows 11, on an x64 PC.

  Zinc isn't signed yet, so Windows may say "Windows protected your PC". Select More info, then
  Run anyway. To skip that: before unzipping, right-click the zip, choose Properties, check
  Unblock, then OK.

  The first start asks a few questions (how you like to work, colors, your default shell).
  Settings: Ctrl+, or the command palette (Ctrl+Shift+P).

WHERE ZINC KEEPS THINGS
  Settings          %APPDATA%\Zinc\config.toml (Settings > About > Config file opens it)
  Your themes       %APPDATA%\Zinc\themes\
  Logs, crash log   %LOCALAPPDATA%\Zinc\

SENDING FEEDBACK
  Something wrong: Settings > About > Debug log, turn it on, make the problem happen, then Copy,
  and paste it with your note. If Zinc crashed, crash.log is in %LOCALAPPDATA%\Zinc.
  Slow drawing: Settings > About > Drawing benchmark, Run (check "Also with software rendering"
  too if you have a minute), then Copy the results.
  Screenshots help, and so does saying which shell you use (PowerShell, WSL, Git Bash, ...).

GOOD TO KNOW
  - "Default terminal app" (Settings > Layout) opens console programs in Zinc instead of a console
    window. It needs Windows Terminal installed, which provides the other half for now.
  - If you move the folder, start zinc.exe from its new place once: starting with Windows and the
    default terminal follow it.
  - There are no automatic updates yet: a new version is a new zip. Unzip it next to the old one
    (your settings carry over) and delete the old folder.

REMOVING ZINC
  1. In Settings > Layout, turn off "Default terminal app", and set "When you sign in to Windows"
     to "Don't start Zinc".
  2. Quit Zinc: right-click its tray icon, Quit Zinc.
  3. Delete the folder. To remove your settings and logs too, delete %APPDATA%\Zinc and
     %LOCALAPPDATA%\Zinc.
