# Shared by the build scripts, which dot-source it. It holds the Zig target for each architecture and `zig build` with
# a retry.

# The GNU ABI, as libghostty's own Windows builds use, so the DLLs import only Windows' Universal CRT. Zinc then ships
# no C or C++ runtime of its own, and building the DLLs needs no Visual Studio.
$ZigTargets = @{ x64 = 'x86_64-windows-gnu'; arm64 = 'aarch64-windows-gnu' }

# Runs `zig build` in $Folder. Zig 0.16 on Windows sometimes fails to read a file it compiles or caches ("unable to
# load 'verify.zig': Unexpected", or "Constant.cpp:1:1: error: CacheCheckFailed", a different file each time). Each
# run caches what finished, so running again gets further. A failure like that is retried a few times, and any other
# failure stops the script.
function Invoke-ZigBuild([string]$Folder, [string[]]$Arguments) {
    $tries = 5
    Push-Location $Folder
    try {
        for ($try = 1; ; $try++) {
            $lines = & {
                $PSNativeCommandUseErrorActionPreference = $false
                zig build @Arguments 2>&1 | ForEach-Object { $line = "$_"; Write-Host $line; $line }
            }
            if ($LASTEXITCODE -eq 0) { return }
            if ($try -lt $tries -and ($lines -match '\b(Unexpected|CacheCheckFailed)\s*$')) {
                Write-Host "Zig couldn't read one of the files it builds, so the build runs again ($($try + 1) of $tries)."
                continue
            }
            throw "zig build failed in $Folder."
        }
    } finally {
        Pop-Location
    }
}
