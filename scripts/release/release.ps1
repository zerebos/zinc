<#
.SYNOPSIS
  Builds a Zinc release: the installer, its update packages and a portable zip.

.DESCRIPTION
  Publishes a self-contained Release build (the .NET runtime inside), then packs it with Velopack (vpk, pinned
  in dotnet-tools.json) into -Out: Zinc-win-Setup.exe (what testers run once), the full package and, when
  the previous release is there, a delta package with only what changed, releases.win.json (the list a
  running Zinc checks for updates) and Zinc-win-Portable.zip.

  -UpdateSource is built into the release as the address it checks for updates. For a Zinc release it defaults to
  the GitHub repository in Directory.Build.props (ZincRepositoryUrl), whose releases hold the updates. Test packages
  (-PackId Zinc.Test) get none and find theirs through ZINC_UPDATE_URL.

  -UpdateUrl is the folder this release is uploaded to and that testers' installed copies still check (the feed
  from before GitHub Releases). -Publish needs it, to refuse a version that isn't above the one published there.

  Deltas need the previous release in -Out. -From fetches it first, from a GitHub repository's releases (the
  repository's address, with a token in GH_TOKEN when it's private), from the address testers update from (any other
  https URL) or from a local folder. A repository with no release yet gives none, and the release has no delta.

  A release is a commit, so a working tree with uncommitted changes is refused (-AllowDirty is for tests). Its
  version is the one the build works out in Directory.Build.props. A commit tagged v<ZincVersion> is a release, one
  tagged v<ZincVersion>-<something> is a pre-release, and any other commit is a dev build, which -Publish refuses.

  Its notes are CHANGELOG.md's entry for its version ("## 0.1.0 (date)"), handed to the installer (a downloaded
  update shows them under "What's new" in About). -Publish refuses without one, or with anything left under
  "## Unreleased". Before publishing, rename "## Unreleased" to the version, start a new empty "## Unreleased"
  above it, commit that, and tag the commit.

  -Publish does the whole thing from release.local.json, in the repo's root and not in git:
    { "updateUrl": "https://example.com/zinc/",
      "ftp": "ftp://host/path/to/zinc/",
      "ftpPin": "sha256//<base64 of the server's public key>" }   (optional, see below)
  It builds into a fresh dist\release with -UpdateUrl and -From set to updateUrl, then uploads over FTPS with curl,
  using your login from %USERPROFILE%\.netrc. The server's certificate is checked as usual. With ftpPin, the
  server's key is checked against the pin instead, so an expired or self-signed certificate still gives an encrypted
  connection to that server only. A pin breaks at every renewal, so use it only while a host's certificate is broken.

  The upload sends .htaccess (release-htaccess, next to this script), then the new files, then releases.win.json
  last, under another name and then renamed, so a copy checking mid-upload never sees a release whose files aren't
  there yet. Packages the new list doesn't mention are deleted from the server.

.EXAMPLE
  pwsh scripts/release/release.ps1 -Publish                                                             # the real thing
  pwsh scripts/release/release.ps1 -From https://example.com/zinc/                                      # build only
  pwsh scripts/release/release.ps1 -PackId Zinc.Test -Version 0.1.0-test.1 -AllowDirty -Out C:\Temp\feed  # tests
#>
param(
    [string]$Out,
    [string]$From,
    [string]$PackId = 'Zinc',
    [string]$UpdateUrl,
    [string]$UpdateSource,
    [string]$Version,
    [switch]$AllowDirty,
    # Where shortcuts go at install ('' for none, because test packages must not touch the Start Menu).
    [string]$Shortcuts = 'StartMenuRoot',
    [switch]$Publish,
    # The changelog the release notes come from (tests give their own).
    [string]$Changelog
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
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

if ($PackId -eq 'Zinc' -and -not $UpdateSource) {
    $UpdateSource = [regex]::Match((Get-Content (Join-Path $repo 'Directory.Build.props') -Raw), '<ZincRepositoryUrl>(.+?)</ZincRepositoryUrl>').Groups[1].Value
    if (-not $UpdateSource) { throw 'A release needs -UpdateSource, or ZincRepositoryUrl in Directory.Build.props, to know where to look for updates.' }
}

if (-not (Test-Path (Join-Path $repo 'native\x64\ghostty-vt.dll'))) {
    throw 'native\x64\ghostty-vt.dll missing: run scripts/build/libghostty.ps1 first.'
}

# ---- version (the one Directory.Build.props works out) ----
$dirty = [bool](git -C $repo status --porcelain --untracked-files=no)
if ($dirty -and -not $AllowDirty) { throw 'Uncommitted changes: a release is a commit. Commit first (or -AllowDirty for a test).' }
if (-not $Version) {
    $Version = (dotnet msbuild (Join-Path $repo 'src\Zinc.Core\Zinc.Core.csproj') -nologo -t:ZincGitVersion -getProperty:ZincInformationalVersion).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $Version) { throw "Couldn't ask the build for its version." }
    if ($dirty) { $Version += '-dirty' }
}
if ($Publish -and $Version -match '-dev\+') {
    throw "This commit has no version tag, so it would be $Version. Tag it (v<ZincVersion>, or v<ZincVersion>-rc.N for a pre-release) and run this again."
}

# ---- a release testers would never get, because it isn't above the one already published ----
# Velopack only updates to a higher version. Versions compare as SemVer orders them, so 0.1.0-rc.1 is above every
# 0.1.0-dev.N, and 0.1.0 is above every pre-release of it.
if ($Publish) {
    $mine = [semver]$Version.Split('+')[0]
    $highest = (Invoke-RestMethod "$($UpdateUrl.TrimEnd('/'))/releases.win.json").Assets |
        ForEach-Object { [semver]$_.Version.Split('+')[0] } | Sort-Object | Select-Object -Last 1
    if ($highest -and $mine -le $highest) {
        throw "This would be $mine, but $highest is already published, and testers only update to a higher version. Tag it with a version above $highest."
    }
}

# ---- release notes, from the changelog's entry for this version ----
$notesArgs = @()
if (-not $Changelog) { $Changelog = Join-Path $repo 'CHANGELOG.md' }
if ($PackId -eq 'Zinc' -or $PSBoundParameters.ContainsKey('Changelog')) {
    # release-notes.cs reads the changelog as the app does. With --check it refuses a version that has no entry, or a
    # changelog with notes left under "## Unreleased". Without --check, a build with no entry has no notes.
    $plain = $Version.Split('+')[0]
    $notesFile = Join-Path ([IO.Path]::GetTempPath()) "zinc-release-notes-$plain.md" # not in -Out, because everything there is uploaded
    if (Test-Path $notesFile) { Remove-Item $notesFile }
    $check = if ($Publish) { @('--check') } else { @() }
    dotnet run (Join-Path $PSScriptRoot 'release-notes.cs') -- $plain --changelog $Changelog --out $notesFile @check
    if ($LASTEXITCODE -ne 0) { throw 'The changelog is not ready for this release (see above, and this script''s help).' }
    if (Test-Path $notesFile) { $notesArgs = '--releaseNotes', $notesFile }
}

# ---- the previous release, for deltas ----
if ($From) {
    Write-Host "Fetching the previous release from $From ..."
    if ($From -match '^https://github\.com/[^/]+/[^/]+/?$') {
        # A pre-release's delta is made from the newest release or pre-release, and a release's from the newest release.
        $pre = if ($Version.Split('+')[0].Contains('-')) { @('--pre') } else { @() }
        $token = if ($env:GH_TOKEN) { @('--token', $env:GH_TOKEN) } else { @() }
        dotnet vpk download github --repoUrl $From -o $Out @pre @token
        if ($LASTEXITCODE -ne 0) { Write-Warning "No previous release came from $From, so this release has no delta package." }
    } else {
        $kind = if ($From -match '^https?://') { 'http', '--url' } else { 'local', '--path' }
        dotnet vpk download $kind[0] $kind[1] $From -o $Out --allowEmptyChannel
        if ($LASTEXITCODE -ne 0) { throw "Couldn't fetch the previous release from $From." }
    }
}

# ---- publish ----
$publishDir = Join-Path $repo "dist\publish-$PackId"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Write-Host "Publishing $Version ..."
# One publish at a time (any script that publishes into obj\ takes the same mutex), and a full rebuild, because an
# incremental publish after a framework-dependent one can reuse a zinc.deps.json that names no runtime.
$inheritedPath = $env:PATH
$lock = [Threading.Mutex]::new($false, 'Local\ZincPublish')
try { $null = $lock.WaitOne() } catch [Threading.AbandonedMutexException] { }
try {
    $state = Join-Path $repo 'src\Zinc\obj\x64\Release'
    if (Test-Path $state) { Remove-Item $state -Recurse -Force }
    # The publish gets PATH as a fresh terminal has it (from the registry), because an inherited PATH can break VS's
    # vcvarsall.bat or put another `link` ahead of MSVC's, and zn's Native AOT link then fails. The rest of the script
    # keeps the inherited PATH (curl).
    $env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')
    $log = dotnet publish (Join-Path $repo 'src\Zinc\Zinc.csproj') -c Release -r win-x64 --self-contained true -o $publishDir -nologo "-p:ZincUpdateUrl=$UpdateSource" 2>&1
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
dotnet vpk pack --packId $PackId --packVersion ($Version.Split('+')[0]) --packTitle $title --packAuthors Zinc --runtime win-x64 `
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
# Every time, because its rules change now and then.
Ftp -T (Join-Path $PSScriptRoot 'release-htaccess') "$($ftp).htaccess" | Out-Null
Write-Host '  .htaccess'

# Files with the same name in every release are always replaced, and packages are uploaded only when they're new.
$replace = "$PackId-win-Setup.exe", "$PackId-win-Portable.zip", 'assets.win.json', 'RELEASES'
foreach ($file in Get-ChildItem $Out -File | Where-Object Name -ne 'releases.win.json') {
    if ($there -contains $file.Name -and $replace -notcontains $file.Name) { continue }
    Ftp -T $file.FullName $ftp | Out-Null
    Write-Host ("  {0} ({1:N1} MB)" -f $file.Name, ($file.Length / 1MB))
}

# The list goes last, and the rename makes it appear in one step.
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
