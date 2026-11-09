<#
.SYNOPSIS
  Builds the custom shader translator (GLSL -> SPIR-V -> HLSL) as native\<arch>\zinc-shader.dll.

.DESCRIPTION
  native-src/shader/zinc_shader.zig sits over glslang and SPIRV-Cross with its HLSL backend. glslang is Ghostty's own
  package, from the pinned checkout (libghostty.ps1 fetches it and runs this script too). SPIRV-Cross is built here
  from the source Ghostty pins, because Ghostty's package leaves the HLSL backend out. Both are C++, linked with Zig's
  own static libc++, so the DLL imports only Windows' Universal CRT and KERNEL32.

  Needs Zig 0.16.x on PATH.

.EXAMPLE
  pwsh scripts/build/shader.ps1
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
$project = Join-Path $root 'native-src\shader'
. (Join-Path $PSScriptRoot 'zig.ps1')

if (-not (Test-Path (Join-Path $src 'pkg\glslang'))) { throw "No Ghostty checkout at ${src}: run scripts/build/libghostty.ps1 first." }

Invoke-ZigBuild $project '-Doptimize=ReleaseFast', "-Dtarget=$($ZigTargets[$Arch])"

$out = Join-Path $root "native\$Arch"
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $project 'zig-out\bin\zinc-shader.dll'), (Join-Path $project 'zig-out\bin\zinc-shader.pdb') $out
Write-Host "zinc-shader ($(git -C $src rev-parse --short HEAD)) -> native\$Arch\zinc-shader.dll"
