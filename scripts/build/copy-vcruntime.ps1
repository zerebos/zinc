<#
.SYNOPSIS
  Copies the Visual C++ runtime (vcruntime140.dll) next to the native DLLs, as native\<arch>\vcruntime140.dll.

.DESCRIPTION
  ghostty-vt.dll and zinc-sprite.dll are built for Zig's windows-msvc target, so they import vcruntime140.dll, which
  isn't part of Windows. Most PCs have it from some other app, but a fresh one (Windows Sandbox, a new install)
  doesn't, and Zinc then fails with "Unable to load DLL 'ghostty-vt'". Microsoft allows apps to carry it in their own
  folder ("app-local"), and the copy in Visual Studio's VC\Redist folder is the one meant for that.

  libghostty.ps1 runs this script. Needs Visual Studio or its Build Tools with the C++ workload, for the Redist folder.

.EXAMPLE
  pwsh scripts/build/copy-vcruntime.ps1
#>
#Requires -Version 7.4
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found: install Visual Studio or its Build Tools (C++ workload).' }

# The newest redistributable among every installed Visual Studio, because a newer runtime runs older programs' code.
# Each path is ...\MSVC\<version>\<arch>\Microsoft.VC14x.CRT\vcruntime140.dll, so it sorts on <version>.
$found = foreach ($install in & $vswhere -all -products '*' -property installationPath) {
    foreach ($file in Get-Item (Join-Path $install "VC\Redist\MSVC\*\$Arch\Microsoft.VC14*.CRT\vcruntime140.dll") -ErrorAction SilentlyContinue) {
        $version = $null
        if ([version]::TryParse($file.Directory.Parent.Parent.Name, [ref]$version)) {
            [pscustomobject]@{ File = $file; Version = $version }
        }
    }
}
$dll = $found | Sort-Object Version | Select-Object -Last 1 -ExpandProperty File
if (-not $dll) { throw "No $Arch vcruntime140.dll in any Visual Studio's VC\Redist folder." }

$out = Join-Path $root "native\$Arch"
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item $dll.FullName $out
Write-Host "vcruntime140.dll ($($dll.Directory.Parent.Parent.Name)) -> native\$Arch\vcruntime140.dll"
