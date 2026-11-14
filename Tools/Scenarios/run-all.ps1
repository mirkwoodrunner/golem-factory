<#
.SYNOPSIS
  Runs every scenario headless, one after another, and says which passed.

.DESCRIPTION
  Asks the game for its own list of scenarios (an unknown name makes ScenarioRunner print the known
  ones), so there is no second list here to keep up to date. Each scenario runs with
  --headless --fixed-fps 60 and its output goes to <Out>\<name>.log.

  A scenario that prints no verdict within -TimeoutSeconds is ended and counted as failed: a scene
  that fails to parse hangs a scenario run instead of failing it (godot/Scenes/CLAUDE.md).

  Build first (CLAUDE.md, "Commands"); this script runs what is already built.
  Exits 0 when every scenario passed, 1 otherwise.

.EXAMPLE
  ./Tools/Scenarios/run-all.ps1
  ./Tools/Scenarios/run-all.ps1 -Only golems,save
#>
param(
    [string[]]$Only = @(),
    [string]$Out = (Join-Path $PSScriptRoot '..\..\build\scenarios'),
    [string]$Godot = 'G:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\..\godot')).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

# Runs one scenario to its verdict. Returns the exit code, or $null when it had to be ended.
function Invoke-Scenario([string]$name, [string]$log) {
    $process = Start-Process -FilePath $Godot -PassThru -NoNewWindow `
        -ArgumentList @('--headless', '--path', "`"$project`"", '--fixed-fps', '60', '--', '--scenario', $name) `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $null = $process.Handle # held, so ExitCode can be read after the process has gone
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        # The console exe is a wrapper around the game's own process: end the whole tree, or the
        # game runs on after its wrapper has gone.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        return $null
    }
    return $process.ExitCode
}

function Get-Verdict([string]$log) {
    $line = Select-String -Path $log -Pattern '^\[scenario ' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($line) { return $line.Line } else { return '' }
}

# The game's own list: "[scenario ?] FAIL no scenario named '?' (known: a, b, c)".
$listLog = Join-Path $Out '_list.log'
$null = Invoke-Scenario '?' $listLog
$known = Get-Verdict $listLog
if ($known -notmatch '\(known: (.+)\)\s*$') {
    Write-Host "The game did not list its scenarios. Is it built? See $listLog"
    exit 1
}
$names = $Matches[1] -split ',\s*'
if ($Only.Count -gt 0) {
    $unknown = $Only | Where-Object { $names -notcontains $_ }
    if ($unknown) {
        Write-Host "No scenario named: $($unknown -join ', '). Known: $($names -join ', ')"
        exit 1
    }
    $names = $Only
}

$failed = @()
$started = Get-Date
foreach ($name in $names) {
    $log = Join-Path $Out "$name.log"
    $t0 = Get-Date
    $code = Invoke-Scenario $name $log
    $seconds = [int]((Get-Date) - $t0).TotalSeconds
    $verdict = Get-Verdict $log
    if ($null -eq $code) {
        $state = 'HUNG'
        $verdict = "no verdict after $TimeoutSeconds s; see $log"
    }
    elseif ($code -eq 0 -and $verdict -match '^\[scenario \S+\] PASS') {
        $state = 'PASS'
    }
    else {
        $state = 'FAIL'
        if (-not $verdict) { $verdict = "exit $code with no verdict; see $log" }
    }
    if ($state -ne 'PASS') { $failed += $name }
    $detail = $verdict -replace '^\[scenario \S+\] (PASS|FAIL) ?', ''
    if ($detail.Length -gt 110) { $detail = $detail.Substring(0, 110) + '...' }
    Write-Host ('{0,-4} {1,-14} {2,4} s  {3}' -f $state, $name, $seconds, $detail)
}

$total = [int]((Get-Date) - $started).TotalSeconds
if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) of $($names.Count) failed in $total s: $($failed -join ', '). Logs: $Out"
    exit 1
}
Write-Host "All $($names.Count) scenarios passed in $total s. Logs: $Out"
exit 0
