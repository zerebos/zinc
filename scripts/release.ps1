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

.EXAMPLE
  pwsh scripts/release.ps1 -UpdateUrl https://example.com/zinc -From https://example.com/zinc   # into dist\release
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
    [string]$Shortcuts = 'StartMenuRoot'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $repo 'dist\release' }
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

# ---- the previous release, for deltas ----
if ($From) {
    Write-Host "Fetching the previous release from $From ..."
    $kind = if ($From -match '^https?://') { 'http', '--url' } else { 'local', '--path' }
    dotnet vpk download $kind[0] $kind[1] $From -o $Out --allowEmptyChannel
    if ($LASTEXITCODE -ne 0) { throw "Couldn't fetch the previous release from $From." }
}

# ---- publish ----
$publish = Join-Path $repo "dist\publish-$PackId"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
Write-Host "Publishing $Version ..."
# One publish at a time with install.ps1 (they share obj\), and a full rebuild: an
# incremental publish after a framework-dependent one reused a zinc.deps.json naming no runtime.
$lock = [Threading.Mutex]::new($false, 'Local\ZincPublish')
try { $null = $lock.WaitOne() } catch [Threading.AbandonedMutexException] { }
try {
    $state = Join-Path $repo 'src\Zinc\obj\x64\Release'
    if (Test-Path $state) { Remove-Item $state -Recurse -Force }
    $log = dotnet publish (Join-Path $repo 'src\Zinc\Zinc.csproj') -c Release -r win-x64 --self-contained true -o $publish -nologo "-p:ZincUpdateUrl=$UpdateUrl" 2>&1
    $failed = $LASTEXITCODE -ne 0
} finally {
    $lock.ReleaseMutex()
}

if ($failed) {
    $log | Write-Host
    throw 'dotnet publish failed.'
}

foreach ($file in 'zinc.exe', 'coreclr.dll', 'hostfxr.dll', 'ghostty-vt.dll', 'zinc-sprite.dll', 'zinc.pri', 'Microsoft.Graphics.Canvas.dll', 'Velopack.dll') {
    if (-not (Test-Path (Join-Path $publish $file))) { throw "$file is missing from the build: not releasing it." }
}

$deps = Get-Content (Join-Path $publish 'zinc.deps.json') -Raw
if ($deps -notmatch 'runtimepack\.Microsoft\.NETCore\.App\.Runtime' -or $deps -notmatch '"coreclr\.dll"') {
    throw 'zinc.deps.json lists no .NET runtime, so zinc.exe would not start: not releasing it.'
}

# The tester notes, for the portable zip (and beside the installed files).
(Get-Content (Join-Path $PSScriptRoot 'package-readme.txt') -Raw).Replace('{version}', $Version) |
    Set-Content -Encoding utf8 (Join-Path $publish 'README.txt')

# ---- pack ----
Write-Host "Packing $PackId $Version ..."
# The title is what Settings > Apps shows: a test package says so, never a second "Zinc".
$title = if ($PackId -eq 'Zinc') { 'Zinc' } else { $PackId }
dotnet vpk pack --packId $PackId --packVersion $Version --packTitle $title --packAuthors Zinc `
    --packDir $publish --mainExe zinc.exe --icon (Join-Path $repo 'src\Zinc\Assets\Zinc.ico') `
    --shortcuts $Shortcuts --outputDir $Out
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }
Remove-Item $publish -Recurse -Force

Get-ChildItem $Out -File | Sort-Object LastWriteTime | ForEach-Object { "{0,-60} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
