<#
.SYNOPSIS
  Builds a Zinc release: the installer, its update packages and a portable zip.

.DESCRIPTION
  Publishes a self-contained Release build (the .NET runtime inside), then packs it with Velopack (vpk, pinned
  in dotnet-tools.json) into -Out: Zinc-win-Setup.exe (what testers run once), the full package and, when
  the previous release is there, a delta package with only what changed, releases.win.json (the list a
  running Zinc checks for updates) and Zinc-win-Portable.zip.

  -Arch arm64 builds for ARM64 instead, as its own Velopack channel (win-arm64), so its files have their own names
  (Zinc-win-arm64-Setup.exe, releases.win-arm64.json) and both architectures fit in one folder and one release. It
  needs the ARM64 native DLLs (libghostty.ps1 -Arch arm64), and zn's Native AOT link needs Build Tools' ARM64
  component.

  -UpdateSource is built into the release as the address it checks for updates. For a Zinc release it defaults to
  the GitHub repository in Directory.Build.props (ZincRepositoryUrl), whose releases hold the updates. Test packages
  (-PackId Zinc.Test) get none and find theirs through ZINC_UPDATE_URL.

  Deltas need the previous release in -Out. -From fetches it first, from a GitHub repository's releases (the
  repository's address, with a token in GH_TOKEN when it's private), from a web address that serves release files,
  or from a local folder. A repository with no release yet gives none, and the release has no delta.

  A release is a commit, so a working tree with uncommitted changes is refused (-AllowDirty is for tests). Its
  version is the one the build works out in Directory.Build.props. A commit tagged v<ZincVersion> is a release, one
  tagged v<ZincVersion>-<label>.N is a pre-release, and any other commit is a dev build.

  Its notes are CHANGELOG.md's entry for its version ("## 0.1.0 (date)"), handed to the installer (a downloaded
  update shows them under "What's new" in About). A build with no entry has no notes. The release workflow checks
  the changelog itself, and uploads what this script leaves in -Out to the repository's GitHub Releases.

.EXAMPLE
  pwsh scripts/release/release.ps1 -Out dist/release -From https://github.com/zerebos/zinc               # what the release workflow runs
  pwsh scripts/release/release.ps1 -PackId Zinc.Test -Version 0.1.0-test.1 -AllowDirty -Out C:\Temp\feed  # tests
#>
param(
    [string]$Out,
    # The architecture to build. Each one is its own Velopack channel, so an installed copy only updates to packages
    # of its own architecture. x64 keeps `win`, the channel every release before ARM64 used, and ARM64 uses `win-arm64`.
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',
    [string]$From,
    [string]$PackId = 'Zinc',
    [string]$UpdateSource,
    [string]$Version,
    [switch]$AllowDirty,
    # Where shortcuts go at install ('' for none, because test packages must not touch the Start Menu).
    [string]$Shortcuts = 'StartMenuRoot',
    # The changelog the release notes come from (tests give their own).
    [string]$Changelog
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Out) { $Out = Join-Path $repo 'dist\release' }

New-Item -ItemType Directory -Force $Out | Out-Null

if ($PackId -eq 'Zinc' -and -not $UpdateSource) {
    $UpdateSource = [regex]::Match((Get-Content (Join-Path $repo 'Directory.Build.props') -Raw), '<ZincRepositoryUrl>(.+?)</ZincRepositoryUrl>').Groups[1].Value
    if (-not $UpdateSource) { throw 'A release needs -UpdateSource, or ZincRepositoryUrl in Directory.Build.props, to know where to look for updates.' }
}

$platform = @{ x64 = 'x64'; arm64 = 'ARM64' }[$Arch]
$runtime = "win-$Arch"
$channel = @{ x64 = 'win'; arm64 = 'win-arm64' }[$Arch]

if (-not (Test-Path (Join-Path $repo "native\$Arch\ghostty-vt.dll"))) {
    throw "native\$Arch\ghostty-vt.dll missing: run scripts/build/libghostty.ps1 -Arch $Arch first."
}

# ---- version (the one Directory.Build.props works out) ----
$dirty = [bool](git -C $repo status --porcelain --untracked-files=no)
if ($dirty -and -not $AllowDirty) { throw 'Uncommitted changes: a release is a commit. Commit first (or -AllowDirty for a test).' }
if (-not $Version) {
    $Version = (dotnet msbuild (Join-Path $repo 'src\Zinc.Core\Zinc.Core.csproj') -nologo -t:ZincGitVersion -getProperty:ZincInformationalVersion).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $Version) { throw "Couldn't ask the build for its version." }
    if ($dirty) { $Version += '-dirty' }
}

# ---- release notes, from the changelog's entry for this version ----
$notesArgs = @()
if (-not $Changelog) { $Changelog = Join-Path $repo 'CHANGELOG.md' }
if ($PackId -eq 'Zinc' -or $PSBoundParameters.ContainsKey('Changelog')) {
    # release-notes.cs reads the changelog as the app does. A build with no entry gets no notes file.
    $plain = $Version.Split('+')[0]
    $notesFile = Join-Path ([IO.Path]::GetTempPath()) "zinc-release-notes-$plain.md" # not in -Out, because everything there is uploaded
    if (Test-Path $notesFile) { Remove-Item $notesFile }
    dotnet run (Join-Path $PSScriptRoot 'release-notes.cs') -- $plain --changelog $Changelog --out $notesFile
    if ($LASTEXITCODE -ne 0) { throw 'Reading the release notes from the changelog failed (see above).' }
    if (Test-Path $notesFile) { $notesArgs = '--releaseNotes', $notesFile }
}

# ---- the previous release, for deltas ----
if ($From) {
    Write-Host "Fetching the previous release from $From ..."
    if ($From -match '^https://github\.com/[^/]+/[^/]+/?$') {
        # A pre-release's delta is made from the newest release or pre-release, and a release's from the newest release.
        $pre = @(if ($Version.Split('+')[0].Contains('-')) { '--pre' })
        $token = if ($env:GH_TOKEN) { @('--token', $env:GH_TOKEN) } else { @() }
        dotnet vpk download github --repoUrl $From -o $Out --channel $channel @pre @token
        if ($LASTEXITCODE -ne 0) { Write-Warning "No previous release came from $From, so this release has no delta package." }
    } else {
        $kind = if ($From -match '^https?://') { 'http', '--url' } else { 'local', '--path' }
        dotnet vpk download $kind[0] $kind[1] $From -o $Out --channel $channel --allowEmptyChannel
        if ($LASTEXITCODE -ne 0) { throw "Couldn't fetch the previous release from $From." }
    }
}

# ---- publish ----
$publishDir = Join-Path $repo "dist\publish-$PackId-$Arch"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Write-Host "Publishing $Version for $Arch ..."
# One publish at a time (any script that publishes into obj\ takes the same mutex), and a full rebuild, because an
# incremental publish after a framework-dependent one can reuse a zinc.deps.json that names no runtime.
$inheritedPath = $env:PATH
$lock = [Threading.Mutex]::new($false, 'Local\ZincPublish')
try { $null = $lock.WaitOne() } catch [Threading.AbandonedMutexException] { }
try {
    $state = Join-Path $repo "src\Zinc\obj\$platform\Release"
    if (Test-Path $state) { Remove-Item $state -Recurse -Force }
    # The publish gets PATH as a fresh terminal has it (from the registry), because an inherited PATH can break VS's
    # vcvarsall.bat or put another `link` ahead of MSVC's, and zn's Native AOT link then fails. The rest of the script
    # keeps the inherited PATH.
    $env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')
    $log = dotnet publish (Join-Path $repo 'src\Zinc\Zinc.csproj') -c Release -r $runtime "-p:Platform=$platform" --self-contained true -o $publishDir -nologo "-p:ZincUpdateUrl=$UpdateSource" 2>&1
    $failed = $LASTEXITCODE -ne 0
} finally {
    $env:PATH = $inheritedPath
    $lock.ReleaseMutex()
}

if ($failed) {
    $log | Write-Host
    throw 'dotnet publish failed.'
}

foreach ($file in 'zinc.exe', 'coreclr.dll', 'hostfxr.dll', 'ghostty-vt.dll', 'zinc-sprite.dll', 'zinc-shader.dll', 'zinc.pri', 'Microsoft.Graphics.Canvas.dll', 'Velopack.dll', 'bin\zn.exe') {
    if (-not (Test-Path (Join-Path $publishDir $file))) { throw "$file is missing from the build: not releasing it." }
}

$deps = Get-Content (Join-Path $publishDir 'zinc.deps.json') -Raw
if ($deps -notmatch 'runtimepack\.Microsoft\.NETCore\.App\.Runtime' -or $deps -notmatch '"coreclr\.dll"') {
    throw 'zinc.deps.json lists no .NET runtime, so zinc.exe would not start: not releasing it.'
}

# Every assembly something here references must be in zinc.deps.json (trimming can leave one out, which check-publish.ps1 finds).
$missing = & (Join-Path $PSScriptRoot 'check-publish.ps1') $publishDir
if ($missing) {
    throw "zinc.deps.json doesn't list assemblies these need, so they'd fail to load when used: $($missing -join ', '). Not releasing it."
}

# The tester notes, for the portable zip (and beside the installed files).
(Get-Content (Join-Path $PSScriptRoot 'package-readme.txt') -Raw).Replace('{version}', $Version) |
    Set-Content -Encoding utf8 (Join-Path $publishDir 'README.txt')

# ---- pack ----
Write-Host "Packing $PackId $Version ..."
# The title is what Settings > Apps shows, so a test package shows its own id instead of a second "Zinc".
$title = if ($PackId -eq 'Zinc') { 'Zinc' } else { $PackId }
# Packed under the version without the commit's hash. The hash is in the app itself, where About shows it.
dotnet vpk pack --packId $PackId --packVersion ($Version.Split('+')[0]) --packTitle $title --packAuthors Zinc --runtime $runtime --channel $channel `
    --packDir $publishDir --mainExe zinc.exe --icon (Join-Path $repo 'src\Zinc\Assets\Zinc.ico') `
    --shortcuts $Shortcuts --outputDir $Out @notesArgs
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }
Remove-Item $publishDir -Recurse -Force

Get-ChildItem $Out -File | Sort-Object LastWriteTime | ForEach-Object { "{0,-60} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
