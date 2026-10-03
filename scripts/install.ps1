<#
.SYNOPSIS
  Installs Zinc for the current user from this checkout.

.DESCRIPTION
  Publishes a Release build into a new versioned folder under
  %LOCALAPPDATA%\Programs\Zinc\versions\ and points the Start Menu shortcut
  (what Raycast and Windows Search index) at it. Open windows keep running from
  their own folder, so installing never conflicts with them; old versions are
  pruned once nothing is using them.

  The shortcut targets the versioned folder rather than a "current" symlink on
  purpose: a running Zinc loads files lazily (OpenConsole.exe starts per
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
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Zinc'
$versions = Join-Path $installRoot 'versions'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Zinc.lnk'
$hook = Join-Path $repo '.git\hooks\post-commit'
$hookMarker = '# zinc-install-hook'

function Say($msg) { if (-not $Quiet) { Write-Host $msg } }

# Background (hook) installs have nobody watching the log: surface failures as a
# Windows notification. Toasts need WinRT projections, which Windows PowerShell 5.1
# has and pwsh doesn't, hence powershell.exe.
function Notify($title, $text) {
    if (-not $Quiet) { return }
    $toast = Join-Path ([IO.Path]::GetTempPath()) 'zinc-install-toast.ps1'
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
    if (Get-Process zinc -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installRoot*" }) {
        throw 'Close all installed Zinc windows first.'
    }
    if (Test-Path $shortcut) { Remove-Item $shortcut }
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Zinc -ErrorAction SilentlyContinue
    # Zinc as the default terminal: give the terminal half back to what it replaced.
    $clsid = '{8E3F6C4B-2D71-4A59-9B0E-5C7A1D2F3B64}'
    $startup = 'HKCU:\Console\%%Startup'
    if ((Get-ItemProperty $startup -ErrorAction SilentlyContinue).DelegationTerminal -eq $clsid) {
        $previous = (Get-ItemProperty 'HKCU:\Software\Zinc' -ErrorAction SilentlyContinue).PreviousDelegationTerminal
        if ($previous) { Set-ItemProperty $startup -Name DelegationTerminal -Value $previous }
        else {
            Set-ItemProperty $startup -Name DelegationConsole -Value '{00000000-0000-0000-0000-000000000000}'
            Set-ItemProperty $startup -Name DelegationTerminal -Value '{00000000-0000-0000-0000-000000000000}'
        }
    }
    if (Test-Path "HKCU:\Software\Classes\CLSID\$clsid") { Remove-Item "HKCU:\Software\Classes\CLSID\$clsid" -Recurse -Force }
    if (Test-Path $installRoot) { Remove-Item $installRoot -Recurse -Force }
    Say 'Zinc uninstalled (config in %APPDATA%\Zinc is kept).'
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
# Reinstall Zinc in the background after each commit (scripts/install.ps1 -RemoveGitHook to undo).
log="`$(git rev-parse --git-dir)/zinc-install.log"
nohup pwsh -NoProfile -File scripts/install.ps1 -Quiet > "`$log" 2>&1 &
"@ -replace "`r", '' | Set-Content -NoNewline -Encoding utf8NoBOM $hook
    Say "Installed post-commit hook (log: .git/zinc-install.log)."
}

# ---- build ----
$rev = (git -C $repo rev-parse --short=7 HEAD).Trim()
if (git -C $repo status --porcelain --untracked-files=no) { $rev += '-dirty' }
# The version as Directory.Build.props computes it: the tagged release, else a dev build numbered by
# commit count. The folder name starts with a timestamp, which sorts (versions as text don't).
$base = [regex]::Match((Get-Content (Join-Path $repo 'Directory.Build.props') -Raw), '<ZincVersion>(.+?)</ZincVersion>').Groups[1].Value
$commits = (git -C $repo rev-list --count HEAD).Trim()
git -C $repo describe --exact-match --tags --match "v$base" HEAD 2>$null | Out-Null
$version = if ($LASTEXITCODE -eq 0) { "$base+$rev" } else { "$base-dev.$commits+$rev" }
$name = "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$version"
$target = Join-Path $versions $name

if (-not (Test-Path (Join-Path $repo 'native\x64\ghostty-vt.dll'))) {
    throw 'native\x64\ghostty-vt.dll missing: run scripts/build-libghostty.sh x64 first.'
}

Say "Publishing $name ..."
# The same build as scripts/release.ps1's (the .NET runtime inside), one publish at a time: the
# two share obj\, and a publish of the other kind there once left a file list naming no runtime.
$lock = [Threading.Mutex]::new($false, 'Local\ZincPublish')
try { $null = $lock.WaitOne() } catch [Threading.AbandonedMutexException] { }
try {
    $publishLog = dotnet publish (Join-Path $repo 'src\Zinc\Zinc.csproj') -c Release -r win-x64 --self-contained true -o $target -nologo 2>&1
    $publishFailed = $LASTEXITCODE -ne 0
} finally {
    $lock.ReleaseMutex()
}
if ($publishFailed) {
    $publishLog | Write-Host
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    $firstError = ($publishLog | Select-String ' error ' | Select-Object -First 1) -replace '^.*?error ', ''
    Notify "Zinc install failed ($rev)" "The build failed, so the previous version is still installed. $firstError"
    throw 'dotnet publish failed.'
}

# ---- shortcut ----
New-Item -ItemType Directory -Force (Split-Path $shortcut) | Out-Null
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = Join-Path $target 'zinc.exe'
$lnk.WorkingDirectory = $env:USERPROFILE
$lnk.Description = "Zinc terminal ($name)"
$lnk.Save()
Say "Start Menu shortcut -> $target"

# ---- sign-in entry (launch-at-login): follows the new version, if there is one ----
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty $runKey -ErrorAction SilentlyContinue).Zinc) {
    Set-ItemProperty $runKey -Name Zinc -Value "`"$(Join-Path $target 'zinc.exe')`" --at-login"
    Say "Sign-in entry -> $target"
}

# ---- default terminal (Settings > Layout): Windows starts this exe to hand it a console ----
$defterm = 'HKCU:\Software\Classes\CLSID\{8E3F6C4B-2D71-4A59-9B0E-5C7A1D2F3B64}\LocalServer32'
if (Test-Path $defterm) {
    Set-ItemProperty $defterm -Name '(default)' -Value "`"$(Join-Path $target 'zinc.exe')`" -Embedding"
    Say "Default terminal -> $target"
}

# ---- prune old versions (skip any still running) ----
$running = @(Get-Process zinc -ErrorAction SilentlyContinue | ForEach-Object { try { Split-Path $_.Path } catch { } })
Get-ChildItem $versions -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep | ForEach-Object {
    if ($running -contains $_.FullName) { return }
    try { Remove-Item $_.FullName -Recurse -Force; Say "Pruned $($_.Name)" }
    catch { Say "Kept $($_.Name) (in use)" }
}

Say 'Done. Launch "Zinc" from the Start Menu or Raycast.'
