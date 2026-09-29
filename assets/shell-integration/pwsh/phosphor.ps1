# Phosphor's shell integration for PowerShell (7 and Windows PowerShell 5.1).
#
# Phosphor starts PowerShell with -NoExit -Command that runs this file, so it runs after your
# profile. It changes nothing you see: it adds invisible marks around your prompt and commands
# (OSC 133: prompt start, where typing starts, command running, finished with its exit code) and
# reports the current folder (OSC 9;9). Phosphor uses them to jump between prompts, select a
# command's output, mark failed commands and open new tabs in the same folder.
#
# A prompt that already sends these marks (a hand-written integration) is left alone. To turn
# this off: shell-integration = "off" under [terminal] in config.toml (or in one [profile.x]).

if ($Global:__PhosphorIntegration -or $Host.Name -ne 'ConsoleHost') { return }
$Global:__PhosphorIntegration = $true

if ("$function:prompt" -match '133;') { return }

$Global:__PhosphorPrompt = $function:prompt
$Global:__PhosphorFirstPrompt = $true

function Global:prompt {
    # Read the last command's status before anything else changes it.
    $ok = $global:?
    $code = if ($ok) { 0 } elseif ($global:LASTEXITCODE) { $global:LASTEXITCODE } else { 1 }
    $e, $bel = [char]27, [char]7

    $marks = ''
    if (-not $Global:__PhosphorFirstPrompt) { $marks += "$e]133;D;$code$bel" }
    $Global:__PhosphorFirstPrompt = $false
    $marks += "$e]133;A$bel"
    $location = $executionContext.SessionState.Path.CurrentLocation
    if ($location.Provider.Name -eq 'FileSystem') {
        $marks += "$e]9;9;`"$($location.ProviderPath)`"$e\"
    }

    # Your prompt sees the last command's status as it was ($? is false again after a failure).
    if (-not $ok) { Write-Error 'failed' -ErrorAction Ignore }
    $text = $Global:__PhosphorPrompt.Invoke() -join ''

    # Without PSReadLine, typing starts where the prompt ends.
    if (-not $Global:__PhosphorReadLine) { $text += "$e]133;B$bel" }
    $marks + $text
}

# With PSReadLine, typing starts when it starts reading, and the command runs once it has read it.
# (Marking them inside the prompt text would break PSReadLine's prompt redraws.)
if (Test-Path Function:\PSConsoleHostReadLine) {
    $Global:__PhosphorReadLine = $function:PSConsoleHostReadLine
    function Global:PSConsoleHostReadLine {
        [Console]::Write("$([char]27)]133;B$([char]7)")
        $line = & $Global:__PhosphorReadLine
        [Console]::Write("$([char]27)]133;C;cmdline_url=$([uri]::EscapeDataString($line))$([char]7)")
        $line
    }
}
