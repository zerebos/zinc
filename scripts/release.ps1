<#
.SYNOPSIS
  Builds a Zinc release: the installer, its update packages and a portable zip.

.DESCRIPTION
  Publishes a self-contained Release build (the .NET runtime inside), then packs it with Velopack (vpk, pinned
  in dotnet-tools.json) into -Out: Zinc-win-Setup.exe (what testers run once), the full package and, when
  the previous release is there, a delta package with only what changed, releases.win.json (the list a
  running Zinc checks for updates) and Zinc-win-Portable.zip.

  -UpdateUrl is built into the release: where it checks for updates (the folder testers download from).
  A Zinc release needs one; test packages (-PackId Zinc.Test) find theirs through ZINC_UPDATE_URL.

  Deltas need the previous release in -Out. -From fetches it first: the address testers update from (an
  https URL) or a local folder.

  A release is a commit: a working tree with uncommitted changes is refused (-AllowDirty is for tests),
  and its version (computed as Directory.Build.props does) only ever goes up.

  Its notes are CHANGELOG.md's entry for its version ("## 0.1.0-dev.N (date)"), handed to the installer (a downloaded
  update shows them under "What's new" in About). -Publish refuses without one, or with anything left under
  "## Unreleased". Before publishing: rename "## Unreleased" to the version the next commit makes (N = commits + 1:
  `git rev-list --count HEAD` plus one), start a new empty "## Unreleased" above it, and commit that.

  -Publish does the whole thing from release.local.json (next to this repo's root; not in git):
    { "updateUrl": "https://example.com/zinc/",
      "ftp": "ftp://host/path/to/zinc/",
      "ftpPin": "sha256//<base64 of the server's public key>" }   (optional, see below)
  It builds into a fresh dist\release with -UpdateUrl and -From set to updateUrl, then uploads over FTPS (curl,
  your login from %USERPROFILE%\.netrc, the server's certificate checked as usual; with ftpPin, that key instead,
  so an expired or self-signed certificate still gives an encrypted connection to that server only. A pin breaks
  at every renewal: use it only while a host's certificate is broken): .htaccess (scripts/release-htaccess), the new files,
  then releases.win.json last, under another name and renamed, so a copy checking mid-upload never sees a
  release whose files aren't there yet. Packages the new list doesn't mention are deleted from the server.

.EXAMPLE
  pwsh scripts/release.ps1 -Publish                                                            # the real thing
  pwsh scripts/release.ps1 -UpdateUrl https://example.com/zinc/ -From https://example.com/zinc/ # build only
  pwsh scripts/release.ps1 -PackId Zinc.Test -Version 0.1.0-test.1 -AllowDirty -Out C:\Temp\feed   # tests
#>
param(
    [string]$Out,
    [string]$From,
    [string]$PackId = 'Zinc',
    [string]$UpdateUrl,
    [string]$Version,
    [switch]$AllowDirty,
    # Where shortcuts go at install ('' = none: test packages must not touch the Start Menu).
    [string]$Shortcuts = 'StartMenuRoot',
    [switch]$Publish,
    # The changelog the release notes come from (tests give their own).
    [string]$Changelog
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $repo 'dist\release' }

if ($Publish) {
    $settingsFile = Join-Path $repo 'release.local.json'
    if (-not (Test-Path $settingsFile)) { throw "-Publish needs $settingsFile (see this script's help)." }
    $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json
    if ($PackId -ne 'Zinc' -or $AllowDirty -or $Version) { throw '-Publish is for real releases: no -PackId, -AllowDirty or -Version.' }
    $UpdateUrl = $settings.updateUrl
    $From = $settings.updateUrl
    if (Test-Path $Out) { Remove-Item $Out -Recurse -Force } # only this release and the one it updates from
}

New-Item -ItemType Directory -Force $Out | Out-Null

if ($PackId -eq 'Zinc' -and -not $UpdateUrl) { throw 'A release needs -UpdateUrl: where it will look for updates.' }

if (-not (Test-Path (Join-Path $repo 'native\x64\ghostty-vt.dll'))) {
    throw 'native\x64\ghostty-vt.dll missing: run scripts/build-libghostty.sh x64 first.'
}

# ---- version (as Directory.Build.props computes it) ----
$dirty = [bool](git -C $repo status --porcelain --untracked-files=no)
if ($dirty -and -not $AllowDirty) { throw 'Uncommitted changes: a release is a commit. Commit first (or -AllowDirty for a test).' }
if (-not $Version) {
    $rev = (git -C $repo rev-parse --short=7 HEAD).Trim()
    if ($dirty) { $rev += '-dirty' }
    $base = [regex]::Match((Get-Content (Join-Path $repo 'Directory.Build.props') -Raw), '<ZincVersion>(.+?)</ZincVersion>').Groups[1].Value
    $commits = (git -C $repo rev-list --count HEAD).Trim()
    git -C $repo describe --exact-match --tags --match "v$base" HEAD 2>$null | Out-Null
    $Version = if ($LASTEXITCODE -eq 0) { "$base+$rev" } else { "$base-dev.$commits+$rev" }
}

# ---- release notes: the changelog's entry for this version ----
$notesArgs = @()
if (-not $Changelog) { $Changelog = Join-Path $repo 'CHANGELOG.md' }
if ($PackId -eq 'Zinc' -or $PSBoundParameters.ContainsKey('Changelog')) {
    $plain = $Version.Split('+')[0]
    $lines = (Get-Content $Changelog -Raw) -replace "`r`n", "`n" -split "`n"
    function Entry([string]$heading) {
        $start = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l -eq "## $heading" -or $l.StartsWith("## $heading (") })
        if ($start -lt 0) { return $null }
        $end = [Array]::FindIndex($lines, $start + 1, [Predicate[string]] { param($l) $l.StartsWith('## ') })
        if ($end -lt 0) { $end = $lines.Length }
        ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
    }

    if ($Publish -and (Entry 'Unreleased')) {
        throw "CHANGELOG.md has notes under '## Unreleased': rename it to '## $plain ($(Get-Date -Format yyyy-MM-dd))', add a new empty '## Unreleased' above it, commit, then publish."
    }

    $notes = Entry $plain
    if (-not $notes) {
        if ($Publish) { throw "CHANGELOG.md has no entry for $plain (see this script's help)." }
        Write-Warning "CHANGELOG.md has no entry for ${plain}: this build has no release notes."
    } else {
        $notesFile = Join-Path ([IO.Path]::GetTempPath()) "zinc-release-notes-$plain.md" # not in -Out: everything there is uploaded
        [IO.File]::WriteAllText($notesFile, $notes + "`n", [Text.UTF8Encoding]::new($false))
        $notesArgs = '--releaseNotes', $notesFile
        Write-Host "Release notes: CHANGELOG.md's entry for $plain"
    }
}

# ---- the previous release, for deltas ----
if ($From) {
    Write-Host "Fetching the previous release from $From ..."
    $kind = if ($From -match '^https?://') { 'http', '--url' } else { 'local', '--path' }
    dotnet vpk download $kind[0] $kind[1] $From -o $Out --allowEmptyChannel
    if ($LASTEXITCODE -ne 0) { throw "Couldn't fetch the previous release from $From." }
}

# ---- publish ----
$publishDir = Join-Path $repo "dist\publish-$PackId"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Write-Host "Publishing $Version ..."
# One publish at a time with install.ps1 (they share obj\), and a full rebuild: an
# incremental publish after a framework-dependent one reused a zinc.deps.json naming no runtime.
$inheritedPath = $env:PATH
$lock = [Threading.Mutex]::new($false, 'Local\ZincPublish')
try { $null = $lock.WaitOne() } catch [Threading.AbandonedMutexException] { }
try {
    $state = Join-Path $repo 'src\Zinc\obj\x64\Release'
    if (Test-Path $state) { Remove-Item $state -Recurse -Force }
    # The publish gets PATH as a fresh terminal has it (from the registry): under Claude Code's Git Bash, VS's
    # vcvarsall.bat failed on the inherited PATH ("\NVIDIA was unexpected at this time"), so zn's Native AOT link
    # found no MSVC link.exe, or Git's coreutils `link` (2026-10-07). The rest keeps the inherited PATH (curl).
    $env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')
    $log = dotnet publish (Join-Path $repo 'src\Zinc\Zinc.csproj') -c Release -r win-x64 --self-contained true -o $publishDir -nologo "-p:ZincUpdateUrl=$UpdateUrl" 2>&1
    $failed = $LASTEXITCODE -ne 0
} finally {
    $env:PATH = $inheritedPath
    $lock.ReleaseMutex()
}

if ($failed) {
    $log | Write-Host
    throw 'dotnet publish failed.'
}

foreach ($file in 'zinc.exe', 'coreclr.dll', 'hostfxr.dll', 'ghostty-vt.dll', 'zinc-sprite.dll', 'zinc-shader.dll', 'zinc.pri', 'Microsoft.Graphics.Canvas.dll', 'Velopack.dll', 'vcruntime140.dll', 'bin\zn.exe') {
    if (-not (Test-Path (Join-Path $publishDir $file))) { throw "$file is missing from the build: not releasing it." }
}

$deps = Get-Content (Join-Path $publishDir 'zinc.deps.json') -Raw
if ($deps -notmatch 'runtimepack\.Microsoft\.NETCore\.App\.Runtime' -or $deps -notmatch '"coreclr\.dll"') {
    throw 'zinc.deps.json lists no .NET runtime, so zinc.exe would not start: not releasing it.'
}

# The tester notes, for the portable zip (and beside the installed files).
(Get-Content (Join-Path $PSScriptRoot 'package-readme.txt') -Raw).Replace('{version}', $Version) |
    Set-Content -Encoding utf8 (Join-Path $publishDir 'README.txt')

# ---- pack ----
Write-Host "Packing $PackId $Version ..."
# The title is what Settings > Apps shows: a test package says so, never a second "Zinc".
$title = if ($PackId -eq 'Zinc') { 'Zinc' } else { $PackId }
dotnet vpk pack --packId $PackId --packVersion $Version --packTitle $title --packAuthors Zinc --runtime win-x64 `
    --packDir $publishDir --mainExe zinc.exe --icon (Join-Path $repo 'src\Zinc\Assets\Zinc.ico') `
    --shortcuts $Shortcuts --outputDir $Out @notesArgs
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }
Remove-Item $publishDir -Recurse -Force

Get-ChildItem $Out -File | Sort-Object LastWriteTime | ForEach-Object { "{0,-60} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }

if (-not $Publish) { return }

# ---- upload ----
$ftp = $settings.ftp.TrimEnd('/') + '/'
$curl = Join-Path $env:SystemRoot 'System32\curl.exe' # Windows' own, whatever else is on PATH
function Ftp {
    $trust = if ($settings.ftpPin) { '--insecure', '--pinnedpubkey', $settings.ftpPin } else { @() }
    $output = & $curl -s -S -n --ssl-reqd @trust --ftp-create-dirs --max-time 900 @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "FTP failed ($LASTEXITCODE): $output" }
    $output
}

Write-Host "Uploading to $ftp ..."
$there = @(Ftp --list-only $ftp | ForEach-Object { "$_".Trim() } | Where-Object { $_ -and $_ -notin '.', '..' })
# Every time: its rules change now and then (2026-10-03: packages sent uncompressed, so downloads show progress).
Ftp -T (Join-Path $PSScriptRoot 'release-htaccess') "$($ftp).htaccess" | Out-Null
Write-Host '  .htaccess'

# Same names every release: always replaced. Packages: only the new ones.
$replace = "$PackId-win-Setup.exe", "$PackId-win-Portable.zip", 'assets.win.json', 'RELEASES'
foreach ($file in Get-ChildItem $Out -File | Where-Object Name -ne 'releases.win.json') {
    if ($there -contains $file.Name -and $replace -notcontains $file.Name) { continue }
    Ftp -T $file.FullName $ftp | Out-Null
    Write-Host ("  {0} ({1:N1} MB)" -f $file.Name, ($file.Length / 1MB))
}

# The list last, appearing in one step.
Ftp -T (Join-Path $Out 'releases.win.json') "$($ftp)releases.win.json.new" -Q '-RNFR releases.win.json.new' -Q '-RNTO releases.win.json' | Out-Null
Write-Host '  releases.win.json'

# Packages the new list no longer mentions.
$listed = (Get-Content (Join-Path $Out 'releases.win.json') -Raw | ConvertFrom-Json).Assets | ForEach-Object FileName
foreach ($old in $there | Where-Object { $_ -like '*.nupkg' -and $listed -notcontains $_ }) {
    Ftp --list-only $ftp -Q "-DELE $old" | Out-Null
    Write-Host "  removed $old"
}

Write-Host ''
Write-Host "Published $Version. Installer: $($UpdateUrl.TrimEnd('/'))/$PackId-win-Setup.exe"
