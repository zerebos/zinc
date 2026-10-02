<#
.SYNOPSIS
  Builds a portable Zinc zip to share with testers.

.DESCRIPTION
  Publishes a Release build with everything it needs inside (the .NET runtime and the Windows App
  SDK; nothing to install) into dist\Zinc-<version>-win-x64\, adds the tester notes
  (scripts\package-readme.txt, as README.txt), and zips that folder as
  dist\Zinc-<version>-win-x64.zip. The version is computed as Directory.Build.props and install.ps1
  do; a build with uncommitted changes gets "-dirty".

  The zip runs from any folder: unzip, open zinc.exe. Settings go to %APPDATA%\Zinc as for an
  installed copy. Launch-at-login and the default-terminal registration follow whichever zinc.exe
  last started, so a moved folder fixes itself on its next start.

.EXAMPLE
  pwsh scripts/package.ps1
  pwsh scripts/package.ps1 -Out C:\Temp\zinc-builds
#>
param([string]$Out)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $repo 'dist' }

if (-not (Test-Path (Join-Path $repo 'native\x64\ghostty-vt.dll'))) {
    throw 'native\x64\ghostty-vt.dll missing: run scripts/build-libghostty.sh x64 first.'
}

# ---- version (as Directory.Build.props computes it) ----
$rev = (git -C $repo rev-parse --short=7 HEAD).Trim()
if (git -C $repo status --porcelain --untracked-files=no) { $rev += '-dirty' }
$base = [regex]::Match((Get-Content (Join-Path $repo 'Directory.Build.props') -Raw), '<PhosphorVersion>(.+?)</PhosphorVersion>').Groups[1].Value
$commits = (git -C $repo rev-list --count HEAD).Trim()
git -C $repo describe --exact-match --tags --match "v$base" HEAD 2>$null | Out-Null
$version = if ($LASTEXITCODE -eq 0) { "$base+$rev" } else { "$base-dev.$commits+$rev" }
# '+' is fine in a file name, but some upload forms and chat apps mangle it.
$name = "Zinc-$($version.Replace('+', '_'))-win-x64"
$folder = Join-Path $Out $name
$zip = "$folder.zip"

# ---- publish ----
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
Write-Host "Publishing $version ..."
$log = dotnet publish (Join-Path $repo 'src\Phosphor\Phosphor.csproj') -c Release -r win-x64 --self-contained true -o $folder -nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    $log | Write-Host
    throw 'dotnet publish failed.'
}

# Everything a tester needs must be inside (a framework-dependent build would ask for .NET 10).
foreach ($file in 'zinc.exe', 'coreclr.dll', 'hostfxr.dll', 'ghostty-vt.dll', 'phosphor-sprite.dll', 'zinc.pri', 'Microsoft.Graphics.Canvas.dll') {
    if (-not (Test-Path (Join-Path $folder $file))) { throw "$file is missing from the build: not packaging it." }
}

(Get-Content (Join-Path $PSScriptRoot 'package-readme.txt') -Raw).Replace('{version}', $version) |
    Set-Content -Encoding utf8 (Join-Path $folder 'README.txt')

# ---- zip (one top-level folder, so unzipping doesn't scatter 1,000 files) ----
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $folder -Recurse -Force

$size = (Get-Item $zip).Length / 1MB
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("{0} ({1:N0} MB)" -f $zip, $size)
Write-Host "SHA-256 $hash"
