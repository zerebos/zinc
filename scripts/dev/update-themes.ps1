<#
.SYNOPSIS
  Refreshes assets/themes from iTerm2-Color-Schemes, fetching only its ghostty/ folder.

.DESCRIPTION
  Replaces every file in assets/themes except README.md, whose commit line it updates. Changes show up in git as
  usual, so check the diff before committing. Schemes whose names Windows can't use as file names (a colon, for one)
  are skipped and listed.

.EXAMPLE
  pwsh scripts/dev/update-themes.ps1
#>
#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$themes = Join-Path $root 'assets\themes'
$readme = Join-Path $themes 'README.md'
$tmp = Join-Path ([IO.Path]::GetTempPath()) "zinc-themes-$([guid]::NewGuid().ToString('N'))"

try {
    $schemes = Join-Path $tmp 'schemes'
    $files = Join-Path $tmp 'files'
    $tar = Join-Path $tmp 'schemes.tar'
    # No checkout: git on Windows refuses to check out a repo with names like "Analog Heartbeat: Phosphor", even with
    # a sparse checkout, because core.protectNTFS keeps them out of its index. So the files come out through git
    # archive, without the names Windows can't use. protectNTFS is off for the archive alone, which writes nothing to
    # disk but the tar.
    git clone --quiet --depth 1 --no-checkout https://github.com/mbadolato/iTerm2-Color-Schemes.git $schemes
    $rev = git -C $schemes log -1 --format='%H %cs'
    $skipped = @(git -C $schemes ls-tree --name-only HEAD ghostty/ |
        Where-Object { (Split-Path -Leaf $_).IndexOfAny([char[]]':*?"<>|') -ge 0 })
    $exclude = $skipped | ForEach-Object { ":(exclude,literal)$_" }
    git -C $schemes -c core.protectNTFS=false archive --format=tar -o $tar HEAD -- LICENSE ghostty @exclude
    New-Item -ItemType Directory $files | Out-Null
    & (Join-Path $env:SystemRoot 'System32\tar.exe') -xf $tar -C $files

    Get-ChildItem $themes -File | Where-Object Name -ne 'README.md' | Remove-Item -Force
    Copy-Item (Join-Path $files 'ghostty\*') $themes
    Copy-Item (Join-Path $files 'LICENSE') (Join-Path $themes 'LICENSE.txt')
    $text = [IO.File]::ReadAllText($readme)
    [IO.File]::WriteAllText($readme, [regex]::Replace($text, 'at commit .*?\. Each', "at commit $rev. Each"))
    Write-Host "$((Get-ChildItem $themes -File).Count) files, $rev"
    foreach ($name in $skipped) { Write-Host "skipped $(Split-Path -Leaf $name) (not a valid Windows file name)" }
} finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
}
