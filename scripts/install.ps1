<#
.SYNOPSIS
  Installs Phosphor for the current user from this checkout.

.DESCRIPTION
  Publishes a Release build into a new versioned folder under
  %LOCALAPPDATA%\Programs\Phosphor\versions\ and points the Start Menu shortcut
  (what Raycast and Windows Search index) at it. Open windows keep running from
  their own folder, so installing never conflicts with them; old versions are
  pruned once nothing is using them.

  The shortcut targets the versioned folder rather than a "current" symlink on
  purpose: a running Phosphor loads files lazily (OpenConsole.exe starts per
  session), and repointing a link under it would mix versions.

.EXAMPLE
  pwsh scripts/install.ps1                  # build + install
  pwsh scripts/install.ps1 -InstallGitHook  # also reinstall after every commit
  pwsh scripts/install.ps1 -Uninstall
#>
param(
    [switch]$Uninstall,
    [switch]$InstallGitHook,
    [switch]$RemoveGitHook,
    [int]$Keep = 3,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Phosphor'
$versions = Join-Path $installRoot 'versions'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Phosphor.lnk'
$hook = Join-Path $repo '.git\hooks\post-commit'
$hookMarker = '# phosphor-install-hook'

function Say($msg) { if (-not $Quiet) { Write-Host $msg } }

# Background (hook) installs have nobody watching the log: surface failures as a
# Windows notification. Toasts need WinRT projections, which Windows PowerShell 5.1
# has and pwsh doesn't, hence powershell.exe.
function Notify($title, $text) {
    if (-not $Quiet) { return }
    $toast = Join-Path ([IO.Path]::GetTempPath()) 'phosphor-install-toast.ps1'
    @'
param($Title, $Text)
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$lines = $xml.GetElementsByTagName('text')
$lines.Item(0).InnerText = $Title
$lines.Item(1).InnerText = $Text
$appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show([Windows.UI.Notifications.ToastNotification]::new($xml))
'@ | Set-Content -Encoding utf8 $toast
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $toast -Title $title -Text $text | Out-Null
}

if ($RemoveGitHook -or $Uninstall) {
    if ((Test-Path $hook) -and (Get-Content $hook -Raw) -match $hookMarker) {
        Remove-Item $hook
        Say 'Removed post-commit hook.'
    }
}

if ($Uninstall) {
    if (Get-Process Phosphor -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installRoot*" }) {
        throw 'Close all installed Phosphor windows first.'
    }
    if (Test-Path $shortcut) { Remove-Item $shortcut }
    if (Test-Path $installRoot) { Remove-Item $installRoot -Recurse -Force }
    Say 'Phosphor uninstalled (config in %APPDATA%\Phosphor is kept).'
    return
}

if ($RemoveGitHook) { return }

if ($InstallGitHook) {
    if ((Test-Path $hook) -and -not ((Get-Content $hook -Raw) -match $hookMarker)) {
        throw "$hook already exists and isn't ours; not overwriting it."
    }
    # Runs detached so `git commit` returns immediately; output goes to a log.
    @"
#!/bin/sh
$hookMarker
# Reinstall Phosphor in the background after each commit (scripts/install.ps1 -RemoveGitHook to undo).
log="`$(git rev-parse --git-dir)/phosphor-install.log"
nohup pwsh -NoProfile -File scripts/install.ps1 -Quiet > "`$log" 2>&1 &
"@ -replace "`r", '' | Set-Content -NoNewline -Encoding utf8NoBOM $hook
    Say "Installed post-commit hook (log: .git/phosphor-install.log)."
}

# ---- build ----
$rev = (git -C $repo rev-parse --short HEAD).Trim()
if (git -C $repo status --porcelain --untracked-files=no) { $rev += '-dirty' }
$name = "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$rev"
$target = Join-Path $versions $name

if (-not (Test-Path (Join-Path $repo 'native\x64\ghostty-vt.dll'))) {
    throw 'native\x64\ghostty-vt.dll missing: run scripts/build-libghostty.sh x64 first.'
}

Say "Publishing $name ..."
$publishLog = dotnet publish (Join-Path $repo 'src\Phosphor\Phosphor.csproj') -c Release -r win-x64 -o $target -nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    $publishLog | Write-Host
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    $firstError = ($publishLog | Select-String ' error ' | Select-Object -First 1) -replace '^.*?error ', ''
    Notify "Phosphor install failed ($rev)" "The build failed, so the previous version is still installed. $firstError"
    throw 'dotnet publish failed.'
}

# ---- shortcut ----
New-Item -ItemType Directory -Force (Split-Path $shortcut) | Out-Null
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = Join-Path $target 'Phosphor.exe'
$lnk.WorkingDirectory = $env:USERPROFILE
$lnk.Description = "Phosphor terminal ($name)"
$lnk.Save()
Say "Start Menu shortcut -> $target"

# ---- prune old versions (skip any still running) ----
$running = @(Get-Process Phosphor -ErrorAction SilentlyContinue | ForEach-Object { try { Split-Path $_.Path } catch { } })
Get-ChildItem $versions -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep | ForEach-Object {
    if ($running -contains $_.FullName) { return }
    try { Remove-Item $_.FullName -Recurse -Force; Say "Pruned $($_.Name)" }
    catch { Say "Kept $($_.Name) (in use)" }
}

Say 'Done. Launch "Phosphor" from the Start Menu or Raycast.'
