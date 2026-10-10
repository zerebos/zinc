<#
.SYNOPSIS
  Lists what a published Zinc folder references but its zinc.deps.json doesn't list ("Zinc.Core.dll -> Name").

.DESCRIPTION
  .NET loads only the assemblies zinc.deps.json lists. Trimming can leave one out that an untrimmed assembly (ours)
  still needs: dev.342's settings import failed on System.IO.Compression.ZipFile, copied into the folder but not
  listed, which bin\ builds and the tests never show. release.ps1 and install.ps1 refuse a build this finds anything in.
  Prints nothing when every reference is listed.

.EXAMPLE
  pwsh scripts/check-publish.ps1 dist\publish-Zinc
#>
#Requires -Version 7 # System.Reflection.Metadata, which Windows PowerShell doesn't load
param([Parameter(Mandatory)][string]$Folder)

$ErrorActionPreference = 'Stop'
$deps = Get-Content (Join-Path $Folder 'zinc.deps.json') -Raw
$listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[regex]::Matches($deps, '"(?:[^"]*/)?([^"/]+)\.dll"') | ForEach-Object { [void]$listed.Add($_.Groups[1].Value) }

# Known and harmless: Windows (WinRT metadata), the framework's own facades, and System.Drawing's binary-format reader
# (System.Private.Windows.Core -> System.Formats.Nrbf), which nothing in Zinc reaches.
$unlisted = 'Windows', 'netstandard', 'mscorlib', 'System.Formats.Nrbf'

foreach ($file in Get-ChildItem $Folder -Filter *.dll) {
    $stream = [IO.File]::OpenRead($file.FullName)
    $pe = $null
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        if (-not $pe.HasMetadata) { continue } # native
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $metadata.AssemblyReferences) {
            $name = $metadata.GetString($metadata.GetAssemblyReference($handle).Name)
            if (-not $listed.Contains($name) -and $unlisted -notcontains $name) { "$($file.Name) -> $name" }
        }
    } finally {
        if ($pe) { $pe.Dispose() }
        $stream.Dispose()
    }
}
