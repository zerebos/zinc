<#
.SYNOPSIS
  Sets up a release's GitHub page for people looking for a download: a download section above the notes, and a
  label on every file.

.DESCRIPTION
  The release workflow runs this on the draft, after every file is uploaded and before the draft is published.

  The notes become a download section, with links to the two installers and the two portable zips, followed by
  -Notes (the changelog's entry) under "What's new". Every file gets a label, which GitHub shows in place of its
  name, so the files for the updater, the website, and editors say what they're for. Download links and Velopack use
  the file names, which don't change.

  A file the download section links to that isn't on the release stops the script, so the workflow stops before it
  publishes.

.EXAMPLE
  pwsh scripts/release/release-page.ps1 -Repo zerebos/zinc -Tag v0.1.0 -Notes dist/notes.md
#>
param(
    # The repository as owner/name.
    [Parameter(Mandatory)] [string]$Repo,
    [Parameter(Mandatory)] [string]$Tag,
    # The changelog's entry for the release, as release-notes.cs writes it.
    [Parameter(Mandatory)] [string]$Notes,
    # The Velopack pack id, which starts every package's file name (tests use Zinc.Test).
    [string]$PackId = 'Zinc'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$assets = @((gh release view $Tag -R $Repo --json assets | ConvertFrom-Json).assets)
$names = @($assets.name)

# ---- the download section ----
$rows = @(
    @{ Machine = 'x64 PCs (most PCs)'; Setup = "$PackId-win-Setup.exe"; Portable = "$PackId-win-Portable.zip" }
    @{ Machine = 'ARM64 PCs (Windows on Arm)'; Setup = "$PackId-win-arm64-Setup.exe"; Portable = "$PackId-win-arm64-Portable.zip" }
)
$missing = $rows | ForEach-Object { $_.Setup, $_.Portable } | Where-Object { $names -notcontains $_ }
if ($missing) { throw "$Tag has no $($missing -join ' or '), which the download section links to." }

function Link([string]$Name) { "[$Name](https://github.com/$Repo/releases/download/$Tag/$Name)" }

$page = @(
    '## Download'
    ''
    '| | Installer | Portable |'
    '|:--|:--|:--|'
    $rows | ForEach-Object { "| $($_.Machine) | $(Link $_.Setup) | $(Link $_.Portable) |" }
    ''
    'The installer sets Zinc up for your user and keeps it updated. The portable zip runs from any folder, without updates. The other files are for Zinc''s updater, the website, and editors, so you don''t need them.'
    ''
    '## What''s new'
    ''
    (Get-Content $Notes -Raw).Trim()
) -join "`n"

$pageFile = Join-Path (Split-Path -Parent (Resolve-Path $Notes)) 'release-page.md'
Set-Content -Encoding utf8NoBOM $pageFile $page
gh release edit $Tag -R $Repo --notes-file $pageFile | Out-Null

# ---- the labels ----
function LabelFor([string]$Name) {
    $arch = if ($Name -like '*win-arm64*') { 'ARM64' } else { 'x64' }
    switch -Wildcard ($Name) {
        "$PackId-win*-Setup.exe" { return "Installer for $arch PCs" }
        "$PackId-win*-Portable.zip" { return "Portable zip for $arch PCs" }
        '*-full.nupkg' { return "For the updater ($arch full package)" }
        '*-delta.nupkg' { return "For the updater ($arch package of changes)" }
        'releases.*.json' { return "For the updater ($arch release list)" }
        'RELEASES' { return 'For older updaters (x64 release list)' }
        'zinc-reference.json' { return 'For the website (settings, actions, and keys)' }
        'config.schema.json' { return 'For editors (the config.toml schema)' }
    }
    $null
}

foreach ($asset in $assets) {
    $label = LabelFor $asset.name
    if (-not $label) { Write-Warning "$($asset.name) has no label, so the release page shows its file name."; continue }
    if ($asset.label -ne $label) { gh api -X PATCH $asset.apiUrl -f "label=$label" --silent }
}
