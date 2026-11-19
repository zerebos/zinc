<#
.SYNOPSIS
  Fetches Ghostty at a pinned commit and builds libghostty-vt as a Windows DLL, then the other native DLLs.

.DESCRIPTION
  Output: native\<arch>\ghostty-vt.dll (and its .pdb) and native\include\ghostty\. It then runs sprites.ps1 and
  shader.ps1, which build zinc-sprite.dll and zinc-shader.dll from the same checkout. All three are built for the GNU
  ABI (zig.ps1 says why).

  Needs Zig 0.17.0 on PATH.

.EXAMPLE
  pwsh scripts/build/libghostty.ps1
  pwsh scripts/build/libghostty.ps1 -Arch arm64
#>
#Requires -Version 7.4
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',
    # The commit of a libghostty-vt release tag (vt-1.0.1 here). Within 1.x the C ABI only gains additions, but bump
    # this deliberately and rerun the tests (AbiTests checks the bindings in src/Zinc.Core/Ghostty/Native.cs against
    # ghostty_type_json()).
    [string]$Commit = '108cacb17ed8990840cd14abfce43d8ed8503ca1'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src = Join-Path $root 'external\ghostty'
. (Join-Path $PSScriptRoot 'zig.ps1')

if (-not (Test-Path (Join-Path $src '.git'))) {
    git init -q $src
    git -C $src remote add origin https://github.com/ghostty-org/ghostty.git
}
# A new checkout has no HEAD yet, so `git rev-parse` is allowed to fail here.
$head = & { $PSNativeCommandUseErrorActionPreference = $false; git -C $src rev-parse HEAD 2>$null }
if ($head -ne $Commit) {
    git -C $src fetch --depth 1 origin $Commit
    git -C $src checkout -q --detach FETCH_HEAD
}

# Apply our fixes that aren't upstream yet (patches/ghostty, where each patch says when to drop it). The sources are
# reset to the pinned commit first, so a second run doesn't apply a patch twice and an edit left in the checkout never
# reaches a build.
git -C $src checkout -q -- .
foreach ($patch in Get-ChildItem (Join-Path $root 'patches\ghostty') -Filter *.patch | Sort-Object Name) {
    git -C $src apply $patch.FullName
    Write-Host "applied $($patch.Name)"
}

Invoke-ZigBuild $src '-Demit-lib-vt=true', '-Doptimize=ReleaseFast', "-Dtarget=$($ZigTargets[$Arch])"

$out = Join-Path $root "native\$Arch"
$include = Join-Path $root 'native\include'
New-Item -ItemType Directory -Force $out, $include | Out-Null
Copy-Item (Join-Path $src 'zig-out\bin\ghostty-vt.dll'), (Join-Path $src 'zig-out\bin\ghostty-vt.pdb') $out
Remove-Item (Join-Path $include 'ghostty') -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $src 'zig-out\include\ghostty') $include -Recurse
Write-Host "libghostty-vt ($Commit) -> native\$Arch\ghostty-vt.dll"

# Ghostty's sprite rasterizer and the custom shader translator, from the same checkout.
& (Join-Path $PSScriptRoot 'sprites.ps1') -Arch $Arch
& (Join-Path $PSScriptRoot 'shader.ps1') -Arch $Arch
