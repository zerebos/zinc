<#
.SYNOPSIS
  Builds the custom shader translator (GLSL -> SPIR-V -> HLSL) as native\<arch>\zinc-shader.dll.

.DESCRIPTION
  native-src/shader/zinc_shader.zig sits over glslang and SPIRV-Cross with its HLSL backend. glslang is Ghostty's own
  package, from the pinned checkout (libghostty.ps1 fetches it and runs this script too). SPIRV-Cross is built here
  from the source Ghostty pins, because Ghostty's package leaves the HLSL backend out.

  Both are C++, linked with Microsoft's static C and C++ runtimes (as Ghostty's packages compile them), so the DLL
  imports only KERNEL32. Zig doesn't search the folders of those static libraries, so this script finds them with
  vswhere. They come with Visual Studio or its Build Tools (the C++ workload) and a Windows SDK.

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
$target = @{ x64 = 'x86_64-windows-msvc'; arm64 = 'aarch64-windows-msvc' }[$Arch]
$project = Join-Path $root 'native-src\shader'

if (-not (Test-Path (Join-Path $src 'pkg\glslang'))) { throw "No Ghostty checkout at ${src}: run scripts/build/libghostty.ps1 first." }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found: install Visual Studio or its Build Tools (C++ workload).' }
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'No Visual Studio with the C++ tools.' }

# The newest of each, by the version in its path (the last folder named like 14.44.35207 or 10.0.26100.0).
function Newest([string]$pattern) {
    $found = foreach ($dir in Get-Item $pattern -ErrorAction SilentlyContinue) {
        $version = $null
        if ($dir.FullName -match '^.*\\(\d+(\.\d+)+)\\' -and [version]::TryParse($Matches[1], [ref]$version)) {
            [pscustomobject]@{ Path = $dir.FullName; Version = $version }
        }
    }
    $found | Sort-Object Version | Select-Object -Last 1 -ExpandProperty Path
}
$msvcLib = Newest (Join-Path $vs "VC\Tools\MSVC\*\lib\$Arch")
$ucrtLib = Newest (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\Lib\*\ucrt\$Arch")
if (-not $msvcLib -or -not (Test-Path (Join-Path $msvcLib 'libcpmt.lib'))) { throw "No static MSVC runtime (libcpmt.lib) for $Arch in $vs." }
if (-not $ucrtLib -or -not (Test-Path (Join-Path $ucrtLib 'libucrt.lib'))) { throw "No static UCRT (libucrt.lib) for $Arch in the Windows SDK." }

Push-Location $project
try { zig build -Doptimize=ReleaseFast "-Dtarget=$target" "-Ducrt-lib=$ucrtLib" "-Dmsvc-lib=$msvcLib" }
finally { Pop-Location }

$out = Join-Path $root "native\$Arch"
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $project 'zig-out\bin\zinc-shader.dll'), (Join-Path $project 'zig-out\bin\zinc-shader.pdb') $out
Write-Host "zinc-shader ($(git -C $src rev-parse --short HEAD)) -> native\$Arch\zinc-shader.dll"
