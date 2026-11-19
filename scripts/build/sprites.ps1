<#
.SYNOPSIS
  Builds Ghostty's sprite rasterizer as native\<arch>\zinc-sprite.dll, from the pinned Ghostty checkout.

.DESCRIPTION
  The sprites are box drawing, block elements, Symbols for Legacy Computing, Powerline and Braille
  (Ghostty's src/font/sprite). libghostty.ps1 fetches the checkout and runs this script too.

  native-src/sprite/zinc_sprite.zig is the C API. It goes into the checkout as src/zinc_sprite.zig, so that Ghostty's
  relative imports resolve. One line of Ghostty is repointed for the build and restored afterwards, because the sprite
  canvas takes its std.Io from Ghostty's app-wide state (global.zig, which refuses to build outside the app). For the
  build, the canvas uses the wrapper's std.Io instead.

  Needs Zig 0.17.0 on PATH.

.EXAMPLE
  pwsh scripts/build/sprites.ps1
#>
#Requires -Version 7.4
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src = Join-Path $root 'external\ghostty'
. (Join-Path $PSScriptRoot 'zig.ps1')
$canvas = Join-Path $src 'src\font\sprite\canvas.zig'
$wrapper = Join-Path $src 'src\zinc_sprite.zig'
$project = Join-Path $root 'native-src\sprite'

if (-not (Test-Path $canvas)) { throw "No Ghostty checkout at ${src}: run scripts/build/libghostty.ps1 first." }

$original = [IO.File]::ReadAllBytes($canvas)
$text = [IO.File]::ReadAllText($canvas)
if (-not $text.Contains('global.io()')) { throw 'canvas.zig no longer calls global.io(): update this script.' }

Copy-Item (Join-Path $project 'zinc_sprite.zig') $wrapper
try {
    [IO.File]::WriteAllText($canvas, $text.Replace('global.io()', '@import("../../zinc_sprite.zig").io()'))
    Invoke-ZigBuild $project "-Dghostty=$src", '-Doptimize=ReleaseFast', "-Dtarget=$($ZigTargets[$Arch])"
} finally {
    [IO.File]::WriteAllBytes($canvas, $original)
    Remove-Item $wrapper -Force
}

$out = Join-Path $root "native\$Arch"
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $project 'zig-out\bin\zinc-sprite.dll'), (Join-Path $project 'zig-out\bin\zinc-sprite.pdb') $out
Write-Host "zinc-sprite ($(git -C $src rev-parse --short HEAD)) -> native\$Arch\zinc-sprite.dll"
